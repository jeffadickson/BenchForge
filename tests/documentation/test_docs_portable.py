"""Tests for the standalone documentation preflight."""
import importlib.util
import tempfile
import unittest
from pathlib import Path

SCRIPT = Path(__file__).resolve().parents[2] / "scripts" / "check_docs_portable.py"
spec = importlib.util.spec_from_file_location("check_docs_portable", SCRIPT)
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)


class PortableDocumentationTests(unittest.TestCase):
    """Check documented and undocumented C# and Python without .NET."""

    def test_missing_documentation(self):
        """Find undocumented C# methods, parameters and Python functions."""
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            (root / "src").mkdir()
            (root / "tests").mkdir()
            (root / "src" / "Meter.cs").write_text(
                "public class Meter { public void Write(int channel) {} }")
            (root / "tests" / "sample.py").write_text("def f():\n    return 1\n")
            self.assertGreaterEqual(module.audit(root)["gapCount"], 3)

    def test_primary_record_and_constructor_parameters(self):
        """Detect missing record positional and explicit constructor XML parameters."""
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            (root / "src").mkdir()
            (root / "src" / "Records.cs").write_text(
                '/// <summary>Capture metadata.</summary>\n'
                'public record Capture(int Channel, double Voltage);\n'
                'public class Meter {\n'
                '    /// <summary>Create a meter.</summary>\n'
                '    public Meter(int channel) {}\n'
                '}\n')
            findings = module.scan_csharp(root / "src" / "Records.cs", root)
            issues = {issue for item in findings for issue in item["issues"]}
            self.assertIn("param:Channel", issues)
            self.assertIn("param:Voltage", issues)
            self.assertIn("param:channel", issues)

    def test_complete_documentation(self):
        """Accept fully documented C# methods and Python definitions."""
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            (root / "src").mkdir()
            (root / "tests").mkdir()
            (root / "src" / "Meter.cs").write_text(
                '/// <summary>Meter.</summary>\npublic class Meter {\n'
                '/// <summary>Read.</summary>\n'
                '/// <param name="channel">Channel.</param>\n'
                '/// <returns>Reading.</returns>\n'
                'public double Read(int channel) => 1;\n}')
            (root / "tests" / "sample.py").write_text(
                '"""Tests."""\ndef f():\n    """Return a value."""\n    return 1\n')
            self.assertEqual(module.audit(root)["gapCount"], 0)


if __name__ == "__main__":
    unittest.main()
