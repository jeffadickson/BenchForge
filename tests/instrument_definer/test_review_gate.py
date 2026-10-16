"""Evidence gate tests: never infer review or hardware validation from a mock."""
import copy
import json
import sys
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "tools/InstrumentDefiner"))
import review_gate  # noqa: E402


def approved_fixture():
    """Fully specified synthetic metadata, not a real hardware approval."""
    return {
        "schemaVersion": 2, "definitionStatus": "manualReviewed",
        "identity": {
            "manufacturer": "Example", "model": "DMM1",
            "driverName": "ExampleDMM1", "instrumentClass": "DigitalMultimeter",
            "protocol": "SCPI", "interfaces": ["USB"]
        },
        "provenance": {
            "upstreamProject": "ExternalInstrumentSource", "sourcePath": "drivers/dmm.py",
            "sourceRevision": "a" * 40, "license": "MIT",
            "manualReference": "Programming guide revision 2"
        },
        "modes": [{"id": "voltage", "selectionCommand": "CONF:VOLT"}],
        "capabilities": [{
            "name": "voltage", "kind": "measurement", "access": "read",
            "quantity": "ElectricPotential", "unit": "Volt", "query": "READ?",
            "set": None, "modes": ["voltage"], "mockDefault": 0.25,
            "sourceLine": 17, "reviewStatus": "manualReviewed"
        }],
        "operations": [],
        "mock": {"initialize": [], "measurementModels": [],
                 "virtualTime": True, "realTimeDelayScale": 1.0},
        "review": {
            "status": "manualReviewed", "reviewer": "Fixture Reviewer",
            "hardwareTested": False, "unresolved": [],
            "manualEvidence": "docs/reviewed-programming-guide-checklist.md"
        },
        "extraction": {
            "unresolvedInheritedMembers": 0,
            "unresolvedDynamicMembers": 0
        }
    }


class ReviewGateTests(unittest.TestCase):
    """Regression tests for review gate tests."""
    def test_current_illustrative_draft_cannot_be_generated(self):
        """Verify current illustrative draft cannot be generated."""
        path = ROOT / "instrument-definitions/drafts/keithley2400.definition.json"
        doc = json.loads(path.read_text(encoding="utf-8"))
        result = review_gate.audit(doc)
        self.assertFalse(result["ready"])
        self.assertTrue(result["blockers"])

    def test_complete_synthetic_metadata_passes_only_metadata_gate(self):
        """Verify complete synthetic metadata passes only metadata gate."""
        result = review_gate.audit(approved_fixture())
        self.assertTrue(result["ready"], result["blockers"])
        self.assertIn("Metadata gate only", result["disclaimer"])
        self.assertEqual(1, result["metrics"]["quantityMapped"])

    def test_cannot_self_promote_with_only_review_status(self):
        """Verify cannot self promote with only review status."""
        doc = approved_fixture()
        doc["definitionStatus"] = "extracted"
        self.assertFalse(review_gate.audit(doc)["ready"])
        doc["definitionStatus"] = "manualReviewed"
        doc["review"]["unresolved"] = ["source mode not verified"]
        self.assertFalse(review_gate.audit(doc)["ready"])

    def test_placeholder_commit_and_license_are_blocked(self):
        """Verify placeholder commit and license are blocked."""
        doc = approved_fixture()
        doc["provenance"]["sourceRevision"] = "PINNED_SHA"
        doc["provenance"]["license"] = "verify-upstream-license"
        findings = review_gate.audit(doc)["blockers"]
        self.assertTrue(any("40-character" in x for x in findings))
        self.assertTrue(any("license" in x for x in findings))

    def test_inherited_and_dynamic_members_require_reconciliation(self):
        """Verify inherited and dynamic members require reconciliation."""
        doc = approved_fixture()
        doc["extraction"]["unresolvedInheritedMembers"] = 2
        doc["extraction"]["unresolvedDynamicMembers"] = 1
        blockers = review_gate.audit(doc)["blockers"]
        self.assertTrue(any("Inherited" in x for x in blockers))
        self.assertTrue(any("Dynamic" in x for x in blockers))

    def test_reconciliation_requires_evidence_even_when_counts_are_zero(self):
        """Verify reconciliation requires evidence even when counts are zero."""
        doc = approved_fixture()
        doc["extraction"]["inheritedCandidates"] = ["Base.read"]
        doc["extraction"]["dynamicCandidates"] = ["line 21: setattr"]
        findings = review_gate.audit(doc)["blockers"]
        self.assertTrue(any("Inherited member reconciliation evidence" in x for x in findings))
        self.assertTrue(any("Dynamic member reconciliation evidence" in x for x in findings))
        doc["review"]["inheritanceEvidence"] = "reviews/base-members.md"
        doc["review"]["dynamicEvidence"] = "reviews/dynamic-members.md"
        self.assertTrue(review_gate.audit(doc)["ready"])

    def test_wrong_quantity_and_missing_commands_block(self):
        """Verify wrong quantity and missing commands block."""
        doc = approved_fixture()
        doc["capabilities"][0]["unit"] = "Ohm"
        self.assertFalse(review_gate.audit(doc)["ready"])
        doc = approved_fixture()
        doc["capabilities"][0]["query"] = None
        self.assertFalse(review_gate.audit(doc)["ready"])

    def test_hardware_evidence_is_additional_and_never_inferred(self):
        """Verify hardware evidence is additional and never inferred."""
        doc = approved_fixture()
        self.assertFalse(review_gate.audit(doc, "hardwareValidated")["ready"])
        doc["definitionStatus"] = "hardwareValidated"
        doc["review"]["status"] = "hardwareValidated"
        doc["review"]["hardwareTested"] = True
        doc["review"]["hardwareEvidence"] = "reports/fixture-123.pdf"
        doc["review"]["testFixture"] = "Fixture 123"
        self.assertTrue(review_gate.audit(doc, "hardwareValidated")["ready"])
        # This is still a metadata assertion, not a real hardware test.

    def test_manual_entry_does_not_require_external_instrument_source_sha(self):
        """Verify manual entry does not require external instrument source sha."""
        doc = approved_fixture()
        doc["provenance"].update({
            "upstreamProject": "manual", "sourcePath": "", "sourceRevision": ""
        })
        self.assertTrue(review_gate.audit(doc)["ready"])


if __name__ == "__main__":
    unittest.main()
