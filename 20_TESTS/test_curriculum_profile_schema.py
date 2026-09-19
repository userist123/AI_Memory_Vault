"""Tests for Curriculum Module Profile Schema, Validation, and Topicality Guards.

Verifies:
1. Valid profiles (e.g. OpenStax Psychology Ch 8) pass schema validation.
2. Negative control 1: Ingestion without profile is rejected.
3. Negative control 2: Profile with missing required fields (e.g. missing topicality_terms) fails.
4. Negative control 3: Preliminary topic check rejects off-topic/filler source text (Anderson golf club scam text).
5. Topicality verification accepts genuine chapter text above the 0.5 density threshold.
"""
from __future__ import annotations

import json
import pytest
from pathlib import Path

import sys
REPO_ROOT = Path(__file__).resolve().parents[1]
SCRIPTS_DIR = REPO_ROOT / "30_SCRIPTS" / "knowledge"
if str(SCRIPTS_DIR) not in sys.path:
    sys.path.insert(0, str(SCRIPTS_DIR))

PSYCH_PROFILE_PATH = REPO_ROOT / "07_EVALUATION" / "curriculum" / "profiles" / "openstax_psychology_ch08.json"

from validate_curriculum_profile import (  # noqa: E402
    validate_curriculum_profile,
    validate_profile_dict,
)
from curriculum_ingestion import (  # noqa: E402
    check_topicality,
    run_curriculum_ingestion,
)


class TestCurriculumProfileSchema:
    def test_openstax_psychology_profile_is_valid(self):
        ok, errors = validate_curriculum_profile(PSYCH_PROFILE_PATH, check_referenced_files=True)
        assert ok, f"Expected psychology profile to pass, got errors: {errors}"

    def test_negative_control_missing_required_field_fails(self):
        data = json.loads(PSYCH_PROFILE_PATH.read_text(encoding="utf-8"))
        del data["topicality_terms"]
        ok, errors = validate_profile_dict(data)
        assert not ok, "Expected validation failure when 'topicality_terms' is missing"
        assert any("topicality_terms" in e for e in errors)

    def test_negative_control_missing_learning_goal_fails(self):
        data = json.loads(PSYCH_PROFILE_PATH.read_text(encoding="utf-8"))
        del data["learning_goal"]
        ok, errors = validate_profile_dict(data)
        assert not ok, "Expected validation failure when 'learning_goal' is missing"
        assert any("learning_goal" in e for e in errors)

    def test_negative_control_retroactive_without_note_fails(self):
        data = json.loads(PSYCH_PROFILE_PATH.read_text(encoding="utf-8"))
        data["retroactive"] = True
        data["retroactive_note"] = "   "
        ok, errors = validate_profile_dict(data)
        assert not ok, "Expected failure when retroactive is true without a retroactive_note"
        assert any("retroactive_note" in e for e in errors)

    def test_negative_control_ingestion_without_profile_refuses(self):
        with pytest.raises((ValueError, FileNotFoundError, TypeError)):
            run_curriculum_ingestion(profile_path=None)

    def test_negative_control_ingestion_with_nonexistent_profile_refuses(self):
        with pytest.raises(FileNotFoundError):
            run_curriculum_ingestion(profile_path=REPO_ROOT / "nonexistent_profile.json")


class TestPreliminaryTopicalityCheck:
    def test_genuine_memory_text_clears_topic_density(self):
        profile = json.loads(PSYCH_PROFILE_PATH.read_text(encoding="utf-8"))
        terms = profile["topicality_terms"]
        sample_text = (
            "Memory functions through three main processes: encoding, storage, and retrieval. "
            "The hippocampus and amygdala are critical brain structures for memory consolidation. "
            "Sensory memory transfers representations into working memory."
        )
        passed, density = check_topicality(sample_text, terms, min_density=0.5)
        assert passed is True
        assert density > 1.0

    def test_negative_control_anderson_filler_text_rejected(self):
        """Negative control: nineteenth-century golf club / bicycle filler text from the
        scam Anderson preview must be rejected by the preliminary topic check.
        """
        profile = json.loads(PSYCH_PROFILE_PATH.read_text(encoding="utf-8"))
        terms = profile["topicality_terms"]
        filler_text = (
            "The golf links at Concord are open to members of the school, and the "
            "wheelmen of the league are invited to the exhibition of bicycles held "
            "in the town hall on the afternoon of the fourteenth. The president of the "
            "club announced that carriage horses should be tethered near the pavilion."
        ) * 5
        passed, density = check_topicality(filler_text, terms, min_density=0.5)
        assert passed is False, f"Expected filler text to be rejected, but got density {density}"
        assert density < 0.5
