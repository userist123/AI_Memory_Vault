"""Unit tests for the canonical lifecycle policy authority.

`lifecycle.policy` is the single source of truth for every lifecycle state
change in the runtime. These tests pin its contract directly, independently of
any caller: the full transition matrix, the principal constraints, and the
fail-closed property.

The matrix tests are exhaustive over the 9x9 state space rather than
example-based, so that widening a table anywhere is impossible without a test
turning red.
"""
import itertools

import pytest

from lifecycle import policy
from lifecycle.policy import (
    Decision,
    LifecycleState as S,
    LifecycleViolation,
    Mutation as M,
    PrincipalRole as P,
    TransitionRequest as TR,
)

ALL_STATES = list(S)
ALL_PRINCIPALS = list(P)


def _d(**kw) -> Decision:
    return policy.evaluate(TR(**kw))


# ---------------------------------------------------------------------------
# Structural completeness
# ---------------------------------------------------------------------------


def test_all_nine_canonical_states_are_modelled():
    assert {s.value for s in S} == {
        "RAW", "CLASSIFIED", "NORMALIZED", "REVIEW", "VERIFIED",
        "ACTIVE", "RECONSOLIDATING", "SUPERSEDED", "ARCHIVED",
    }


def test_every_state_has_a_structural_pipeline_entry():
    """A missing entry would silently deny; an explicit empty set is the
    intended way to express a terminal state."""
    assert set(policy._STRUCTURAL_PIPELINE) == set(ALL_STATES)


def test_every_mutation_declares_its_permitted_principals():
    """A mutation absent from the table denies everyone, which is safe but is
    almost certainly an oversight rather than a decision."""
    assert set(policy._MUTATION_PRINCIPALS) == set(M)


# ---------------------------------------------------------------------------
# Fail-closed
# ---------------------------------------------------------------------------


@pytest.mark.parametrize("bad", [None, "", "NOT_A_STATE", 42, object(), ["ACTIVE"]])
def test_unknown_target_state_denies(bad):
    assert not _d(mutation=M.ARCHIVE, from_state=S.ACTIVE, to_state=bad, principal=P.HUMAN)


@pytest.mark.parametrize("bad", ["", "NOT_A_STATE", 42, object()])
def test_unknown_source_state_denies(bad):
    assert not _d(mutation=M.ARCHIVE, from_state=bad, to_state=S.ARCHIVED, principal=P.HUMAN)


def test_missing_source_state_denies_for_non_create():
    assert not _d(mutation=M.ARCHIVE, from_state=None, to_state=S.ARCHIVED, principal=P.HUMAN)


@pytest.mark.parametrize("bad", [None, "", "root", "superuser", 42, object()])
def test_unknown_principal_denies(bad):
    assert not _d(mutation=M.ARCHIVE, from_state=S.ACTIVE, to_state=S.ARCHIVED, principal=bad)


@pytest.mark.parametrize("bad", [None, "archive", "promote", 42, object()])
def test_unknown_mutation_denies(bad):
    """A string that merely *looks* like a mutation value must not be honored."""
    assert not _d(mutation=bad, from_state=S.ACTIVE, to_state=S.ARCHIVED, principal=P.HUMAN)


@pytest.mark.parametrize("bad", [None, "", 42, object(), {"mutation": "archive"}])
def test_malformed_request_denies(bad):
    assert not policy.evaluate(bad)


def test_decision_is_falsy_when_denied_and_truthy_when_allowed():
    assert not policy.evaluate(TR(mutation=M.PROMOTE, from_state=S.RAW,
                                  to_state=S.ACTIVE, principal=P.HUMAN))
    assert policy.evaluate(TR(mutation=M.PROMOTE, from_state=S.REVIEW,
                              to_state=S.ACTIVE, principal=P.HUMAN))


