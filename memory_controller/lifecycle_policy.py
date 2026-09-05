"""Canonical lifecycle transition policy for Memory Controller mutations.

This module is intentionally independent from storage and authorization. It
captures the transitions that the public mutation operations actually permit,
including conditional gates that are not represented by the lifecycle enum
alone (notably verification for promotion).
"""

from __future__ import annotations

from enum import Enum
from typing import Optional

from .controller import Lifecycle


class Mutation(str, Enum):
    """Lifecycle-changing mutation operations."""

    REVIEW = "review"
    PROMOTE = "promote"
    RECONSOLIDATE_CHALLENGE = "reconsolidate_challenge"
    RECONSOLIDATE_RESOLVE = "reconsolidate_resolve"
    ARCHIVE = "archive"
    SUPERSEDE = "supersede"


# The policy reflects the currently supported production mutation semantics.
# Verification is deliberately separate from lifecycle; PROMOTE is the one
# mutation whose lifecycle transition is conditional on verification state.
_TRANSITIONS = {
    Mutation.REVIEW: {
        Lifecycle.RAW: {Lifecycle.REVIEW},
        Lifecycle.CLASSIFIED: {Lifecycle.REVIEW},
        Lifecycle.NORMALIZED: {Lifecycle.REVIEW},
        Lifecycle.REVIEW: {Lifecycle.REVIEW},
    },
    Mutation.PROMOTE: {
        Lifecycle.REVIEW: {Lifecycle.ACTIVE},
    },
    Mutation.RECONSOLIDATE_CHALLENGE: {
        Lifecycle.ACTIVE: {Lifecycle.RECONSOLIDATING},
        Lifecycle.VERIFIED: {Lifecycle.RECONSOLIDATING},
    },
    Mutation.RECONSOLIDATE_RESOLVE: {
        Lifecycle.RECONSOLIDATING: {Lifecycle.REVIEW},
    },
    Mutation.ARCHIVE: {
        Lifecycle.REVIEW: {Lifecycle.ARCHIVED},
        Lifecycle.ACTIVE: {Lifecycle.ARCHIVED},
    },
    Mutation.SUPERSEDE: {
        Lifecycle.ACTIVE: {Lifecycle.SUPERSEDED},
    },
}


def is_transition_allowed(
    old: Lifecycle | str,
    new: Lifecycle | str,
    *,
    mutation: Mutation | str,
    verification: Optional[str] = None,
) -> bool:
    """Return whether a lifecycle transition is valid for a named mutation.

    The function is intentionally a pure predicate. Authorization remains the
    caller's responsibility, while verification remains a separate field.
    Promotion additionally requires ``verification == "verified"``.
    """

    try:
        old_state = old if isinstance(old, Lifecycle) else Lifecycle(str(old))
        new_state = new if isinstance(new, Lifecycle) else Lifecycle(str(new))
        operation = mutation if isinstance(mutation, Mutation) else Mutation(str(mutation))
    except ValueError:
        return False

    allowed_targets = _TRANSITIONS.get(operation, {}).get(old_state, set())
    if new_state not in allowed_targets:
        return False

    if operation is Mutation.PROMOTE and verification != "verified":
        return False

    return True


def allowed_targets(old: Lifecycle | str, *, mutation: Mutation | str) -> frozenset[Lifecycle]:
    """Return the canonical target states for a mutation from ``old``."""

    try:
        old_state = old if isinstance(old, Lifecycle) else Lifecycle(str(old))
        operation = mutation if isinstance(mutation, Mutation) else Mutation(str(mutation))
    except ValueError:
        return frozenset()
    return frozenset(_TRANSITIONS.get(operation, {}).get(old_state, set()))
