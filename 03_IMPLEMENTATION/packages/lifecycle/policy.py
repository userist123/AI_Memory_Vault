"""Canonical lifecycle policy authority.

This module is the ONLY place in the runtime that decides whether a memory
note may move from one lifecycle state to another. Every mutation path --
propose, review, promote, update, attest, archive, supersede, reconsolidation
challenge/resolve, and queue promotion -- must route its decision through
`evaluate()` / `enforce()` here.

Design contract
---------------
1. **Single authority.** No mutation path may re-implement, widen, or
   shortcut these rules locally. A caller may only ever be *more* restrictive
   by refusing to call a mutation at all; it may never grant a transition the
   policy denies.
2. **Fail closed.** `evaluate()` returns DENY for anything not explicitly
   listed. Unknown states, unknown mutations, unknown principals, malformed
   input and `None` all deny. There is no "allow by default" branch and no
   compatibility escape hatch.
3. **Pure.** No I/O, no storage access, no audit side effects, no mutation of
   its arguments. Callers remain responsible for their own audit logging, so
   that a policy decision can never silently rewrite state on its own.
4. **State-complete.** All nine canonical states are modelled:
   RAW, CLASSIFIED, NORMALIZED, REVIEW, VERIFIED, ACTIVE, RECONSOLIDATING,
   SUPERSEDED, ARCHIVED.

Relationship to authorization
-----------------------------
This policy answers "is this lifecycle transition legal for this principal?".
It does NOT replace `Authorizer` (which answers "may this principal invoke
this operation at all?"). Both must pass. Callers invoke `_check_auth()`
first, then `enforce()`; the policy is the second gate, never the only one.

Verification gate (see RESTORE_PROMOTE_VERIFICATION_GATE)
--------------------------------------------------------
The ADR decision `REVIEW -> VERIFIED -> ACTIVE` (no auto-attestation) is
represented by a single module constant rather than scattered conditionals.
It currently defaults to the behavior shipping on `main` so that unifying the
policy introduces zero behavioral regression. Flipping that one constant is
the entire implementation of the ADR decision -- which is the point of having
a single source of truth.
"""
from __future__ import annotations

from dataclasses import dataclass
from enum import Enum
from typing import FrozenSet, Optional


class LifecycleState(str, Enum):
    """The nine canonical lifecycle states."""

    RAW = "RAW"
    CLASSIFIED = "CLASSIFIED"
    NORMALIZED = "NORMALIZED"
    REVIEW = "REVIEW"
    VERIFIED = "VERIFIED"
    ACTIVE = "ACTIVE"
    RECONSOLIDATING = "RECONSOLIDATING"
    SUPERSEDED = "SUPERSEDED"
    ARCHIVED = "ARCHIVED"


class Mutation(str, Enum):
    """The operation requesting a lifecycle transition.

    The mutation matters as much as the state pair: `REVIEW -> ACTIVE` is
    legal via PROMOTE and illegal via RECONSOLIDATE_RESOLVE. Encoding the
    operation prevents one path from borrowing another path's permissions.
    """

    CREATE = "create"                                    # propose()
    #: A bare lifecycle field rewrite with no owning operation -- e.g. a note
    #: re-saved through propose()/import with a changed lifecycle. This is the
    #: *structural* pipeline question ("is this field value sequence legal?"),
    #: which is deliberately stricter and distinct from the operational
    #: question ("may archive() retire this note?"). Both live here so there is
    #: exactly one module holding lifecycle truth.
    STRUCTURAL_REWRITE = "structural_rewrite"            # _validate_note()
    REVIEW = "review"                                    # review()
    PROMOTE = "promote"                                  # promote()
    UPDATE = "update"                                    # update()
    ATTEST = "attest"                                    # attest()
    ARCHIVE = "archive"                                  # archive()
    SUPERSEDE = "supersede"                              # supersede() (predecessor)
    RECONSOLIDATE_CHALLENGE = "reconsolidate_challenge"  # Consolidator.challenge()
    RECONSOLIDATE_RESOLVE = "reconsolidate_resolve"      # Consolidator.resolve_challenge()


class PrincipalRole(str, Enum):
    """Principal roles, mirrored as plain strings so this module stays free of
    any dependency on the memory package (and thus of circular imports)."""

    HUMAN = "human"
    AI_AGENT = "ai_agent"
    ADMIN = "admin"