def test_denials_always_carry_a_reason():
    d = _d(mutation=M.PROMOTE, from_state=S.RAW, to_state=S.ACTIVE, principal=P.HUMAN)
    assert not d.allowed
    assert d.reason.strip()


def test_policy_does_not_mutate_its_request():
    req = TR(mutation=M.ARCHIVE, from_state=S.ACTIVE, to_state=S.ARCHIVED, principal=P.HUMAN)
    policy.evaluate(req)
    assert req.from_state is S.ACTIVE and req.to_state is S.ARCHIVED
    with pytest.raises(Exception):
        req.to_state = S.RAW  # frozen dataclass


# ---------------------------------------------------------------------------
# CREATE
# ---------------------------------------------------------------------------


def test_ai_agent_cannot_create_into_trusted_states():
    for state in (S.VERIFIED, S.ACTIVE):
        assert not _d(mutation=M.CREATE, to_state=state, principal=P.AI_AGENT)


def test_ai_agent_may_create_into_pre_trust_states():
    for state in (S.RAW, S.CLASSIFIED, S.NORMALIZED, S.REVIEW):
        assert _d(mutation=M.CREATE, to_state=state, principal=P.AI_AGENT)


@pytest.mark.parametrize("principal", [P.HUMAN, P.ADMIN])
def test_nobody_may_create_directly_into_a_terminal_or_transient_state(principal):
    for state in (S.SUPERSEDED, S.ARCHIVED, S.RECONSOLIDATING):
        assert not _d(mutation=M.CREATE, to_state=state, principal=principal)


def test_create_must_not_declare_a_source_state():
    assert not _d(mutation=M.CREATE, from_state=S.RAW, to_state=S.REVIEW, principal=P.HUMAN)


def test_permitted_creation_states_matches_evaluate():
    for principal in ALL_PRINCIPALS:
        permitted = policy.permitted_creation_states(principal)
        for state in ALL_STATES:
            assert bool(_d(mutation=M.CREATE, to_state=state, principal=principal)) == (
                state in permitted
            )


@pytest.mark.parametrize("bad", [None, "nobody", 42])
def test_permitted_creation_states_denies_all_for_unknown_principal(bad):
    assert policy.permitted_creation_states(bad) == frozenset()


# ---------------------------------------------------------------------------
# STRUCTURAL_REWRITE -- the strict linear pipeline
# ---------------------------------------------------------------------------

#: The only legal bare lifecycle field advances.
STRUCTURAL_LEGAL = {
    (S.RAW, S.CLASSIFIED),
    (S.CLASSIFIED, S.NORMALIZED),
    (S.NORMALIZED, S.REVIEW),
    (S.REVIEW, S.VERIFIED),
    (S.VERIFIED, S.ACTIVE),
    (S.ACTIVE, S.SUPERSEDED),
    (S.ACTIVE, S.ARCHIVED),
}


@pytest.mark.parametrize("src,dst", sorted(itertools.product(ALL_STATES, ALL_STATES),
                                           key=lambda p: (p[0].value, p[1].value)))
def test_structural_rewrite_matrix_is_exhaustively_pinned(src, dst):
    """Every one of the 81 ordered pairs is either an identity, an explicitly
    legal advance, or denied. Nothing is left unspecified."""
    allowed = bool(_d(mutation=M.STRUCTURAL_REWRITE, from_state=src,
                      to_state=dst, principal=P.HUMAN))
    expected = (src is dst) or ((src, dst) in STRUCTURAL_LEGAL)
    assert allowed is expected, f"{src.value} -> {dst.value}"


def test_structural_rewrite_cannot_skip_a_pipeline_stage():
    assert not _d(mutation=M.STRUCTURAL_REWRITE, from_state=S.RAW,
                  to_state=S.ACTIVE, principal=P.HUMAN)


def test_structural_rewrite_cannot_run_backwards():
    assert not _d(mutation=M.STRUCTURAL_REWRITE, from_state=S.ACTIVE,
                  to_state=S.REVIEW, principal=P.HUMAN)


