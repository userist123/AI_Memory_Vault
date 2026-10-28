"""The Prompt Master has a PASS/FAIL gate and a golden set that runs on every change.

`prompt_master_eval.py check` must pass on the repository as it is. The other
tests prove each check can fail: a gate that cannot fail verifies nothing.
"""
from __future__ import annotations

import copy
import importlib.util
import sys
from pathlib import Path

import pytest

REPO = Path(__file__).resolve().parents[1]
_spec = importlib.util.spec_from_file_location(
    "prompt_master_eval", REPO / "30_SCRIPTS" / "prompt" / "prompt_master_eval.py")
pme = importlib.util.module_from_spec(_spec)
sys.modules[_spec.name] = pme  # dataclasses resolve annotations through sys.modules
_spec.loader.exec_module(pme)


@pytest.fixture(scope="module")
def routing() -> dict:
    return pme.load_routing()


@pytest.fixture(scope="module")
def golden() -> dict:
    return pme.load_golden()


# ------------------------------------------------------------ the gate passes

def test_check_passes_on_the_repository():
    report = pme.run_all()
    assert report.passed, [(c.id, c.detail) for c in report.failures()]


def test_cli_check_exit_code_is_zero(capsys):
    assert pme.main(["check"]) == 0
    assert "verdict: PASS" in capsys.readouterr().out


def test_every_routing_rule_has_a_golden_case(routing, golden):
    expected_rules = {c["expected"]["rule"] for c in golden["cases"]}
    assert {r["id"] for r in routing["rules"]} <= expected_rules


def test_golden_set_holds_both_verdicts(golden):
    verdicts = {ref["verdict"] for c in golden["cases"] for ref in c.get("reference_outputs", [])}
    assert verdicts == {"PASS", "FAIL"}


# ------------------------------------------------------------ routing

@pytest.mark.parametrize("text,rule", [
    ("Vreau un magazin online pentru ceai", "mvp"),
    ("Aplicația are o eroare la login și crapă", "bug"),
    ("audit de securitate pentru API", "security"),
    ("Vreau o platformă, dar nu știu ce arhitectură să aleg", "ambiguous"),
    ("bună ziua", "ambiguous"),
    ("scrie-mi un prompt pentru ChatGPT despre CV-uri", "prompt_for_tool"),
])
def test_route_examples(routing, text, rule):
    assert pme.route(text, routing)["rule"] == rule


def test_route_ignores_diacritics(routing):
    assert pme.route("taxe și impozite", routing) == pme.route("taxe si impozite", routing)


def test_whole_word_keywords_do_not_match_inside_words(routing):
    # "ui" is a whole-word keyword: "uitat" must not route to the UI rule
    assert pme.route("am uitat parola", routing)["rule"] == "ambiguous"


def test_a_misrouted_golden_case_fails(routing, golden):
    bad = copy.deepcopy(golden)
    bad["cases"][0]["expected"] = {"mode": "execute", "rule": "backend", "templates": [7]}
    report = pme.Report()
    pme.check_golden(report, routing, bad)
    assert not report.passed
    assert any(c.id.endswith("/route") for c in report.failures())


def test_an_uncovered_rule_fails(routing, golden):
    bad = copy.deepcopy(golden)
    bad["cases"] = [c for c in bad["cases"] if c["expected"]["rule"] != "taxes"]
    report = pme.Report()
    pme.check_golden(report, routing, bad)
    assert [c.id for c in report.failures()] == ["G0-every-rule-has-a-golden-case"]


# ------------------------------------------------------------ docs drift

def test_routing_json_drifting_from_the_table_fails(routing):
    bad = copy.deepcopy(routing)
    next(r for r in bad["rules"] if r["id"] == "mvp")["templates"] = [2, 13]
    report = pme.Report()
    pme.check_docs(report, bad)
    assert [c.id for c in report.failures()] == ["D5-routing-table-matches-json"]


def test_missing_template_heading_fails(monkeypatch, tmp_path, routing):
    text = pme.TABLE_MD.read_text(encoding="utf-8").replace("### 7. ", "### 7x. ", 1)
    fake = tmp_path / "PROMPT_MASTER_TABLE.md"
    fake.write_text(text, encoding="utf-8")
    monkeypatch.setattr(pme, "TABLE_MD", fake)
    report = pme.Report()
    pme.check_docs(report, routing)
    assert "D2-twenty-templates" in {c.id for c in report.failures()}


def test_portable_block_without_credential_rule_fails(monkeypatch, tmp_path, routing):
    text = pme.PORTABLE_MD.read_text(encoding="utf-8").replace("**Credential safety**", "**Notes**")
    fake = tmp_path / "PROMPT_MASTER_PORTABLE.md"
    fake.write_text(text, encoding="utf-8")
    monkeypatch.setattr(pme, "PORTABLE_MD", fake)
    report = pme.Report()
    pme.check_docs(report, routing)
    assert [c.id for c in report.failures()] == ["P4-credential-safety"]