class LifecycleViolation(ValueError):
    """Raised by `enforce()` when a transition is denied."""


@dataclass(frozen=True)
class Decision:
    allowed: bool
    reason: str

    def __bool__(self) -> bool:
        return self.allowed


@dataclass(frozen=True)
class TransitionRequest:
    """A single lifecycle transition proposal.

    `from_state` is None only for CREATE. `verification` is the note's CURRENT
    verification value; the policy never reads or writes it beyond deciding.
    """

    mutation: Mutation
    to_state: LifecycleState
    principal: PrincipalRole
    from_state: Optional[LifecycleState] = None
    verification: Optional[str] = None


# ---------------------------------------------------------------------------
# Policy tables. Everything not listed here is denied.
# ---------------------------------------------------------------------------

#: States a principal may create a note directly into.
#: AI_AGENT is confined to the pre-trust states; HUMAN/ADMIN retain the wider
#: creation surface that ships today (schema validation still applies).
_CREATION_STATES = {
    PrincipalRole.AI_AGENT: frozenset({
        LifecycleState.RAW,
        LifecycleState.CLASSIFIED,
        LifecycleState.NORMALIZED,
        LifecycleState.REVIEW,
    }),
    PrincipalRole.HUMAN: frozenset({
        LifecycleState.RAW,
        LifecycleState.CLASSIFIED,
        LifecycleState.NORMALIZED,
        LifecycleState.REVIEW,
        LifecycleState.VERIFIED,
        LifecycleState.ACTIVE,
    }),
    PrincipalRole.ADMIN: frozenset({
        LifecycleState.RAW,
        LifecycleState.CLASSIFIED,
        LifecycleState.NORMALIZED,
        LifecycleState.REVIEW,
        LifecycleState.VERIFIED,
        LifecycleState.ACTIVE,
    }),
}

#: The strict structural pipeline. A bare lifecycle field rewrite (no owning
#: operation) may only advance one step along this chain. Operational
#: mutations below are evaluated separately and are intentionally broader --
#: `archive()` may retire a REVIEW note even though a bare REVIEW -> ARCHIVED
#: field rewrite is structurally illegal.
_STRUCTURAL_PIPELINE: dict[LifecycleState, FrozenSet[LifecycleState]] = {
    LifecycleState.RAW: frozenset({LifecycleState.CLASSIFIED}),
    LifecycleState.CLASSIFIED: frozenset({LifecycleState.NORMALIZED}),
    LifecycleState.NORMALIZED: frozenset({LifecycleState.REVIEW}),
    LifecycleState.REVIEW: frozenset({LifecycleState.VERIFIED}),
    LifecycleState.VERIFIED: frozenset({LifecycleState.ACTIVE}),
    LifecycleState.ACTIVE: frozenset({LifecycleState.SUPERSEDED, LifecycleState.ARCHIVED}),
    LifecycleState.RECONSOLIDATING: frozenset(),
    LifecycleState.SUPERSEDED: frozenset(),
    LifecycleState.ARCHIVED: frozenset(),
}

#: (mutation, to_state) -> the set of from_states the transition may start in.
_TRANSITIONS: dict[tuple[Mutation, LifecycleState], FrozenSet[LifecycleState]] = {
    # review() moves any pre-trust state into REVIEW, including REVIEW itself
    # (idempotent re-review with a fresh decision record).
    (Mutation.REVIEW, LifecycleState.REVIEW): frozenset({
        LifecycleState.RAW,
        LifecycleState.CLASSIFIED,
        LifecycleState.NORMALIZED,
        LifecycleState.REVIEW,
    }),
    # promote() is the only path into ACTIVE from the review pipeline.
    (Mutation.PROMOTE, LifecycleState.ACTIVE): frozenset({
        LifecycleState.REVIEW,
        LifecycleState.VERIFIED,
    }),
    # archive() retires a note. Re-archiving an ARCHIVED note is denied so the
    # existing archive_reason/audit trail cannot be silently overwritten.
    (Mutation.ARCHIVE, LifecycleState.ARCHIVED): frozenset({
        LifecycleState.RAW,
        LifecycleState.CLASSIFIED,
        LifecycleState.NORMALIZED,
        LifecycleState.REVIEW,
        LifecycleState.VERIFIED,
        LifecycleState.ACTIVE,
        LifecycleState.RECONSOLIDATING,
        LifecycleState.SUPERSEDED,
    }),
    # supersede() retires the predecessor. An ARCHIVED note is terminal and a
    # SUPERSEDED one already points at a successor.
    (Mutation.SUPERSEDE, LifecycleState.SUPERSEDED): frozenset({
        LifecycleState.RAW,
        LifecycleState.CLASSIFIED,
        LifecycleState.NORMALIZED,
        LifecycleState.REVIEW,
        LifecycleState.VERIFIED,
        LifecycleState.ACTIVE,
        LifecycleState.RECONSOLIDATING,
    }),
    # Reconsolidation: only settled knowledge can be challenged.
    (Mutation.RECONSOLIDATE_CHALLENGE, LifecycleState.RECONSOLIDATING): frozenset({
        LifecycleState.ACTIVE,
        LifecycleState.VERIFIED,
    }),
    # Resolving a challenge either restores the note or demotes it to REVIEW.
    (Mutation.RECONSOLIDATE_RESOLVE, LifecycleState.ACTIVE): frozenset({
        LifecycleState.RECONSOLIDATING,
    }),
    (Mutation.RECONSOLIDATE_RESOLVE, LifecycleState.REVIEW): frozenset({
        LifecycleState.RECONSOLIDATING,
    }),
}

