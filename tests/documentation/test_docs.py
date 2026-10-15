"""Regression tests for documentation gap tracking and AST-only inspection."""
import importlib.util
import json
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch

SPEC = importlib.util.spec_from_file_location("check_api_docs", Path(__file__).resolve().parents[2] / "scripts/check_api_docs.py")
docs = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(docs)


class DocumentationGateTests(unittest.TestCase):
    """Exercise failures that could otherwise silently waive missing comments."""

    def test_generated_gaps_fail_even_in_baseline(self):
        """Generated output must never inherit legacy documentation exceptions."""
        rows = [dict(path="src/BenchDouble/Generated/Test.g.cs", symbol="Test", issues=["summary"])]
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            (root / "build").mkdir()
            baseline = root / "baseline.json"
            baseline.write_text(json.dumps(docs.gaps(rows)))
            with patch.object(docs, "ROOT", root), patch.object(docs, "BASELINE", baseline), patch.object(docs, "collect", return_value=rows):
                self.assertEqual(docs.main(), 1)

    def test_new_gap_fails_and_resolution_passes(self):
        """A new missing summary fails; fixing existing comments needs no waiver."""
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            (root / "build").mkdir()
            baseline = root / "baseline.json"
            baseline.write_text('["legacy::item::summary"]')
            with patch.object(docs, "ROOT", root), patch.object(docs, "BASELINE", baseline):
                with patch.object(docs, "collect", return_value=[dict(path="new.cs", symbol="New", issues=["summary"])]):
                    self.assertEqual(docs.main(), 1)
                with patch.object(docs, "collect", return_value=[]):
                    self.assertEqual(docs.main(), 0)

    def test_python_source_is_never_executed(self):
        """Import side effects must not run while collecting nested Python gaps."""
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            (root / "build").mkdir()
            (root / "build/api-audit.json").write_text("[]")
            (root / "tools").mkdir()
            (root / "tools/example.py").write_text('raise RuntimeError("must not run")\nclass Example:\n    def method(self):\n        pass\n')
            rows = docs.collect(root)
            self.assertEqual({r["symbol"] for r in rows}, {"<module>", "Example", "Example.method"})
            self.assertTrue(all(r["issues"] == ["docstring"] for r in rows))


if __name__ == "__main__":
    unittest.main()
