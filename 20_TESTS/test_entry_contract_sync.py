"""Rules every agent must see, in the contract each runtime actually loads.

Claude Code loads CLAUDE.md and does NOT load AGENTS.md while CLAUDE.md exists (measured 2026-10-21, context map
C8 vs C22); Codex and Antigravity load AGENTS.md; Gemini CLI loads GEMINI.md, which defers to AGENTS.md. So a
binding rule that lives only in AGENTS.md reaches Claude only if it opens AGENTS.md on its own. Each rule below must
be stated (in short form at least) in both AGENTS.md and CLAUDE.md, and the two must not disagree on the frozen
core. Adding a binding rule to one contract means adding it here and to the other one.
"""
import re
from pathlib import Path

import pytest

REPO = Path(__file__).resolve().parents[1]
AGENTS = (REPO / "AGENTS.md").read_text(encoding="utf-8")
CLAUDE = (REPO / "CLAUDE.md").read_text(encoding="utf-8")
GEMINI = (REPO / "GEMINI.md").read_text(encoding="utf-8")

# rule id -> regex that must match (case-insensitive) in both AGENTS.md and CLAUDE.md
SHARED_RULES = {
    "state_card_first": r"Read `00_GOVERNANCE/VAULT_STATE\.md` first",
    "source_of_truth_order": r"user-confirmed facts, direct execution/test evidence, official documentation",
    "never_weaker_evidence": r"never silently replace stronger evidence with weaker",
    "never_whole_vault": r"never load the whole vault",
    "raw_imports_untrusted": r"06_INBOX/RAW_IMPORTS",
    "excluded_by_default": r"(?s)audit reports.{0,80}(excluded by default|out of context by default)",
    "no_rest_server": r"localhost:8000/memory/search",
    "proposal_is_candidate": r"memory_propose",
    "owner_attests": r"(only the owner attests|until the owner attests)",
    "cite_routes": r"(cite every claim|cite_as)",
    "not_found_is_answer": r"NOT_FOUND",
    "trust_invariants": r"I-001\.\.I-012",
    "secrets_never_stored": r"secrets (must never be|are never) stored",
    "privacy_identifiers_grep": r"%APPDATA%\\ai-memory-vault\\private_identifiers\.txt",
    "privacy_hit_blocks": r"a hit blocks the commit",
    "history_visibility_owner": r"Rewriting Git history and changing repository visibility are the owner's decision",
    "critical_review": r"Critical_Review_Protocol_V1\.md",
    "production_consumer": r"Global Production-Consumer Rule",
    "checkpoint_file": r"00_GOVERNANCE/coordination/tasks/todo-<agent-name>\.md",
    "coordination_first": r"00_GOVERNANCE/coordination/",
}

FROZEN_CORE = ["model_provider.py", "fake_model_provider.py", "model_tier_router.py", "actual_usage_telemetry.py",
               "council_model_execution.py", "executive_model_execution_bridge.py",
               "Planner", "PlanComplexityAnalyzer", "CouncilBudgetController", "Council_Orchestrator.py",
               "ContextPackBuilder", "council_token_telemetry.py"]


@pytest.mark.parametrize("rule", sorted(SHARED_RULES))
def test_binding_rule_is_in_both_contracts(rule):
    pattern = re.compile(SHARED_RULES[rule], re.IGNORECASE)
    missing = [name for name, text in (("AGENTS.md", AGENTS), ("CLAUDE.md", CLAUDE)) if not pattern.search(text)]
    assert not missing, f"rule {rule!r} missing from {missing}"


def _protected_core_paragraph(text, marker):
    m = re.search(marker + r".*", text)
    assert m, f"no protected-core paragraph ({marker})"
    return m.group(0)


def test_both_contracts_list_the_same_frozen_core():
    agents = _protected_core_paragraph(AGENTS, r"\*\*Protected Core Invariant\*\*")
    claude = _protected_core_paragraph(CLAUDE, r"\*\*Protected Core\*\*")
    for module in FROZEN_CORE:
        assert f"`{module}`" in agents or f"/{module}`" in agents, f"AGENTS.md protected core lacks {module}"
        assert f"`{module}`" in claude or f"/{module}`" in claude, f"CLAUDE.md protected core lacks {module}"


def test_the_test_enforced_part_of_the_frozen_core_matches_the_boundary_test():
    """Only modules the boundary test imports may be called test-enforced."""
    boundary = (REPO / "20_TESTS" / "test_protected_core_boundaries.py").read_text(encoding="utf-8")
    imported = set(re.findall(r"^import cognitive_core\.(\w+) as", boundary, re.MULTILINE))
    assert imported == {m[:-3] for m in FROZEN_CORE[:6]}


def test_gemini_defers_to_agents_and_keeps_the_hard_rules_visible():
    assert "Follow `AGENTS.md` in full" in GEMINI
    for needle in ("VAULT_STATE.md", "Critical_Review_Protocol_V1.md", "Owner privacy"):
        assert needle in GEMINI, needle