#: Mutations that must never change the lifecycle field at all.
_NON_TRANSITIONING = frozenset({Mutation.UPDATE, Mutation.ATTEST})

#: Principals permitted to drive each mutation's lifecycle transition.
#: This is a lifecycle-level constraint layered on top of `Authorizer`; it is
#: never a substitute for it.
_MUTATION_PRINCIPALS: dict[Mutation, FrozenSet[PrincipalRole]] = {
    Mutation.CREATE: frozenset({PrincipalRole.HUMAN, PrincipalRole.AI_AGENT, PrincipalRole.ADMIN}),
    # A structural rewrite carries no operation authority of its own; the
    # calling operation has already been authorized. Principal is therefore
    # not further narrowed here, but the pipeline table above stays strict.
    Mutation.STRUCTURAL_REWRITE: frozenset({PrincipalRole.HUMAN, PrincipalRole.AI_AGENT, PrincipalRole.ADMIN}),
    Mutation.REVIEW: frozenset({PrincipalRole.HUMAN, PrincipalRole.ADMIN}),
    Mutation.PROMOTE: frozenset({PrincipalRole.HUMAN, PrincipalRole.ADMIN}),
    Mutation.UPDATE: frozenset({PrincipalRole.HUMAN, PrincipalRole.AI_AGENT, PrincipalRole.ADMIN}),
    Mutation.ATTEST: frozenset({PrincipalRole.HUMAN, PrincipalRole.ADMIN}),
    Mutation.ARCHIVE: frozenset({PrincipalRole.HUMAN, PrincipalRole.ADMIN}),
    Mutation.SUPERSEDE: frozenset({PrincipalRole.HUMAN, PrincipalRole.AI_AGENT, PrincipalRole.ADMIN}),
    # Reconsolidation rewrites settled memory. An AI agent may never drive it:
    # this is the closure of the documented reconsolidation bypass.
    Mutation.RECONSOLIDATE_CHALLENGE: frozenset({PrincipalRole.HUMAN, PrincipalRole.ADMIN}),
    Mutation.RECONSOLIDATE_RESOLVE: frozenset({PrincipalRole.HUMAN, PrincipalRole.ADMIN}),
}

#: ADR `REVIEW -> VERIFIED -> ACTIVE`: when True, promote() additionally
#: requires `verification == "verified"`. Defaults to False to match the
#: behavior currently shipping on main; flipping it here enables the gate
#: everywhere at once, with no other code change.
RESTORE_PROMOTE_VERIFICATION_GATE = False

#: The verification value that satisfies the gate above.
_VERIFIED_VALUE = "verified"


def _coerce_state(value: object) -> Optional[LifecycleState]:
    """Best-effort coercion; returns None for anything not a canonical state.

    Accepts LifecycleState, a plain string, or any enum whose `.value` is a
    canonical state name (e.g. the controller's own `Lifecycle` enum).
    """
    if value is None:
        return None
    if isinstance(value, LifecycleState):
        return value
    raw = getattr(value, "value", value)
    if not isinstance(raw, str):
        return None
    try:
        return LifecycleState(raw)
    except ValueError:
        return None


