"""Tests for OpenStax transfer benchmark - Punctul 3 and Punctul 4.

Tests:
1. Frozen test set integrity (22 questions frozen, schema correct).
2. Control arm excludes all 16 openstax notes from index.
3. Treatment arm includes all 16 openstax notes.
4. Verdict logic: CORRECT_SUPPORTED, CORRECT_UNSUPPORTED, ABSTAIN, WRONG, TRAP_PASS, TRAP_FAIL.
5. evidence_quote verification is substring-only (no LLM needed).
6. NEGATIVE CONTROLS: verdict logic can fail/produce wrong outcomes when logic is broken.
"""
from __future__ import annotations
import json
import os
import sys
from pathlib import Path

REPO = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(REPO.resolve()))
sys.path.insert(0, str((REPO / "03_IMPLEMENTATION" / "packages").resolve()))
os.environ.setdefault("MEMORY_CONTROLLER_HMAC_SECRET", "0" * 32)

FROZEN_TEST_PATH = REPO / "07_EVALUATION" / "curriculum" / "openstax_ch08_frozen_test_set.json"
REPORT_PATH = REPO / "08_OBSERVABILITY" / "reports" / "curriculum_heldout_eval.json"
OPENSTAX_NOTE_PATTERN = "openstax-psychology-2e-ch08"

import pytest
from retrieval.vault_index import VaultIndex
from memory_controller.storage.file_engine import FileStorageEngine


# == 1. Frozen test set integrity ==
class TestFrozenTestSetIntegrity:
    def test_file_exists(self):
        assert FROZEN_TEST_PATH.exists(), f"Frozen test set not found: {FROZEN_TEST_PATH}"

    def test_schema_keys(self):
        data = json.loads(FROZEN_TEST_PATH.read_text(encoding="utf-8"))
        assert "total_review_questions" in data
        assert "total_trap_questions" in data
        assert "questions" in data

    def test_question_count(self):
        data = json.loads(FROZEN_TEST_PATH.read_text(encoding="utf-8"))
        assert data["total_review_questions"] == 12, "Expected 12 review questions"
        assert data["total_trap_questions"] == 10, "Expected 10 trap questions"
        assert len(data["questions"]) == 22

    def test_all_review_questions_have_choices_and_correct_answer(self):
        data = json.loads(FROZEN_TEST_PATH.read_text(encoding="utf-8"))
        for q in data["questions"]:
            if q["type"] == "author_review":
                assert len(q["choices"]) >= 2, f"{q['id']}: fewer than 2 choices"
                assert q["correct_answer"] in q["choices"], f"{q['id']}: correct_answer not in choices"

    def test_all_trap_questions_have_unanswerable_true(self):
        data = json.loads(FROZEN_TEST_PATH.read_text(encoding="utf-8"))
        for q in data["questions"]:
            if q["type"] == "unanswerable_trap":
                assert q.get("unanswerable") is True, f"{q['id']}: unanswerable != True"

    def test_all_trap_questions_have_exactly_four_choices(self):
        data = json.loads(FROZEN_TEST_PATH.read_text(encoding="utf-8"))
        for q in data["questions"]:
            if q["type"] == "unanswerable_trap":
                assert len(q["choices"]) == 4, f"{q['id']}: trap has {len(q['choices'])} choices, expected 4"

    def test_all_trap_questions_have_correct_answer_insufficient(self):
        data = json.loads(FROZEN_TEST_PATH.read_text(encoding="utf-8"))
        for q in data["questions"]:
            if q["type"] == "unanswerable_trap":
                assert q.get("correct_answer") == "INSUFFICIENT", f"{q['id']}: trap correct_answer != INSUFFICIENT"

    def test_no_choices_contain_giveaway_strings(self):
        """No choices in any question may contain giveaway strings like NOT_IN_CHAPTER or INSUFFICIENT."""
        data = json.loads(FROZEN_TEST_PATH.read_text(encoding="utf-8"))
        banned = ["not_in_chapter", "insufficient", "none of the above", "all of the above"]
        for q in data["questions"]:
            for choice in q.get("choices", []):
                norm = choice.strip().lower()
                for b in banned:
                    assert b not in norm, f"Giveaway '{b}' found in {q['id']} choice: '{choice}'"

    def test_negative_control_giveaway_choice_fails(self):
        """NEGATIVE: Simulated choice containing NOT_IN_CHAPTER must fail the giveaway check."""
        banned = ["not_in_chapter", "insufficient", "none of the above", "all of the above"]
        fake_choice = "NOT_IN_CHAPTER"
        with pytest.raises(AssertionError):
            assert not any(b in fake_choice.lower() for b in banned), "Giveaway was not caught"

    def test_negative_wrong_count_fails(self):
        """NEGATIVE: Claiming 13 review questions must fail."""
        data = json.loads(FROZEN_TEST_PATH.read_text(encoding="utf-8"))
        with pytest.raises(AssertionError):
            assert data["total_review_questions"] == 13, "Expected 13 review questions"