def test_terminal_states_are_structurally_frozen():
    for terminal in (S.SUPERSEDED, S.ARCHIVED, S.RECONSOLIDATING):
        for dst in ALL_STATES:
            if dst is terminal:
                continue
            assert not _d(mutation=M.STRUCTURAL_REWRITE, from_state=terminal,
                          to_state=dst, principal=P.HUMAN)


def test_structural_denial_preserves_the_legacy_error_message():
    """Existing callers and tests assert on this exact wording."""
    d = _d(mutation=M.STRUCTURAL_REWRITE, from_state=S.RAW,
           to_state=S.ACTIVE, principal=P.HUMAN)
    assert d.reason == "Invalid transition from Lifecycle.RAW to Lifecycle.ACTIVE"


# ---------------------------------------------------------------------------
# Non-transitioning mutations
# ---------------------------------------------------------------------------


@pytest.mark.parametrize("mutation", [M.UPDATE, M.ATTEST])
def test_non_transitioning_mutations_allow_identity(mutation):
    principal = P.HUMAN
    for state in ALL_STATES:
        assert _d(mutation=mutation, from_state=state, to_state=state, principal=principal)


@pytest.mark.parametrize("mutation", [M.UPDATE, M.ATTEST])
def test_non_transitioning_mutations_deny_every_actual_move(mutation):
    for src, dst in itertools.product(ALL_STATES, ALL_STATES):
        if src is dst:
            continue
        assert not _d(mutation=mutation, from_state=src, to_state=dst, principal=P.HUMAN)


def test_attest_does_not_promote():
    """Attestation is a verification-axis act; it must never reach ACTIVE."""
    assert not _d(mutation=M.ATTEST, from_state=S.REVIEW, to_state=S.ACTIVE, principal=P.HUMAN)


def test_ai_agent_may_not_attest():
    assert not _d(mutation=M.ATTEST, from_state=S.REVIEW, to_state=S.REVIEW,
                  principal=P.AI_AGENT)


# ---------------------------------------------------------------------------
# Operational transitions
# ---------------------------------------------------------------------------


def test_review_accepts_every_pre_trust_state():
    for src in (S.RAW, S.CLASSIFIED, S.NORMALIZED, S.REVIEW):
        assert _d(mutation=M.REVIEW, from_state=src, to_state=S.REVIEW, principal=P.HUMAN)


def test_review_cannot_pull_back_settled_memory():
    for src in (S.VERIFIED, S.ACTIVE, S.SUPERSEDED, S.ARCHIVED, S.RECONSOLIDATING):
        assert not _d(mutation=M.REVIEW, from_state=src, to_state=S.REVIEW, principal=P.HUMAN)


def test_promote_targets_only_active():
    for dst in ALL_STATES:
        if dst is S.ACTIVE:
            continue
        assert not _d(mutation=M.PROMOTE, from_state=S.REVIEW, to_state=dst, principal=P.HUMAN)


def test_promote_sources_are_review_and_verified_only():
    for src in ALL_STATES:
        allowed = bool(_d(mutation=M.PROMOTE, from_state=src, to_state=S.ACTIVE,
                          principal=P.HUMAN))
        assert allowed is (src in (S.REVIEW, S.VERIFIED)), src.value


def test_archive_may_retire_a_review_note():
    """Operationally legal even though the bare field rewrite is not -- this is
    precisely the distinction STRUCTURAL_REWRITE exists to preserve."""
    assert _d(mutation=M.ARCHIVE, from_state=S.REVIEW, to_state=S.ARCHIVED, principal=P.HUMAN)
    assert not _d(mutation=M.STRUCTURAL_REWRITE, from_state=S.REVIEW,
                  to_state=S.ARCHIVED, principal=P.HUMAN)


def test_archive_is_not_idempotent():
    """Re-archiving would overwrite the existing archive_reason and audit trail."""
    assert not _d(mutation=M.ARCHIVE, from_state=S.ARCHIVED, to_state=S.ARCHIVED,
                  principal=P.HUMAN)


