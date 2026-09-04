import subprocess
from pathlib import Path

from cognitive_core.git_hooks import PromotionGitHook


def _init_repo(path: Path):
    subprocess.run(["git", "init"], cwd=path, check=True, capture_output=True)
    subprocess.run(["git", "config", "user.email", "test@example.com"], cwd=path, check=True, capture_output=True)
    subprocess.run(["git", "config", "user.name", "Test"], cwd=path, check=True, capture_output=True)


def test_hook_disabled_by_default(tmp_path):
    _init_repo(tmp_path)
    hook = PromotionGitHook(repo_path=str(tmp_path), enabled=False)
    assert hook.commit_promotion(["n1"], []) is None


def test_hook_noop_without_paths(tmp_path):
    _init_repo(tmp_path)
    hook = PromotionGitHook(repo_path=str(tmp_path), enabled=True)
    assert hook.commit_promotion(["n1"], []) is None


def test_hook_commits_when_enabled(tmp_path):
    _init_repo(tmp_path)
    note_file = tmp_path / "note.md"
    note_file.write_text("---\nid: n1\n---\ncontent\n", encoding="utf-8")
    subprocess.run(["git", "add", "."], cwd=tmp_path, check=True, capture_output=True)
    subprocess.run(["git", "commit", "-m", "init"], cwd=tmp_path, check=True, capture_output=True)
    note_file.write_text("---\nid: n1\n---\nupdated content\n", encoding="utf-8")
    hook = PromotionGitHook(repo_path=str(tmp_path), enabled=True)
    result = hook.commit_promotion(["n1"], [str(note_file)])
    assert result is not None and "n1" in result
