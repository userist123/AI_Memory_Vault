"""Navigation benchmark harness: run plan, answer parsing, URI mapping, grading and the decision statistics.
No model is called here."""
import importlib.util
import sys
from pathlib import Path

REPO = Path(__file__).resolve().parents[1]
_spec = importlib.util.spec_from_file_location("navb", REPO / "30_SCRIPTS" / "evaluation" / "navigation_benchmark.py")
navb = importlib.util.module_from_spec(_spec)
sys.modules["navb"] = navb
_spec.loader.exec_module(navb)

GOLD = {"id": "N01", "answer_path": "10_DOCUMENTATION/procedures/Backup.md", "fact_regex": r"\b7\s*days\b"}
URIS = {"vault://procedures/backup": "10_DOCUMENTATION/procedures/Backup.md"}


def test_every_repeat_0_run_comes_before_any_repeat_1_run():
    plan = navb.plan_runs(["N01", "N02", "N03"], 2)
    assert len(plan) == 18
    reps = [r["repeat"] for r in plan]
    assert reps == sorted(reps)
    assert plan == navb.plan_runs(["N01", "N02", "N03"], 2)
    assert {r["condition"] for r in plan} == {"G", "I", "V"}


def test_a_path_or_a_vault_uri_both_grade_as_the_gold_file():
    by_path = {"answer": "text\nANSWER_PATH: `10_DOCUMENTATION/procedures/Backup.md`\nANSWER: kept for 7 days"}
    by_uri = {"answer": "ANSWER_PATH: vault://procedures/backup\nANSWER: Backups are kept 7 days."}
    with_repo = {"answer": "ANSWER_PATH: <REPO>\\10_DOCUMENTATION\\procedures\\Backup.md:12\nANSWER: 7 days"}
    for rec in (by_path, by_uri, with_repo):
        g = navb.grade(rec, GOLD, URIS)
        assert g["correct"], (rec, g)


def test_wrong_file_or_wrong_fact_or_no_answer_lines_are_not_correct():
    wrong_file = {"answer": "ANSWER_PATH: README.md\nANSWER: 7 days"}
    wrong_fact = {"answer": "ANSWER_PATH: 10_DOCUMENTATION/procedures/Backup.md\nANSWER: 30 days"}
    nothing = {"answer": "I could not find it."}
    g = navb.grade(wrong_file, GOLD, URIS)
    assert g["fact_ok"] and not g["path_ok"] and not g["correct"]
    g = navb.grade(wrong_fact, GOLD, URIS)
    assert g["path_ok"] and not g["fact_ok"] and not g["correct"]
    g = navb.grade(nothing, GOLD, URIS)
    assert not g["correct"] and g["path"] is None


def test_the_last_answer_lines_win():
    rec = {"answer": "ANSWER_PATH: README.md\nANSWER: 30 days\n...\nANSWER_PATH: 10_DOCUMENTATION/procedures/Backup.md\n"
                     "ANSWER: 7 days"}
    assert navb.grade(rec, GOLD, URIS)["correct"]


def test_dot_directories_keep_their_dot():
    assert navb.resolve_answer_path("./.agents/rules/x.md", {}) == ".agents/rules/x.md"
    assert navb.resolve_answer_path(".claude/agents/vault-worker.md:3", {}) == ".claude/agents/vault-worker.md"


def test_sign_test():
    assert navb.sign_test_p(0, 0) == 1.0
    assert navb.sign_test_p(6, 0) == 2 / 64
