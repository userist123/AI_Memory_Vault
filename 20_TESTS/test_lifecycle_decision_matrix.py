"""The transition policy is pure, deterministic, and its whole decision table is committed.

LIFE-001 asks for a single authority over lifecycle transitions that can be
tested exhaustively. The authority exists — `lifecycle/policy.py`, deny by
default — but nothing proved it was free of I/O, nothing pinned its answers, and
its table was only readable by reading the code.

2,430 combinations of mutation, principal, from-state and to-state (2,700 with
"no from-state") are evaluated here. 166 are allowed. The allowed set is
committed as `20_TESTS/fixtures/lifecycle_decision_matrix.json`, so widening the
policy by one cell shows up as a diff in review instead of passing unnoticed.
"""
from __future__ import annotations

import ast
import json
import sys
from pathlib import Path

import pytest

REPO = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(REPO / "03_IMPLEMENTATION" / "packages"))

POLICY_SOURCE = REPO / "03_IMPLEMENTATION" / "packages" / "lifecycle" / "policy.py"
MATRIX = REPO / "20_TESTS" / "fixtures" / "lifecycle_decision_matrix.json"

from lifecycle import policy  # noqa: E402


def every_request():
    for mutation in policy.Mutation:
        for principal in policy.PrincipalRole:
            for to_state in policy.LifecycleState:
                for from_state in [None, *policy.LifecycleState]:
                    yield policy.TransitionRequest(
                        mutation=mutation, principal=principal,
                        to_state=to_state, from_state=from_state)


def as_row(request: policy.TransitionRequest) -> dict:
    return {"mutation": request.mutation.value, "principal": request.principal.value,
            "from": request.from_state.value if request.from_state else None,
            "to": request.to_state.value}


# --- the policy is a function, not a program ------------------------------------

FORBIDDEN_CALLS = {"open", "print", "input", "exec", "eval", "compile"}
FORBIDDEN_MODULES = {"os", "io", "sqlite3", "random", "requests", "urllib", "socket", "subprocess"}


def test_the_policy_module_performs_no_io_and_no_randomness():
    """A decision that depends on the clock, the disk or a seed cannot be tested exhaustively."""
    tree = ast.parse(POLICY_SOURCE.read_text(encoding="utf-8"))
    imported = set()
    for node in ast.walk(tree):
        if isinstance(node, ast.Import):
            imported.update(alias.name.split(".")[0] for alias in node.names)
        elif isinstance(node, ast.ImportFrom) and node.module:
            imported.add(node.module.split(".")[0])
        elif isinstance(node, ast.Call) and isinstance(node.func, ast.Name):
            assert node.func.id not in FORBIDDEN_CALLS, f"policy calls {node.func.id}() at line {node.lineno}"
    assert imported & FORBIDDEN_MODULES == set(), f"policy imports {sorted(imported & FORBIDDEN_MODULES)}"


def test_evaluate_is_deterministic():
    for request in every_request():
        first, second = policy.evaluate(request), policy.evaluate(request)
        assert (first.allowed, first.reason) == (second.allowed, second.reason)


def test_evaluate_never_raises_and_always_gives_a_reason_when_it_refuses():
    for request in every_request():
        decision = policy.evaluate(request)
        assert isinstance(decision.allowed, bool)
        if not decision.allowed:
            assert decision.reason, f"silent refusal for {as_row(request)}"


# --- the table itself -----------------------------------------------------------

def test_the_allowed_set_matches_the_committed_matrix():
    allowed = sorted((as_row(r) for r in every_request() if policy.evaluate(r).allowed),
                     key=lambda row: (row["mutation"], row["principal"], str(row["from"]), row["to"]))
    committed = json.loads(MATRIX.read_text(encoding="utf-8"))["allowed_transitions"]
    assert allowed == committed, "the policy changed; review the diff before updating the fixture"


def test_almost_everything_is_denied():
    """Deny by default is the property; the exact ratio is evidence it still holds."""
    total = sum(1 for _ in every_request())
    allowed = sum(1 for r in every_request() if policy.evaluate(r).allowed)
    assert total == 2700
    assert allowed == 166, f"{allowed} of {total} allowed"


# --- what an agent may reach, through the API that actually exists ---------------

def test_an_agent_cannot_create_a_note_into_a_trusted_state():
    agent_creation = policy.permitted_creation_states(policy.PrincipalRole.AI_AGENT)
    assert policy.LifecycleState.ACTIVE not in agent_creation
    assert policy.LifecycleState.VERIFIED not in agent_creation


def test_the_table_is_wider_than_the_api_and_that_gap_is_recorded():
    """`structural_rewrite` lets an agent walk REVIEW -> VERIFIED in the table.

    The public API does not expose it: `update()` treats lifecycle as immutable
    and `attest()` refuses an agent outright, so no caller can reach it today.
    The gap is pinned rather than silently trusted — if someone ever calls the
    policy directly with STRUCTURAL_REWRITE, this is the path they would get.
    """
    request = policy.TransitionRequest(
        mutation=policy.Mutation.STRUCTURAL_REWRITE, principal=policy.PrincipalRole.AI_AGENT,
        from_state=policy.LifecycleState.REVIEW, to_state=policy.LifecycleState.VERIFIED)
    assert policy.evaluate(request).allowed is True, (
        "if this now refuses, the policy was tightened: delete this test and say so in the state card"
    )


@pytest.mark.parametrize("mutation, expected", [
    (policy.Mutation.ATTEST, False),
    (policy.Mutation.PROMOTE, False),
])
def test_attestation_and_promotion_stay_out_of_an_agents_reach(mutation, expected):
    reachable = any(
        policy.evaluate(policy.TransitionRequest(
            mutation=mutation, principal=policy.PrincipalRole.AI_AGENT,
            from_state=from_state, to_state=to_state)).allowed
        for from_state in policy.LifecycleState for to_state in policy.LifecycleState)
    assert reachable is expected