# ------------------------------------------------------------ golden file schema

@pytest.mark.parametrize("mutate,message", [
    (lambda g: g.update(schema_version=2), "schema_version"),
    (lambda g: g["cases"][0].update(id="mvp"), "bad case id"),
    (lambda g: g["cases"][0].update(extra=1), "unknown keys"),
    (lambda g: g["cases"][0]["expected"].update(mode="guess"), "expected"),
    (lambda g: g["cases"].append(dict(g["cases"][0])), "duplicate"),
])
def test_invalid_golden_file_is_a_config_error(golden, mutate, message):
    bad = copy.deepcopy(golden)
    mutate(bad)
    with pytest.raises(pme.ConfigError, match=message):
        pme.validate_golden(bad)


# ------------------------------------------------------------ output rubric

EXEC = {"mode": "execute", "rule": "mvp", "templates": [2, 13, 8]}
GOOD_EXEC = ("> Senior full-stack engineer (#2, #13, #8) building a booking site in Next.js.\n\n"
             + "Execution. " * 30 + "\n\n```ts\nexport const x = 1;\n```\n")


def _failed(text: str, expected: dict) -> list[str]:
    return [c.id for c in pme.score_output(text, expected).failures()]


def test_execute_answer_passes():
    assert _failed(GOOD_EXEC, EXEC) == []


@pytest.mark.parametrize("placeholder", ["[PRODUCT TYPE]", "[MODERN/MINIMAL/BOLD/etc]", "[Authoritative/Technical]"])
def test_unfilled_placeholder_fails(placeholder):
    text = GOOD_EXEC.replace("booking site", placeholder)
    assert _failed(text, EXEC) == ["O3-variables-filled"]


def test_markdown_link_and_verification_marker_are_not_placeholders():
    text = GOOD_EXEC.replace("booking site", "[docs](https://example.com) [NEEDS VERIFICATION]")
    assert _failed(text, EXEC) == []


def test_answer_without_mega_prompt_fails():
    text = GOOD_EXEC.replace("> ", "", 1)
    assert "O1-mega-prompt-quoted" in _failed(text, EXEC)


def test_finance_answer_needs_disclaimer():
    stock = {"mode": "execute", "rule": "stock", "templates": [19]}
    text = "> (#19) Analyst, NVDA, long term.\n\n" + "Analysis. " * 30
    assert _failed(text, stock) == ["O6-finance-disclaimer"]
    assert _failed(text + "\nThis is not financial advice.", stock) == []


@pytest.mark.parametrize("secret", [
    "sk-ant-" + "a" * 30,
    "AKIA" + "B" * 16,
    "ghp_" + "c" * 36,
    "-----BEGIN RSA " + "PRIVATE KEY-----",
    'password = "' + 'EXAMPLE_FAKE_PASSWORD' + '"',
])
def test_credentials_fail_in_every_mode(secret):
    for expected in (EXEC, {"mode": "clarify", "rule": "ambiguous", "templates": [9]},
                     {"mode": "prompt_for_tool", "rule": "prompt_for_tool", "templates": []}):
        assert "S1-no-credentials" in _failed(GOOD_EXEC + "\n" + secret, expected)


def test_clarify_needs_three_or_four_questions_and_no_code():
    clarify = {"mode": "clarify", "rule": "ambiguous", "templates": [9]}
    good = "#9 AI Technical Lead Mode\n1. A?\n2. B?\n3. C?\n"
    assert _failed(good, clarify) == []
    assert _failed("#9\n1. A?\n2. B?\n", clarify) == ["C1-three-to-four-questions"]
    assert _failed(good + "```py\nx=1\n```\n", clarify) == ["C2-no-code-yet"]


def test_prompt_for_tool_needs_one_block_and_target():
    tool = {"mode": "prompt_for_tool", "rule": "prompt_for_tool", "templates": []}
    good = "```text\nA cabin at dusk --ar 3:2\n```\n\n🎯 Target: Midjourney — subject first.\n"
    assert _failed(good, tool) == []
    assert _failed(good.replace("🎯 Target:", "Target:"), tool) == ["T2-target-line"]
    assert _failed(good + "```text\nsecond\n```\n", tool) == ["T1-one-prompt-block"]


def test_cli_score_unknown_case_is_usage_error(tmp_path, capsys):
    answer = tmp_path / "a.md"
    answer.write_text("x", encoding="utf-8")
    assert pme.main(["score", "--case", "G99-nope", "--output", str(answer)]) == 2
