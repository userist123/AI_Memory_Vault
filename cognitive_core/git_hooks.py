"""Optional git auto-commit hook for controlled memory promotion events.

Only fires after QueuePromoter.promote_approved() succeeds. Uses the existing
memory_controller.git_integration.GitIntegration; never bypasses it, never
force-pushes, and never runs unless VAULT_GIT_AUTO_COMMIT=1.
"""
from __future__ import annotations

import os
from typing import List, Optional

try:
    from memory_controller.git_integration import GitIntegration, GitTransactionError
except Exception:  # pragma: no cover - controller package may not be importable in isolation
    GitIntegration = None

    class GitTransactionError(RuntimeError):
        pass


class PromotionGitHook:
    """Wraps a promotion event to auto-commit already-written note files.

    Disabled by default. Enable with VAULT_GIT_AUTO_COMMIT=1.
    Never mutates note content; only stages and commits files that
    MemoryController.propose() already wrote to disk.
    """

    def __init__(self, repo_path: Optional[str] = None, enabled: Optional[bool] = None):
        self.repo_path = repo_path or os.getcwd()
        self.enabled = enabled if enabled is not None else os.getenv("VAULT_GIT_AUTO_COMMIT", "0") == "1"

    def commit_promotion(self, promoted_ids: List[str], note_paths: List[str]) -> Optional[str]:
        if not self.enabled or not promoted_ids or GitIntegration is None:
            return None
        existing_paths = [p for p in note_paths if p]
        if not existing_paths:
            return None
        try:
            git = GitIntegration(repo_path=self.repo_path)
            git.stage(existing_paths)
            message = f"vault(promote): {', '.join(promoted_ids)}"
            git.commit(message)
            return message
        except GitTransactionError:
            return None