def _coerce_principal(value: object) -> Optional[PrincipalRole]:
    if value is None:
        return None
    if isinstance(value, PrincipalRole):
        return value
    raw = getattr(value, "value", value)
    if not isinstance(raw, str):
        return None
    try:
        return PrincipalRole(raw)
    except ValueError:
        return None


def evaluate(request: TransitionRequest) -> Decision:
    """Decide a single transition. Fail-closed: unknown input denies."""
    if not isinstance(request, TransitionRequest):
        return Decision(False, "malformed transition request")

    mutation = request.mutation if isinstance(request.mutation, Mutation) else None
    if mutation is None:
        return Decision(False, f"unknown mutation: {request.mutation!r}")

    principal = _coerce_principal(request.principal)
    if principal is None:
        return Decision(False, f"unknown principal: {request.principal!r}")

    to_state = _coerce_state(request.to_state)
    if to_state is None:
        return Decision(False, f"unknown target lifecycle: {request.to_state!r}")

    allowed_principals = _MUTATION_PRINCIPALS.get(mutation, frozenset())
    if principal not in allowed_principals:
        return Decision(
            False,
            f"principal '{principal.value}' may not drive '{mutation.value}' "
            f"(permitted: {sorted(p.value for p in allowed_principals)})",
        )

    # Creation has no origin state.
    if mutation is Mutation.CREATE:
        if request.from_state is not None:
            return Decision(False, "create must not declare a from_state")
        permitted = _CREATION_STATES.get(principal, frozenset())
        if to_state not in permitted:
            return Decision(
                False,
                f"principal '{principal.value}' cannot create into '{to_state.value}' "
                f"(permitted: {sorted(s.value for s in permitted)})",
            )
        return Decision(True, "create permitted")

    from_state = _coerce_state(request.from_state)
    if from_state is None:
        return Decision(False, f"unknown source lifecycle: {request.from_state!r}")

    # Mutations that must not move the lifecycle at all.
    if mutation in _NON_TRANSITIONING:
        if from_state is not to_state:
            return Decision(
                False,
                f"'{mutation.value}' must not change lifecycle "
                f"({from_state.value} -> {to_state.value})",
            )
        return Decision(True, f"'{mutation.value}' leaves lifecycle unchanged")

    # Structural rewrites are judged against the strict linear pipeline.
    if mutation is Mutation.STRUCTURAL_REWRITE:
        if from_state is to_state:
            return Decision(True, "structural rewrite leaves lifecycle unchanged")
        permitted_next = _STRUCTURAL_PIPELINE.get(from_state, frozenset())
        if to_state not in permitted_next:
            return Decision(
                False,
                f"Invalid transition from Lifecycle.{from_state.name} to Lifecycle.{to_state.name}",
            )
        return Decision(True, f"structural {from_state.value} -> {to_state.value} permitted")

    permitted_sources = _TRANSITIONS.get((mutation, to_state))
    if permitted_sources is None:
        return Decision(
            False,
            f"'{mutation.value}' may not target '{to_state.value}'",
        )
    if from_state not in permitted_sources:
        return Decision(
            False,
            f"'{mutation.value}' cannot move {from_state.value} -> {to_state.value} "
            f"(permitted sources: {sorted(s.value for s in permitted_sources)})",
        )

    if (
        RESTORE_PROMOTE_VERIFICATION_GATE
        and mutation is Mutation.PROMOTE
        and to_state is LifecycleState.ACTIVE
        and str(request.verification or "").strip().lower() != _VERIFIED_VALUE
    ):
        return Decision(
            False,
            "only VERIFIED notes can be promoted to ACTIVE "
            "(attest() first; promote() never self-attests)",
        )

    return Decision(True, f"{from_state.value} -> {to_state.value} permitted for '{mutation.value}'")


def enforce(request: TransitionRequest) -> None:
    """`evaluate()` but raising `LifecycleViolation` on denial."""
    decision = evaluate(request)
    if not decision.allowed:
        raise LifecycleViolation(decision.reason)


def permitted_creation_states(principal: object) -> FrozenSet[LifecycleState]:
    """Creation states for a principal; empty (deny-all) if unrecognized."""
    role = _coerce_principal(principal)
    if role is None:
        return frozenset()
    return _CREATION_STATES.get(role, frozenset())