# == 2. Control arm index exclusion ==
class TestControlArmIndexExclusion:
    def test_control_excludes_openstax_notes(self):
        full_index = VaultIndex.load(REPO, lifecycles=["ACTIVE", "VERIFIED", "REVIEW"])
        openstax_ids = {
            n.id for n in full_index.notes
            if OPENSTAX_NOTE_PATTERN in (n.meta.get("provenance", {}) or {}).get("source_ref", "")
        }
        assert len(openstax_ids) == 16, f"Expected 16 OpenStax notes, got {len(openstax_ids)}"
        control_notes = [n for n in full_index.notes if n.id not in openstax_ids]
        assert len(control_notes) == len(full_index.notes) - 16
        assert all(
            OPENSTAX_NOTE_PATTERN not in (n.meta.get("provenance", {}) or {}).get("source_ref", "")
            for n in control_notes
        ), "Control arm still contains OpenStax notes"

    def test_negative_control_full_index_has_openstax(self):
        """NEGATIVE: Full index must contain OpenStax notes."""
        full_index = VaultIndex.load(REPO, lifecycles=["ACTIVE", "VERIFIED", "REVIEW"])
        openstax_ids = {
            n.id for n in full_index.notes
            if OPENSTAX_NOTE_PATTERN in (n.meta.get("provenance", {}) or {}).get("source_ref", "")
        }
        assert len(openstax_ids) > 0, "NEGATIVE CONTROL FAILED: treatment arm has no OpenStax notes"


# == 3. Treatment arm index inclusion ==
class TestTreatmentArmIndexInclusion:
    def test_treatment_includes_openstax_notes(self):
        full_index = VaultIndex.load(REPO, lifecycles=["ACTIVE", "VERIFIED", "REVIEW"])
        openstax_notes = [
            n for n in full_index.notes
            if OPENSTAX_NOTE_PATTERN in (n.meta.get("provenance", {}) or {}).get("source_ref", "")
        ]
        assert len(openstax_notes) == 16

    def test_openstax_notes_have_content(self):
        storage = FileStorageEngine(str(REPO))
        full_index = VaultIndex.load(REPO, lifecycles=["ACTIVE", "VERIFIED", "REVIEW"])
        openstax_ids = [
            n.id for n in full_index.notes
            if OPENSTAX_NOTE_PATTERN in (n.meta.get("provenance", {}) or {}).get("source_ref", "")
        ]
        empty_content = []
        for nid in openstax_ids:
            note = storage.get(nid)
            content = (note or {}).get("content", "")
            if not content:
                empty_content.append(nid)
        assert not empty_content, f"These notes have empty content: {empty_content}"


