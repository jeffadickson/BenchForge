"""Description normalization and unit inference safety tests."""
import importlib.util
import unittest
from pathlib import Path

path = Path(__file__).resolve().parents[2] / "tools" / "InstrumentDefiner" / "normalize_descriptions.py"
spec = importlib.util.spec_from_file_location("normalize_descriptions", path)
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)


class NormalizeDescriptionsTests(unittest.TestCase):
    def test_shortens_boilerplate_without_losing_original(self):
        self.assertEqual("Voltage reading in volts.", module.concise("Gets the voltage reading in volts"))
    def test_unit_is_review_candidate_only(self):
        self.assertEqual(("ElectricResistance", "Ohm", "candidate-unreviewed"),
                         module.unit_candidate("resistance", "measure resistance"))
    def test_ambiguous_unit_is_not_assigned(self):
        self.assertEqual(("", "", "ambiguous"),
                         module.unit_candidate("power", "voltage and current"))
    def test_unknown_unit_is_not_guessed(self):
        self.assertEqual(("", "", "unknown"), module.unit_candidate("status", "instrument ready"))


if __name__ == "__main__":
    unittest.main()
