"""Compatibility name for ``security.vault_secret_runtime`` (moved in WP-14b).

The per-user secret, state directory and usage log are a security concern used by lower
layers (``vault_access``), so the implementation lives in the ``security`` package (L0).
This module makes ``interfaces.vault_runtime`` the *same object* as the implementation, not
a re-export copy: callers and tests that patch attributes (``_WINDOWS``, ``_restrict_to_user``,
``subprocess``) on ``interfaces.vault_runtime`` patch the real module, and the module-level
``_LOG_LOCK`` exists once.
"""
import sys

from security import vault_secret_runtime as _impl

sys.modules[__name__] = _impl
