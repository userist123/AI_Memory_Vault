"""Canonical lifecycle transition policy for Memory Controller mutations.

The module is intentionally independent from storage, authorization, and the
controller implementation. It describes lifecycle values as strings so it can
be imported by the controller later without circular dependencies.
"""

from __future__ import annotations

from enum import Enum
from typing import Optional


class Mutation(str, Enum):
    """Lifecycle-changing mutation operations."""

    REVIEW = "review"
    PROMOTE = "promote"
    RECONSOLIDATE_CHALLENGE = "reconsolidate_challenge"
    RECONSOLIDATE_RESOLVE = "reconsolidate_resolve"
    ARCHIVE = "archive"
    SUPERSEDE = "supersede"


# The policy reflects the mutation semantics currently implemented in
# production. Verification remains a separate trust field; PROMOTE is the one
# lifecycle transition that requires a verified note.
_TRANSITIONS = {
    Mutation.REVIEW: {
        "RAW": {"REVIEW"},
        "CLASSIFIED": {"REVIEW"},
        "NORMALIZED": {"REVIEW"},
        "REVIEW": {"REVIEW"},
    },
    Mutation.PROMOTE: {
        "REVIEW": {"ACTIVE"},
    },
    Mutation.RECONSOLIDATE_CHALLENGE: {
        "ACTIVE": {"RECONSOLIDATING"},
        "VERIFIED": {"RECONSOLIDATING"},
    },
    Mutation.RECONSOLIDATE_RESOLVE: {
        "RECONSOLIDATING": {"REVIEW"},
    },
    Mutation.ARCHIVE: {
        "REVIEW": {"ARCHIVED"},
        "ACTIVE": {"ARCHIVED"},
    },
    Mutation.SUPERSEDE: {
        "ACTIVE": {"SUPERSEDED"},
    },
}


def _value(value: object) -> str:
    """Normalize enums and plain strings to lifecycle/operation values."""

    raw = getattr(value, "value", value)
    return str(raw)


def is_transition_allowed(
    old: object,
    new: object,
    *,
    mutation: Mutation | str,
    verification: Optional[str] = None,
) -> bool:
    """Return whether a lifecycle transition is valid for a named mutation.

    Authorization is deliberately outside this pure policy function.
    Verification is also separate from lifecycle, except that promotion is
    conditionally allowed only for a note whose verification is ``verified``.
    Unknown values fail closed.
    """

    old_state = _value(old)
    new_state = _value(new)
    operation_value = _value(mutation)
    try:
        operation = Mutation(operation_value)
    except ValueError:
        return False

    if new_state not in _TRANSITIONS.get(operation, {}).get(old_state, set()):
        return False

    if operation is Mutation.PROMOTE and verification != "verified":
        return False

    return True


def allowed_targets(old: object, *, mutation: Mutation | str) -> frozenset[str]:
    """Return canonical lifecycle target values for a mutation from ``old``."""

    old_state = _value(old)
    operation_value = _value(mutation)
    try:
        operation = Mutation(operation_value)
    except ValueError:
        return frozenset()
    return frozenset(_TRANSITIONS.get(operation, {}).get(old_state, set()))