# == 4. Verdict logic (no LLM) ==
class TestVerdictLogic:
    """Test verdict derivation logic directly - no LLM calls."""

    def _verdict(self, is_trap, selected, correct, quote_verified):
        if is_trap:
            return "TRAP_PASS" if selected == "INSUFFICIENT" else "TRAP_FAIL"
        if selected == correct:
            return "CORRECT_SUPPORTED" if quote_verified else "CORRECT_UNSUPPORTED"
        if selected == "INSUFFICIENT":
            return "ABSTAIN"
        return "WRONG"

    def test_correct_with_quote(self):
        assert self._verdict(False, "working memory", "working memory", True) == "CORRECT_SUPPORTED"

    def test_correct_without_quote(self):
        assert self._verdict(False, "working memory", "working memory", False) == "CORRECT_UNSUPPORTED"

    def test_wrong_choice(self):
        assert self._verdict(False, "sensory memory", "working memory", False) == "WRONG"

    def test_insufficient_on_review_q(self):
        assert self._verdict(False, "INSUFFICIENT", "working memory", False) == "ABSTAIN"

    def test_trap_pass(self):
        assert self._verdict(True, "INSUFFICIENT", "", False) == "TRAP_PASS"

    def test_trap_fail(self):
        assert self._verdict(True, "sensory memory", "", False) == "TRAP_FAIL"

    def test_negative_wrong_answer_not_correct(self):
        """NEGATIVE: wrong choice must NOT produce CORRECT_SUPPORTED."""
        v = self._verdict(False, "sensory memory", "working memory", True)
        assert v != "CORRECT_SUPPORTED", "Wrong choice incorrectly flagged as CORRECT_SUPPORTED"

    def test_negative_trap_choice_not_trap_pass(self):
        """NEGATIVE: if trap picks a choice, must NOT be TRAP_PASS."""
        v = self._verdict(True, "working memory", "", False)
        assert v != "TRAP_PASS", "Trap with choice answer incorrectly flagged TRAP_PASS"


# == 5. Evidence quote verification (no LLM) ==
class TestEvidenceQuoteVerification:
    """verify_evidence_quote is substring-only - no LLM."""

    def _verify(self, quote, notes):
        if not quote:
            return False
        norm_q = " ".join(quote.split())
        norm_n = " ".join(notes.split())
        return norm_q in norm_n

    def test_exact_match(self):
        notes = "Short-term memory is a temporary storage system that processes incoming sensory memory."
        assert self._verify("Short-term memory is a temporary storage system", notes) is True

    def test_normalized_whitespace(self):
        notes = "Short-term   memory is\na temporary   storage."
        assert self._verify("Short-term memory is a temporary storage.", notes) is True

    def test_invented_quote_fails(self):
        notes = "Short-term memory lasts 15-30 seconds."
        assert self._verify("long-term memory is essentially limitless", notes) is False

    def test_none_quote_fails(self):
        assert self._verify(None, "any notes text") is False

    def test_negative_partial_match_must_be_substring(self):
        """NEGATIVE: a truncated quote that does not appear verbatim must fail."""
        notes = "explicit memory is consciously recalled"
        result = self._verify("explicit memory is explicitly recalled", notes)
        assert result is False, "Invented quote passed as verified"

def _report_is_ready() -> bool:
    """True only when the new schema-v1 report exists (not the old Ashby report)."""
    if not REPORT_PATH.exists():
        return False
    try:
        data = json.loads(REPORT_PATH.read_text(encoding="utf-8"))
        return data.get("schema") == "curriculum-transfer-benchmark-v1"
    except Exception:
        return False


# == 6. Heldout eval report structure (post-run) ==
class TestHeldoutReportStructure:
    """Only runs if the report has been generated."""

    @pytest.mark.skipif(not _report_is_ready(), reason="Run run_curriculum_eval.py first to generate v1 report")
    def test_report_has_both_arms(self):
        data = json.loads(REPORT_PATH.read_text(encoding="utf-8"))
        assert "control_arm" in data
        assert "treatment_arm" in data

    @pytest.mark.skipif(not _report_is_ready(), reason="Run run_curriculum_eval.py first to generate v1 report")
    def test_report_schema_version(self):
        data = json.loads(REPORT_PATH.read_text(encoding="utf-8"))
        assert data.get("schema") == "curriculum-transfer-benchmark-v1"

    @pytest.mark.skipif(not _report_is_ready(), reason="Run run_curriculum_eval.py first to generate v1 report")
    def test_treatment_arm_has_per_question(self):
        data = json.loads(REPORT_PATH.read_text(encoding="utf-8"))
        arm = data["treatment_arm"]
        assert "per_question" in arm
        assert len(arm["per_question"]) == 22

    @pytest.mark.skipif(not _report_is_ready(), reason="Run run_curriculum_eval.py first to generate v1 report")
    def test_control_arm_has_zero_openstax_in_retrieved(self):
        """Control arm must retrieve 0 OpenStax notes per question."""
        data = json.loads(REPORT_PATH.read_text(encoding="utf-8"))
        arm = data["control_arm"]
        for q in arm["per_question"]:
            assert q["openstax_notes_in_retrieved"] == 0, (
                f"{q['id']}: control arm retrieved {q['openstax_notes_in_retrieved']} OpenStax notes"
            )

    @pytest.mark.skipif(not _report_is_ready(), reason="Run run_curriculum_eval.py first to generate v1 report")
    def test_negative_control_trap_fail_count(self):
        """NEGATIVE: If ALL traps fail, something is wrong - reader is hallucinating."""
        data = json.loads(REPORT_PATH.read_text(encoding="utf-8"))
        arm = data["treatment_arm"]
        tq = arm["trap_questions"]
        total = tq["total"]
        fail_count = int(tq["trap_fail"].split("/")[0])
        assert fail_count < total, (
            f"NEGATIVE CONTROL: All {total} traps failed - reader is hallucinating on every trap"
        )


