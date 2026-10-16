"""Unit tests for the source-neutrality audit."""
import importlib.util
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch

MODULE = Path(__file__).resolve().parents[2] / "scripts" / "check_source_neutrality.py"
spec = importlib.util.spec_from_file_location("check_source_neutrality", MODULE)
audit_module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(audit_module)


class SourceNeutralityTests(unittest.TestCase):
    def test_legal_register_is_only_exemption(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            (root / "THIRD_PARTY_REVIEW.md").write_text("Py" + "Measure\n")
            (root / "README.md").write_text("BenchForge only\n")
            with patch.object(audit_module, "ROOT", root), patch.object(
                audit_module, "tracked_files",
                return_value=["THIRD_PARTY_REVIEW.md", "README.md"]):
                self.assertEqual([], audit_module.audit())

    def test_content_and_filenames_are_reported(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            (root / "README.md").write_text("Legacy Py" + "Measure reference\n")
            (root / ("Py" + "Measure-note.md")).write_text("unrelated text\n")
            with patch.object(audit_module, "ROOT", root), patch.object(
                audit_module, "tracked_files",
                return_value=["README.md", "Py" + "Measure-note.md"]):
                result = audit_module.audit()
                self.assertEqual(2, len(result))
                self.assertEqual({"content", "filename"}, {x["kind"] for x in result})


if __name__ == "__main__":
    unittest.main()
