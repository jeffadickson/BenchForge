"""Tests for full ExternalInstrumentSource inventory discovery without importing upstream code."""
import importlib.util
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch

MODULE_PATH = Path(__file__).resolve().parents[2] / "tools" / "InstrumentDefiner" / "discover_catalog.py"
spec = importlib.util.spec_from_file_location("discover_catalog", MODULE_PATH)
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)


class CatalogDiscoveryTests(unittest.TestCase):
    """Regression tests for catalog discovery tests."""
    def test_inventory_includes_classes_and_reports_parse_errors(self):
        """Verify inventory includes classes and reports parse errors."""
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            instruments = root / "external_instrument_source" / "instruments" / "acme"
            instruments.mkdir(parents=True)
            (instruments / "meter.py").write_text(
                "class Meter(BaseMeter):\n    def measure(self):\n        pass\n",
                encoding="utf-8")
            (instruments / "broken.py").write_text("class Broken(\n", encoding="utf-8")
            with patch.object(module.subprocess, "check_output", return_value="a" * 40 + "\n"):
                result = module.discover(root)
            self.assertEqual(1, len(result["classes"]))
            self.assertEqual("Meter", result["classes"][0]["className"])
            self.assertEqual(["BaseMeter"], result["classes"][0]["bases"])
            self.assertEqual(1, len(result["parseErrors"]))

    def test_missing_source_tree_fails(self):
        """Verify missing source tree fails."""
        with tempfile.TemporaryDirectory() as directory:
            with self.assertRaises(ValueError):
                module.discover(Path(directory))


if __name__ == "__main__":
    unittest.main()