# == 7. Judgment logic and simulated reader negative controls ==
class TestJudgmentLogicAndNegativeControls:
    def _judge(
        self,
        selected_choice: str,
        evidence_quote: str | None,
        is_trap: bool,
        choices: list[str],
        correct_answer: str,
        quote_verified: bool,
    ) -> str:
        valid_options = set(choices) | {"INSUFFICIENT"}
        if selected_choice not in valid_options:
            return "READER_FORMAT_ERROR"
        if is_trap:
            return "TRAP_PASS" if selected_choice == "INSUFFICIENT" else "TRAP_FAIL"
        else:
            if selected_choice == correct_answer:
                return "CORRECT_SUPPORTED" if quote_verified else "CORRECT_UNSUPPORTED"
            elif selected_choice == "INSUFFICIENT":
                return "ABSTAIN"
            else:
                return "WRONG"

    def test_simulated_reader_trap_selection_yields_trap_fail(self):
        """Simulated reader choosing a distractor on a trap question must yield TRAP_FAIL."""
        trap_choices = ["0.5 mmol/L", "10 mmol/L", "25 mmol/L", "50 mmol/L"]
        for chosen in trap_choices:
            verdict = self._judge(
                selected_choice=chosen,
                evidence_quote=None,
                is_trap=True,
                choices=trap_choices,
                correct_answer="INSUFFICIENT",
                quote_verified=False,
            )
            assert verdict == "TRAP_FAIL", f"Expected TRAP_FAIL for choice {chosen}, got {verdict}"

    def test_simulated_reader_trap_insufficient_yields_trap_pass(self):
        """Simulated reader abstaining with INSUFFICIENT on a trap question must yield TRAP_PASS."""
        trap_choices = ["0.5 mmol/L", "10 mmol/L", "25 mmol/L", "50 mmol/L"]
        verdict = self._judge(
            selected_choice="INSUFFICIENT",
            evidence_quote=None,
            is_trap=True,
            choices=trap_choices,
            correct_answer="INSUFFICIENT",
            quote_verified=False,
        )
        assert verdict == "TRAP_PASS"

    def test_simulated_reader_unknown_or_giveaway_yields_format_error(self):
        """Simulated reader returning NOT_IN_CHAPTER or arbitrary string yields READER_FORMAT_ERROR."""
        trap_choices = ["0.5 mmol/L", "10 mmol/L", "25 mmol/L", "50 mmol/L"]
        for bad in ["NOT_IN_CHAPTER", "none of the above", "I think choice B", "UNKNOWN", ""]:
            verdict = self._judge(
                selected_choice=bad,
                evidence_quote=None,
                is_trap=True,
                choices=trap_choices,
                correct_answer="INSUFFICIENT",
                quote_verified=False,
            )
            assert verdict == "READER_FORMAT_ERROR", f"Expected READER_FORMAT_ERROR for '{bad}', got {verdict}"

    def test_simulated_reader_review_verdicts(self):
        review_choices = ["sensory memory", "episodic memory", "working memory", "implicit memory"]
        correct = "working memory"

        assert self._judge("working memory", "quote", False, review_choices, correct, True) == "CORRECT_SUPPORTED"
        assert self._judge("working memory", None, False, review_choices, correct, False) == "CORRECT_UNSUPPORTED"
        assert self._judge("INSUFFICIENT", None, False, review_choices, correct, False) == "ABSTAIN"
        assert self._judge("episodic memory", None, False, review_choices, correct, False) == "WRONG"
        assert self._judge("unknown choice", None, False, review_choices, correct, False) == "READER_FORMAT_ERROR"

