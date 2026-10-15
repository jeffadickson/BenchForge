"""No-hardware, no-PyMeasure-import tests for Instrument Definer."""
import importlib.util
import json
import tempfile
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
SCRIPT = ROOT / "tools/InstrumentDefiner/definer.py"
spec = importlib.util.spec_from_file_location("instrument_definer", SCRIPT)
definer = importlib.util.module_from_spec(spec)
spec.loader.exec_module(definer)


class InstrumentDefinerTests(unittest.TestCase):
    def test_extract_is_static_and_preserves_command_metadata(self):
        with tempfile.TemporaryDirectory() as folder:
            path = Path(folder) / "driver.py"
            path.write_text("""
raise RuntimeError("MUST NOT EXECUTE")
class Meter(Instrument):
    voltage = Instrument.measurement("MEAS:VOLT?", "Voltage")
    current = Instrument.control("SOUR:CURR?", "SOUR:CURR %g", "Current",
                                 values=[0, 1])
    def reset(self):
        raise RuntimeError("MUST NOT EXECUTE")
""", encoding="utf-8")
            doc = definer.extract(path, "Example", "123", "pinned-sha")
            self.assertEqual(2, len(doc["capabilities"]))
            self.assertEqual(1, len(doc["operations"]))
            self.assertEqual("MEAS:VOLT?", doc["capabilities"][0]["query"])
            self.assertEqual("SOUR:CURR %g", doc["capabilities"][1]["set"])
            self.assertEqual(4, doc["capabilities"][0]["sourceLine"])
            self.assertEqual([], definer.validate(doc))
            self.assertEqual("unreviewed", doc["review"]["status"])

    def test_quantity_dimension_mismatch_rejected(self):
        doc = definer.draft("Example", "123", "Example123", source="example.py")
        doc["capabilities"] = [{
            "name": "resistance", "kind": "measurement", "access": "read",
            "quantity": "ElectricResistance", "unit": "Volt", "query": "RES?",
            "set": None, "modes": [], "sourceLine": 10
        }]
        self.assertTrue(any("quantity/unit" in error for error in definer.validate(doc)))

    def test_mock_default_outside_range_rejected(self):
        doc = definer.draft("Example", "123", "Example123", source="example.py")
        doc["capabilities"] = [{
            "name": "voltage", "kind": "control", "access": "readwrite",
            "quantity": "ElectricPotential", "unit": "Volt", "query": "V?",
            "set": "V %g", "minimum": 0, "maximum": 5, "mockDefault": 10,
            "modes": [], "sourceLine": 10
        }]
        self.assertTrue(any("mockDefault above" in error for error in definer.validate(doc)))

    def test_hardware_validation_cannot_be_self_declared(self):
        doc = definer.draft("Example", "123", "Example123", source="example.py")
        doc["operations"] = [{"name": "reset"}]
        doc["review"]["status"] = "hardwareValidated"
        self.assertTrue(any("Hardware validation" in error for error in definer.validate(doc)))

    def test_csv_import_preserves_modes_and_dimensions(self):
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder)
            properties = root / "properties.csv"
            properties.write_text(
                "name,kind,access,quantity,unit,query,set,mode,minimum,maximum,sourceLine,notes\n"
                "resistance,measurement,read,ElectricResistance,Ohm,MEAS:RES?,,resistance,,,,Review mode\n",
                encoding="utf-8")
            doc = definer.import_csv(properties, None, "Example", "123",
                                     "Example123", "Vendor manual")
            self.assertEqual("resistance", doc["modes"][0]["id"])
            self.assertEqual(["resistance"], doc["capabilities"][0]["modes"])
            self.assertEqual([], definer.validate(doc))

    def test_example_keithley_draft_passes_validation(self):
        path = ROOT / "instrument-definitions/drafts/keithley2400.definition.json"
        doc = json.loads(path.read_text(encoding="utf-8"))
        self.assertEqual([], definer.validate(doc))
        self.assertFalse(doc["review"]["hardwareTested"])

    def test_output_file_is_never_overwritten(self):
        with tempfile.TemporaryDirectory() as folder:
            path = Path(folder) / "draft.json"
            path.write_text("original", encoding="utf-8")
            with self.assertRaises(FileExistsError):
                definer.save(path, {})
            self.assertEqual("original", path.read_text(encoding="utf-8"))


if __name__ == "__main__":
    unittest.main()