def test_archive_accepts_every_other_state():
    for src in ALL_STATES:
        allowed = bool(_d(mutation=M.ARCHIVE, from_state=src, to_state=S.ARCHIVED,
                          principal=P.HUMAN))
        assert allowed is (src is not S.ARCHIVED), src.value


def test_supersede_cannot_retire_terminal_notes():
    for src in (S.ARCHIVED, S.SUPERSEDED):
        assert not _d(mutation=M.SUPERSEDE, from_state=src, to_state=S.SUPERSEDED,
                      principal=P.HUMAN)


def test_reconsolidation_challenges_only_settled_states():
    for src in ALL_STATES:
        allowed = bool(_d(mutation=M.RECONSOLIDATE_CHALLENGE, from_state=src,
                          to_state=S.RECONSOLIDATING, principal=P.HUMAN))
        assert allowed is (src in (S.ACTIVE, S.VERIFIED)), src.value


def test_reconsolidation_resolves_only_from_reconsolidating():
    for dst in (S.ACTIVE, S.REVIEW):
        assert _d(mutation=M.RECONSOLIDATE_RESOLVE, from_state=S.RECONSOLIDATING,
                  to_state=dst, principal=P.HUMAN)
        for src in ALL_STATES:
            if src is S.RECONSOLIDATING:
                continue
            assert not _d(mutation=M.RECONSOLIDATE_RESOLVE, from_state=src,
                          to_state=dst, principal=P.HUMAN)


def test_reconsolidation_resolve_cannot_reach_arbitrary_states():
    for dst in ALL_STATES:
        if dst in (S.ACTIVE, S.REVIEW):
            continue
        assert not _d(mutation=M.RECONSOLIDATE_RESOLVE, from_state=S.RECONSOLIDATING,
                      to_state=dst, principal=P.HUMAN)


def test_a_mutation_cannot_borrow_another_mutations_permissions():
    """REVIEW -> ACTIVE is legal via PROMOTE and must stay illegal elsewhere."""
    assert _d(mutation=M.PROMOTE, from_state=S.REVIEW, to_state=S.ACTIVE, principal=P.HUMAN)
    for mutation in (M.REVIEW, M.ARCHIVE, M.SUPERSEDE,
                     M.RECONSOLIDATE_CHALLENGE, M.RECONSOLIDATE_RESOLVE):
        assert not _d(mutation=mutation, from_state=S.REVIEW, to_state=S.ACTIVE,
                      principal=P.HUMAN)


# ---------------------------------------------------------------------------
# Principal constraints
# ---------------------------------------------------------------------------


@pytest.mark.parametrize("mutation,from_state,to_state", [
    (M.REVIEW, S.RAW, S.REVIEW),
    (M.PROMOTE, S.REVIEW, S.ACTIVE),
    (M.ARCHIVE, S.ACTIVE, S.ARCHIVED),
    (M.RECONSOLIDATE_CHALLENGE, S.ACTIVE, S.RECONSOLIDATING),
    (M.RECONSOLIDATE_RESOLVE, S.RECONSOLIDATING, S.ACTIVE),
])
def test_ai_agent_cannot_drive_trust_conferring_mutations(mutation, from_state, to_state):
    assert not _d(mutation=mutation, from_state=from_state, to_state=to_state,
                  principal=P.AI_AGENT)
    assert _d(mutation=mutation, from_state=from_state, to_state=to_state,
              principal=P.HUMAN)
    assert _d(mutation=mutation, from_state=from_state, to_state=to_state,
              principal=P.ADMIN)


