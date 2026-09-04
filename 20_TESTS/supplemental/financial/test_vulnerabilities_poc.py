"""
Empirical Proof-of-Concept (PoC) Demonstrating Schema & Invariant Bypasses in memory_controller/financial_schema.py.

This file empirically confirms the existence of the vulnerabilities discovered by Challenger M1-2:
- PoC 1: Universal Wildcard Bypass via Variant C in FINANCIAL_NOTE_SCHEMA.
- PoC 2: P0 Bypass via Case Variations & Forged Verification Strings.
- PoC 3: P2 Privileged Provenance Bypass via Case Variations & Custom Privileged Names.
- PoC 4: P3 Lifecycle Scoping Bypass via Case Variations & Custom Lifecycle Names.
- PoC 5: Missing / Null ID Bypass.
- PoC 6: Mathematical Bounds Bypass for Technical Indicators & Signals.
"""

import uuid
import pytest
from memory_controller.financial_schema import validate_financial_note


class TestVulnerabilitiesPoC:

    def test_poc1_universal_wildcard_bypass_variant_c(self):
        """Remediation verification: Arbitrary invalid fields are rejected by validate_financial_note."""
        corrupted_payload = {
            "id": str(uuid.uuid4()),
            "type": "MALICIOUS_UNREGISTERED_TYPE",
            "lifecycle": "ILLEGAL_LIFECYCLE_STATE",
            "tags": "this_should_be_a_list_not_a_string",
            "provenance": "this_should_be_a_dict",
            "confidence": "SUPER_CONFIDENT",
            "technical_indicators": {"rsi_14": 999999.0}
        }
        is_valid, errors = validate_financial_note(corrupted_payload, is_ai_agent=True)
        assert is_valid is False, "Remediation verified: Corrupted payload properly rejected!"
        assert len(errors) >= 1

    def test_poc2_p0_case_and_string_bypass(self):
        """Remediation verification: Uppercase 'VERIFIED' or custom strings rejected by P0 check."""
        forged_payload = {
            "id": str(uuid.uuid4()),
            "verification": "VERIFIED"
        }
        is_valid, errors = validate_financial_note(forged_payload, is_ai_agent=True)
        assert is_valid is False, "Remediation verified: 'VERIFIED' rejected by P0 AI verification gate!"

    def test_poc3_p2_privileged_provenance_bypass(self):
        """Remediation verification: Uppercase 'USER' or custom 'root' / 'admin' rejected by P2 check."""
        forged_payload = {
            "id": str(uuid.uuid4()),
            "provenance": {"source_type": "USER", "source_ref": "escalation"}
        }
        is_valid, errors = validate_financial_note(forged_payload, is_ai_agent=True)
        assert is_valid is False, "Remediation verified: 'USER' rejected by P2 privileged provenance isolation!"

        root_payload = {
            "id": str(uuid.uuid4()),
            "provenance": {"source_type": "root", "source_ref": "escalation"}
        }
        is_valid_root, _ = validate_financial_note(root_payload, is_ai_agent=True)
        assert is_valid_root is False, "Remediation verified: 'root' rejected by P2 privileged provenance isolation!"

    def test_poc4_p3_lifecycle_scoping_bypass(self):
        """Remediation verification: Lowercase 'active' or 'PRODUCTION' rejected by P3 check."""
        forged_payload = {
            "id": str(uuid.uuid4()),
            "lifecycle": "active"
        }
        is_valid, errors = validate_financial_note(forged_payload, is_ai_agent=True)
        assert is_valid is False, "Remediation verified: 'active' rejected by P3 creation lifecycle scoping!"

        prod_payload = {
            "id": str(uuid.uuid4()),
            "lifecycle": "PRODUCTION"
        }
        is_valid_prod, _ = validate_financial_note(prod_payload, is_ai_agent=True)
        assert is_valid_prod is False, "Remediation verified: 'PRODUCTION' rejected by P3 creation lifecycle scoping!"

    def test_poc5_null_id_bypass(self):
        """Remediation verification: id=None is rejected."""
        null_id_payload = {
            "id": None,
            "title": "Null ID Note"
        }
        is_valid, errors = validate_financial_note(null_id_payload, is_ai_agent=True)
        assert is_valid is False, "Remediation verified: id=None rejected by UUID validation!"

    def test_poc6_mathematical_bounds_bypass(self):
        """Remediation verification: Out-of-bounds indicators are rejected."""
        out_of_bounds_payload = {
            "technical_indicators": {"rsi_14": -50.0, "atr_14": -10.0},
            "quantitative_signal": {"score": 999, "confluences": 100, "win_probability_pct": 5.0},
            "risk_metrics": {"impact": 50}
        }
        is_valid, errors = validate_financial_note(out_of_bounds_payload, is_ai_agent=True)
        assert is_valid is False, "Remediation verified: Out-of-bounds indicators rejected!"
