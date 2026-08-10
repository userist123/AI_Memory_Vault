import subprocess
import os
from typing import List, Tuple

class GitTransactionError(RuntimeError):
    pass

class GitIntegration:
    """Helper for staging changes, validating, and atomic commits.

    Uses git commands; assumes the repository root is the workspace root.
    Does NOT automatically push or create commits without explicit call.
    """

    def __init__(self, repo_path: str = None):
        self.repo_path = repo_path or os.getcwd()
        if not self._is_git_repo():
            raise GitTransactionError("Not a git repository at {}".format(self.repo_path))

    def _run(self, args: List[str]) -> Tuple[int, str]:
        result = subprocess.run(args, cwd=self.repo_path, capture_output=True, text=True)
        return result.returncode, result.stdout.strip() + result.stderr.strip()

    def _is_git_repo(self) -> bool:
        code, _ = self._run(["git", "rev-parse", "--is-inside-work-tree"])
        return code == 0

    def status(self) -> str:
        code, out = self._run(["git", "status", "--porcelain"])
        if code != 0:
            raise GitTransactionError("git status failed: " + out)
        return out

    def stage(self, paths: List[str]) -> None:
        # Stage files for commit
        args = ["git", "add"] + paths
        code, out = self._run(args)
        if code != 0:
            raise GitTransactionError("git add failed: " + out)

    def validate(self) -> None:
        # Simple validation: ensure no deleted files staged unintentionally
        # Here we just run git diff --cached --name-status and check for D entries
        code, out = self._run(["git", "diff", "--cached", "--name-status"])
        if code != 0:
            raise GitTransactionError("git diff failed: " + out)
        for line in out.splitlines():
            if line.startswith("D "):
                raise GitTransactionError("Attempted to delete file in transaction: " + line)

    def commit(self, message: str) -> None:
        # Perform atomic commit after staging and validation
        self.validate()
        code, out = self._run(["git", "commit", "-m", message])
        if code != 0:
            raise GitTransactionError("git commit failed: " + out)

    def revert_last(self) -> None:
        # Revert the most recent commit (does not use reset --hard)
        code, out = self._run(["git", "revert", "--no-edit", "HEAD"])
        if code != 0:
            raise GitTransactionError("git revert failed: " + out)
