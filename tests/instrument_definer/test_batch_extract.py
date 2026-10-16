"""Batch extraction tests using synthetic Python files, never imported."""
import sys
import tempfile
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "tools/InstrumentDefiner"))
import batch_extract  # noqa: E402


class BatchExtractTests(unittest.TestCase):
    """Regression tests for batch extract tests."""
    def test_extracts_multiple_models_and_reports_coverage(self):
        """Verify extracts multiple models and reports coverage."""
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder)
            sources = root / "sources"
            sources.mkdir()
            (sources / "a.py").write_text(
                'class A(Instrument):\n'
                '    voltage = Instrument.measurement("V?", "Voltage")\n',
                encoding="utf-8")
            (sources / "b.py").write_text(
                'class B(Instrument):\n'
                '    resistance = Instrument.measurement("R?", "Resistance")\n',
                encoding="utf-8")
            manifest = root / "manifest.csv"
            manifest.write_text(
                "manufacturer,model,className,sourcePath,revision\n"
                "Acme,A,A,a.py," + "a"*40 + "\n"
                "Acme,B,B,b.py," + "b"*40 + "\n", encoding="utf-8")
            result = batch_extract.process(manifest, sources, root / "out")
            self.assertEqual(2, result["extracted"])
            self.assertEqual(0, result["failed"])
            self.assertEqual(2, len(list((root / "out").glob("*.definition.json"))))
            self.assertGreaterEqual(result["entries"][0]["unresolvedInherited"], 1)

    def test_path_traversal_is_rejected(self):
        """Verify path traversal is rejected."""
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder)
            source = root / "source"
            source.mkdir()
            manifest = root / "manifest.csv"
            manifest.write_text(
                "manufacturer,model,className,sourcePath,revision\n"
                "Acme,X,X,../outside.py,rev\n", encoding="utf-8")
            result = batch_extract.process(manifest, source, root / "out")
            self.assertEqual(1, result["failed"])
            self.assertIn("outside source root", result["entries"][0]["error"])

    def test_incomplete_manifest_row_reports_failure_without_crashing(self):
        """Verify incomplete manifest row reports failure without crashing."""
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder)
            source = root / "sources"
            source.mkdir()
            manifest = root / "manifest.csv"
            manifest.write_text(
                "manufacturer,model,className,sourcePath,revision\\n"
                "Acme,X,,,\\n", encoding="utf-8")
            result = batch_extract.process(manifest, source, root / "out")
            self.assertEqual(1, result["failed"])
            self.assertIn("required", result["entries"][0]["error"])

    def test_does_not_overwrite_existing_definition(self):
        """Verify does not overwrite existing definition."""
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder)
            source = root / "sources"
            source.mkdir()
            (source / "a.py").write_text(
                'class A:\n    def reset(self): pass\n', encoding="utf-8")
            manifest = root / "manifest.csv"
            manifest.write_text(
                "manufacturer,model,className,sourcePath,revision\n"
                "Acme,A,A,a.py," + "a"*40 + "\n", encoding="utf-8")
            destination = root / "out"
            destination.mkdir()
            existing = destination / "acme-a.definition.json"
            existing.write_text("reviewed", encoding="utf-8")
            result = batch_extract.process(manifest, source, destination)
            self.assertEqual(1, result["failed"])
            self.assertEqual("reviewed", existing.read_text(encoding="utf-8"))


if __name__ == "__main__":
    unittest.main()
