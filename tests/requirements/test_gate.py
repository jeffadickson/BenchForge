"""Fault-injection checks for the requirements gate, with isolated file copies."""
import csv
import importlib.util
import shutil
import tempfile
import unittest
from contextlib import redirect_stdout, redirect_stderr
from io import StringIO
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]


class RequirementGateTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        """Copy immutable source evidence once; each case restores mutable tracker files."""
        cls.temp = tempfile.TemporaryDirectory()
        cls.root = Path(cls.temp.name) / 'repo'
        try:
            shutil.copytree(ROOT, cls.root, ignore=shutil.ignore_patterns('.git', '.tools', '__pycache__', 'bin', 'obj', 'build', '.verification'))
            cls.tracker_files = {path: path.read_bytes() for path in (cls.root / 'requirements').iterdir() if path.is_file()}
        except BaseException:
            cls.temp.cleanup()
            raise

    @classmethod
    def tearDownClass(cls):
        """Remove the shared evidence fixture after all sequential unittest cases finish."""
        cls.temp.cleanup()

    def setUp(self):
        # Fault cases only mutate tracker metadata. Restore it before every case so
        # missing/duplicate/status changes cannot leak into the next test.
        for path, content in self.tracker_files.items():
            path.write_bytes(content)
        spec = importlib.util.spec_from_file_location('gate', self.root / 'scripts/check_requirements.py')
        self.gate = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(self.gate)

    def run_gate(self):
        with redirect_stdout(StringIO()), redirect_stderr(StringIO()):
            return self.gate.main()

    def mutate_csv(self, path, change):
        path = self.root / path
        with path.open(newline='') as f:
            rows = list(csv.DictReader(f))
        change(rows)
        with path.open('w', newline='') as f:
            writer = csv.DictWriter(f, fieldnames=rows[0].keys())
            writer.writeheader()
            writer.writerows(rows)

    def test_current_baseline_passes(self):
        self.assertEqual(0, self.run_gate())

    def test_missing_full_audit_row_fails(self):
        self.mutate_csv('requirements/full-audit.csv', lambda rows: rows.pop())
        self.assertEqual(1, self.run_gate())

    def test_changed_statement_requires_review(self):
        path = self.root / 'requirements/benchdouble.sdoc'
        path.write_text(path.read_text().replace('STATEMENT: ', 'STATEMENT: Changed ', 1))
        self.assertEqual(1, self.run_gate())

    def test_missing_issue_fails_for_source_status_too(self):
        self.mutate_csv('requirements/traceability.csv', lambda rows: rows[-1].update(github_issue=''))
        self.assertEqual(1, self.run_gate())

    def test_test_file_alone_cannot_establish_verified(self):
        for path in ('requirements/traceability.csv', 'requirements/full-audit.csv'):
            self.mutate_csv(path, lambda rows: rows[-1].update(implementation_status='Verified'))
        self.assertEqual(1, self.run_gate())

    def test_missing_acceptance_fails(self):
        self.mutate_csv('requirements/full-audit.csv', lambda rows: rows[-1].update(acceptance_criteria=''))
        self.assertEqual(1, self.run_gate())

    def test_duplicate_full_audit_row_fails(self):
        self.mutate_csv('requirements/full-audit.csv', lambda rows: rows.append(rows[-1].copy()))
        self.assertEqual(1, self.run_gate())


if __name__ == '__main__':
    unittest.main()
