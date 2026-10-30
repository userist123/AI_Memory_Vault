"""private_identifier_guard.py: blocks staged additions, file names and commit messages that carry an identifier
from the owner's local list, never prints the identifier, and skips cleanly where the list does not exist (CI)."""
import importlib.util
import subprocess
from pathlib import Path

REPO = Path(__file__).resolve().parents[1]
_spec = importlib.util.spec_from_file_location("pig", REPO / "30_SCRIPTS" / "verification" / "private_identifier_guard.py")
pig = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(pig)

# synthetic identifiers only: a name, a machine name, a profile path
IDS = ["Zorvan", "ZRV-WS42", "C:\\Users\\Zorvan"]  # hygiene: intentional-absolute-path


def _repo(tmp_path):
    subprocess.run(["git", "init", "-q"], cwd=tmp_path, check=True)
    subprocess.run(["git", "config", "user.email", "t@example.invalid"], cwd=tmp_path, check=True)
    subprocess.run(["git", "config", "user.name", "t"], cwd=tmp_path, check=True)
    lst = tmp_path / "ids.txt"
    lst.write_text("# private\n" + "\n".join(IDS) + "\n", encoding="utf-8")
    return lst


def _stage(tmp_path, rel, text):
    p = tmp_path / rel
    p.parent.mkdir(parents=True, exist_ok=True)
    p.write_text(text, encoding="utf-8")
    subprocess.run(["git", "add", rel], cwd=tmp_path, check=True)


def test_staged_hits_are_blocked_and_never_echoed(tmp_path, capsys):
    lst = _repo(tmp_path)
    _stage(tmp_path, "docs/report.md", "Repository: `C:\\Users\\Zorvan\\Documents\\vault`\ncase CASE-ZRV-WS42-1\n")  # hygiene: intentional-absolute-path
    _stage(tmp_path, "docs/clean.md", "nothing here\n")
    rc = pig.main(["--staged", "--list", str(lst), "--repo", str(tmp_path)])
    out = capsys.readouterr().out
    assert rc == 1
    assert "PRIVATE_IDENTIFIER:docs/report.md:#1,#2,#3" in out
    assert "clean.md" not in out
    assert "Zorvan" not in out and "ZRV-WS42" not in out


def test_file_names_and_commit_messages_count(tmp_path, capsys):
    lst = _repo(tmp_path)
    _stage(tmp_path, "imported/Routine_Zorvan.ics", "BEGIN:VCALENDAR\n")
    msg = tmp_path / "msg.txt"
    msg.write_text("fix report from zrv-ws42\n", encoding="utf-8")
    rc = pig.main(["--staged", "--list", str(lst), "--repo", str(tmp_path), "--message-file", str(msg)])
    out = capsys.readouterr().out
    assert rc == 1
    assert "PRIVATE_IDENTIFIER:imported/Routine_Zorvan.ics:#1" in out
    assert "PRIVATE_IDENTIFIER:<commit message>:#2" in out


def test_a_short_name_does_not_hit_a_longer_word_and_removals_do_not_count(tmp_path, capsys):
    lst = _repo(tmp_path)
    _stage(tmp_path, "a.md", "hint by Zorvanski, a third-party author\n")
    subprocess.run(["git", "commit", "-q", "-m", "x"], cwd=tmp_path, check=True)
    (tmp_path / "b.md").write_text("old Zorvan\n", encoding="utf-8")
    subprocess.run(["git", "add", "b.md"], cwd=tmp_path, check=True)
    subprocess.run(["git", "commit", "-q", "-m", "y"], cwd=tmp_path, check=True)
    (tmp_path / "b.md").write_text("owner\n", encoding="utf-8")  # the redaction itself
    subprocess.run(["git", "add", "b.md"], cwd=tmp_path, check=True)
    assert pig.main(["--staged", "--list", str(lst), "--repo", str(tmp_path)]) == 0
    assert pig.main(["--tree", "--list", str(lst), "--repo", str(tmp_path)]) == 0
    capsys.readouterr()


def test_deleting_a_file_that_carries_an_identifier_is_not_blocked(tmp_path, capsys):
    lst = _repo(tmp_path)
    _stage(tmp_path, "imported/Routine_Zorvan.ics", "BEGIN:VCALENDAR Zorvan\n")
    subprocess.run(["git", "commit", "-q", "-m", "x"], cwd=tmp_path, check=True)
    subprocess.run(["git", "rm", "-q", "imported/Routine_Zorvan.ics"], cwd=tmp_path, check=True)
    assert pig.main(["--staged", "--list", str(lst), "--repo", str(tmp_path)]) == 0
    capsys.readouterr()


def test_without_the_list_the_guard_skips_unless_required(tmp_path, capsys):
    _repo(tmp_path)
    missing = tmp_path / "nope.txt"
    assert pig.main(["--tree", "--list", str(missing), "--repo", str(tmp_path)]) == 0
    assert pig.main(["--tree", "--list", str(missing), "--repo", str(tmp_path), "--require-list"]) == 1
    assert "skipped" in capsys.readouterr().err