def test_reconsolidation_bypass_stays_closed():
    """REGRESSION for the documented P0: an AI agent must not reconsolidate."""
    assert not _d(mutation=M.RECONSOLIDATE_CHALLENGE, from_state=S.ACTIVE,
                  to_state=S.RECONSOLIDATING, principal=P.AI_AGENT)
    assert not _d(mutation=M.RECONSOLIDATE_RESOLVE, from_state=S.RECONSOLIDATING,
                  to_state=S.ACTIVE, principal=P.AI_AGENT)


# ---------------------------------------------------------------------------
# The ADR verification gate
# ---------------------------------------------------------------------------


def test_promote_gate_defaults_to_shipping_behavior():
    """Default False == main's behavior, so unification is regression-free."""
    assert policy.RESTORE_PROMOTE_VERIFICATION_GATE is False
    assert _d(mutation=M.PROMOTE, from_state=S.REVIEW, to_state=S.ACTIVE,
              principal=P.HUMAN, verification="unverified")


def test_enabling_the_gate_requires_verified(monkeypatch):
    """Flipping the single constant is the whole ADR implementation."""
    monkeypatch.setattr(policy, "RESTORE_PROMOTE_VERIFICATION_GATE", True)

    assert not _d(mutation=M.PROMOTE, from_state=S.REVIEW, to_state=S.ACTIVE,
                  principal=P.HUMAN, verification="unverified")
    assert not _d(mutation=M.PROMOTE, from_state=S.REVIEW, to_state=S.ACTIVE,
                  principal=P.HUMAN, verification=None)
    assert not _d(mutation=M.PROMOTE, from_state=S.REVIEW, to_state=S.ACTIVE,
                  principal=P.HUMAN, verification="inferred")
    assert _d(mutation=M.PROMOTE, from_state=S.REVIEW, to_state=S.ACTIVE,
              principal=P.HUMAN, verification="verified")
    assert _d(mutation=M.PROMOTE, from_state=S.REVIEW, to_state=S.ACTIVE,
              principal=P.HUMAN, verification="  VERIFIED  ")


def test_gate_does_not_leak_into_other_mutations(monkeypatch):
    monkeypatch.setattr(policy, "RESTORE_PROMOTE_VERIFICATION_GATE", True)
    assert _d(mutation=M.ARCHIVE, from_state=S.ACTIVE, to_state=S.ARCHIVED,
              principal=P.HUMAN, verification="unverified")


# ---------------------------------------------------------------------------
# enforce()
# ---------------------------------------------------------------------------


def test_enforce_raises_on_denial_with_the_decision_reason():
    with pytest.raises(LifecycleViolation) as exc:
        policy.enforce(TR(mutation=M.PROMOTE, from_state=S.RAW,
                          to_state=S.ACTIVE, principal=P.HUMAN))
    assert str(exc.value).strip()


def test_enforce_is_silent_when_permitted():
    assert policy.enforce(TR(mutation=M.PROMOTE, from_state=S.REVIEW,
                             to_state=S.ACTIVE, principal=P.HUMAN)) is None


def test_lifecycle_violation_is_a_value_error():
    """Callers that already catch ValueError keep working."""
    assert issubclass(LifecycleViolation, ValueError)


# ---------------------------------------------------------------------------
# Purity
# ---------------------------------------------------------------------------


def test_policy_module_performs_no_io_and_imports_no_runtime_packages():
    """The authority must stay dependency-free so it cannot be circumvented by
    import order, and so it can never write state on its own."""
    import ast
    import pathlib

    src = pathlib.Path(policy.__file__).read_text(encoding="utf-8")
    tree = ast.parse(src)

    imported = set()
    for node in ast.walk(tree):
        if isinstance(node, ast.Import):
            imported.update(a.name.split(".")[0] for a in node.names)
        elif isinstance(node, ast.ImportFrom) and node.module:
            imported.add(node.module.split(".")[0])

    forbidden = {"memory_controller", "memory", "cognitive_core", "learning",
                 "os", "io", "open", "pathlib", "sqlite3", "requests", "logging"}
    assert not (imported & forbidden), sorted(imported & forbidden)
