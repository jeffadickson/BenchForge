"""Tests for comparison between local C# preflight and Roslyn."""
import importlib.util
import unittest
from pathlib import Path

SCRIPT = Path(__file__).resolve().parents[2] / "scripts" / "compare_doc_audits.py"
spec = importlib.util.spec_from_file_location("compare_doc_audits", SCRIPT)
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)


class DocumentationParityTests(unittest.TestCase):
    """Ensure parity diagnostics distinguish matched and missed findings."""

    def test_matched_and_missed_issues(self):
        """Compare issue categories and nearby declaration line numbers."""
        expected = [{"path": "src/Meter.cs", "line": 10,
                     "issues": ["summary", "param:channel"]}]
        observed = [{"path": "src/Meter.cs", "line": 11,
                     "issues": ["summary", "returns"]}]
        report = module.compare(expected, observed)
        self.assertEqual(1, report["matched"])
        self.assertEqual(1, len(report["missed"]))
        self.assertEqual(1, len(report["portableOnly"]))
        self.assertEqual(0.5, report["recall"])

    def test_no_issues(self):
        """Treat matching clean reports as full agreement."""
        report = module.compare([], [])
        self.assertEqual(1.0, report["recall"])
        self.assertEqual(1.0, report["precision"])


if __name__ == "__main__":
    unittest.main()
