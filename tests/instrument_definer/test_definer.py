"""No-hardware, no-ExternalInstrumentSource-import tests for Instrument Definer."""
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
    def extract_source(self, source, class_name=None):
        """Extract disposable source without importing it."""
        with tempfile.TemporaryDirectory() as folder:
            path = Path(folder) / "driver.py"
            path.write_text(source, encoding="utf-8")
            return definer.extract(path, "Example", "123", "pinned-sha", class_name)

    def test_complete_async_signature_is_preserved_without_execution(self):
        doc = self.extract_source('''
class Meter(Instrument):
    async def acquire(self, channel: int, /, count=explode(), *values, timeout=5, **options):
        pass
''')
        op = doc["operations"][0]
        self.assertEqual(["channel", "count", "*values", "timeout", "**options"], op["arguments"])
        self.assertTrue(op["async"])
        self.assertIn("count=explode()", op["notes"])
        self.assertIn("/", op["notes"])
        self.assertIn("Inherited members are not extracted", doc["review"]["unresolved"][-1])
        self.assertEqual("Meter", doc["provenance"]["upstreamClass"])

    def test_static_and_class_method_receivers(self):
        doc = self.extract_source('''
class Meter:
    @staticmethod
    def static(self, *, flag=True): pass
    @classmethod
    def create(cls, address, /): pass
    def reset(self): pass
''')
        self.assertEqual([["self", "flag"], ["address"], []],
                         [op["arguments"] for op in doc["operations"]])

    def test_ambiguous_class_requires_explicit_selection(self):
        source = "class First:\n    def reset(self): pass\nclass Second:\n    def read(self): pass\n"
        with self.assertRaisesRegex(ValueError, "--class-name"):
            self.extract_source(source)
        doc = self.extract_source(source, "Second")
        self.assertEqual("Second", doc["identity"]["driverName"])
        with self.assertRaisesRegex(ValueError, "--class-name"):
            self.extract_source(source, "Missing")

    def test_inherited_and_dynamic_members_are_reported_not_silently_omitted(self):
        """Verify inherited and dynamic members are reported not silently omitted."""
        doc = self.extract_source('''
class Base:
    inherited = Instrument.measurement("BASE?", "base")
    def reset(self): pass

class Meter(Base, ExternalMixin):
    direct = Instrument.measurement("READ?", "reading")
    def __getattr__(self, name): return 1
    def install(self):
        setattr(self, "dynamic", 1)
''', "Meter")
        coverage = doc["extraction"]
        self.assertIn("Base.inherited", coverage["inheritedCandidates"])
        self.assertIn("Base.reset", coverage["inheritedCandidates"])
        self.assertTrue(any("ExternalMixin" in item
                            for item in coverage["inheritedCandidates"]))
        self.assertGreaterEqual(coverage["unresolvedInheritedMembers"], 3)
        self.assertGreaterEqual(coverage["unresolvedDynamicMembers"], 2)
        self.assertEqual(2, coverage["directMembers"])
        self.assertEqual(["direct"], [x["name"] for x in doc["capabilities"]])

    def test_unrelated_descriptor_is_not_extracted(self):
        doc = self.extract_source('''
class Meter:
    fake = measurement("NOT A EXTERNAL_INSTRUMENT_SOURCE DESCRIPTOR")
    voltage = Instrument.measurement("V?", "Voltage")
''')
        self.assertEqual(["voltage"], [cap["name"] for cap in doc["capabilities"]])

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

    def test_intern_templates_import_with_command_provenance(self):
        templates = ROOT / "tools/InstrumentDefiner/templates"
        doc = definer.import_csv(
            templates / "properties.csv", templates / "methods.csv",
            "Keithley", "2400", "Keithley2400", "Vendor manual")
        self.assertEqual([], definer.validate(doc))
        self.assertEqual(2, len(doc["capabilities"]))
        self.assertEqual(2, len(doc["operations"]))
        self.assertEqual("OUTPUT 1", doc["operations"][0]["commands"])
        self.assertEqual("PINNED_SHA", doc["capabilities"][0]["sourceRevision"])

    def test_output_file_is_never_overwritten(self):
        with tempfile.TemporaryDirectory() as folder:
            path = Path(folder) / "draft.json"
            path.write_text("original", encoding="utf-8")
            with self.assertRaises(FileExistsError):
                definer.save(path, {})
            self.assertEqual("original", path.read_text(encoding="utf-8"))


if __name__ == "__main__":
    unittest.main()

