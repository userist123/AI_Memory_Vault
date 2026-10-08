"""Tests for the real-model ablation harness and the committed B03 task packet (PR #209 B03).

The model callables below are FAKES that exercise the harness mechanics. Nothing here is evidence about any
note, any model or the track; the committed packet contains prompts only and these tests assert that it
contains no answer.
"""
import hashlib
import importlib.util
import json
from pathlib import Path

import pytest

from lifecycle.validation.book_to_memory_real_ablation import (
    COND_DECOY,
    COND_SKEPTIC,
    COND_WITH,
    COND_WITHOUT,
    CONDITIONS,
    MIN_PAIRS,
    PLACEHOLDER_MODEL_ID,
    QUESTION_TEMPLATE,
    SOURCE_TITLES,
    TEMPLATES,
    AblationHarnessError,
    TaskNote,
    analyze,
    analyze_contrast,
    assert_without_note_is_clean,
    build_task_list,
    build_trials,
    compare_runs,
    coverage_score,
    decide,
    parse_promoted_note,
    pick_decoy,
    read_external_run,
    render_prompt,
    run_config_template,
    run_trials,
    template_set_hash,
    write_task_packet,
)
from lifecycle.validation.book_to_memory_run_config import ConfigMismatchError, RunConfig, RunConfigError, config_of

REPO = Path(__file__).resolve().parents[1]
PACKET = REPO / "08_RESEARCH" / "BOOK_TO_MEMORY" / "b03_task_packet"
PREREG = REPO / "08_RESEARCH" / "BOOK_TO_MEMORY" / "PREREGISTRATION_B03_B05.md"

NOTE_TEXT = """---
id: "11111111-aaaa-bbbb-cccc-000000000001"
type: knowledge
lifecycle: REVIEW
provenance:
  source_type: import
  source_ref: "minsky_society_of_mind"
confidence: high
verification: unverified
---

# k-line

## Canonical Definition

Synthetic definition of the term.

## Grounding & Source Context

Extracted with high confidence.

> "A K-line is a wirelike structure that attaches / itself to active agents."
"""


def make_note(i, source="minsky_society_of_mind", term=None):
    return TaskNote("", f"id-{i:03d}", term or f"term {i}", source, f"synthetic definition number {i}",
                    f"synthetic source passage number {i} about widgets", f"path/{i}.md")


def make_tasks(n=45, sources=("minsky_society_of_mind", "ashby_design_for_a_brain", "wcs_1488")):
    return build_task_list([make_note(i, sources[i % len(sources)]) for i in range(n)])


def cfg(model="fake-model", **over):
    t = run_config_template()
    t.update(model_id=model, **over)
    return RunConfig.from_dict(t)


# ---------------------------------------------------------------- notes and selection
def test_parse_promoted_note_extracts_the_rule_fields():
    note, reason = parse_promoted_note(NOTE_TEXT, "x.md")
    assert reason is None
    assert (note.term, note.source_ref, note.note_id) == ("k-line", "minsky_society_of_mind", "11111111-aaaa-bbbb-cccc-000000000001")
    assert note.definition == "Synthetic definition of the term."
    assert note.passage.startswith("A K-line is a wirelike structure")


@pytest.mark.parametrize("mutate,reason", [
    (lambda t: t.replace("lifecycle: REVIEW", "lifecycle: ACTIVE"), "not REVIEW"),
    (lambda t: t.replace("verification: unverified", "verification: verified"), "not unverified"),
    (lambda t: t.replace("# k-line", ""), "heading"),
    (lambda t: t.replace("## Canonical Definition", "## Other"), "Canonical Definition"),
    (lambda t: t.replace('> "', "> '"), "source passage"),
    (lambda t: t.split("---", 2)[2], "frontmatter"),
])
def test_notes_that_fail_the_rule_are_dropped_with_a_reason(mutate, reason):
    note, why = parse_promoted_note(mutate(NOTE_TEXT))
    assert note is None and reason in why


def test_task_list_is_ordered_by_note_id_and_numbered():
    notes = [make_note(3), make_note(1), make_note(2)]
    tasks = build_task_list(notes)
    assert [t.note_id for t in tasks] == ["id-001", "id-002", "id-003"]
    assert [t.task_id for t in tasks] == ["T01", "T02", "T03"]


# ---------------------------------------------------------------- prompts
def test_without_prompt_has_no_note_content():
    tasks = make_tasks(5)
    for t in tasks:
        p = render_prompt(COND_WITHOUT, t)
        assert t.definition not in p and t.passage not in p and t.note_id not in p
        assert p == f"Question: {t.question}"
        assert_without_note_is_clean(p, t)
    with pytest.raises(AblationHarnessError):
        assert_without_note_is_clean(f"Question: {tasks[0].question} {tasks[0].definition}", tasks[0])


def test_with_prompts_show_only_term_definition_and_passage():
    t = make_tasks(5)[0]
    p = render_prompt(COND_WITH, t)
    assert t.definition in p and t.passage in p and t.question in p
    assert "confidence" not in p.lower() and "Grounding" not in p and t.note_id not in p
    assert render_prompt(COND_SKEPTIC, t) != p and "may be wrong" in render_prompt(COND_SKEPTIC, t)


def test_decoy_is_another_source_and_another_term():
    tasks = make_tasks(9)
    for i, t in enumerate(tasks):
        d = pick_decoy(tasks, i)
        assert d.source_ref != t.source_ref and d.term.lower() != t.term.lower()
    prompt = render_prompt(COND_DECOY, tasks[0], pick_decoy(tasks, 0))
    assert tasks[0].definition not in prompt and tasks[0].question in prompt
    with pytest.raises(AblationHarnessError):
        render_prompt(COND_DECOY, tasks[0])
    with pytest.raises(AblationHarnessError):
        render_prompt("BOGUS", tasks[0])


def test_decoy_falls_back_when_all_notes_share_one_source():
    tasks = make_tasks(4, sources=("minsky_society_of_mind",))
    assert pick_decoy(tasks, 0).term != tasks[0].term
    with pytest.raises(AblationHarnessError):
        pick_decoy(make_tasks(1), 0)


def test_source_clause_only_for_known_titles():
    known = make_note(1, "minsky_society_of_mind")
    opaque = make_note(2, "wcs_1488")
    assert SOURCE_TITLES["minsky_society_of_mind"] in known.question
    assert opaque.question == QUESTION_TEMPLATE.format(term="term 2", source_clause="")


def test_template_hash_changes_with_any_template():
    h = template_set_hash()
    assert len(h) == 64
    TEMPLATES[COND_WITH] += " "          # restored below
    try:
        assert template_set_hash() != h
    finally:
        TEMPLATES[COND_WITH] = TEMPLATES[COND_WITH][:-1]
    assert template_set_hash() == h


# ---------------------------------------------------------------- trials
def test_trials_cover_every_task_and_condition_once_with_an_interleaved_seeded_order():
    tasks = make_tasks(45)
    trials = build_trials(tasks, seed=5)
    assert len(trials) == 45 * len(CONDITIONS)
    assert len({t["trial_id"] for t in trials}) == len(trials)
    assert [t["order_index"] for t in trials] == list(range(1, len(trials) + 1))
    assert trials == build_trials(tasks, seed=5) and trials != build_trials(tasks, seed=6)
    # not grouped by condition: the first 20 trials already mix at least three conditions
    assert len({t["condition"] for t in trials[:20]}) >= 3
    for t in trials:
        assert t["answer_path"] == f"answers/{t['trial_id']}.txt"
        assert t["prompt_sha256"] == hashlib.sha256(t["prompt"].encode("utf-8")).hexdigest()


# ---------------------------------------------------------------- running a model callable
def test_run_trials_stores_a_stamped_transcript_per_trial(tmp_path):
    tasks = make_tasks(3)
    trials = build_trials(tasks)
    seen = []

    def fake_model(prompt):
        seen.append(prompt)
        return "fake answer about widgets"

    run = run_trials(trials, fake_model, cfg(), tmp_path / "tr")
    assert run["trials"] == 12 and run["failed"] == 0 and config_of(run).model_id == "fake-model"
    assert seen == [t["prompt"] for t in sorted(trials, key=lambda t: t["order_index"])]
    one = json.loads((tmp_path / "tr" / f"{trials[0]['trial_id']}.json").read_text(encoding="utf-8"))
    assert one["status"] == "OK" and one["prompt"] == trials[0]["prompt"] and config_of(one).seed == run_config_template()["seed"]


def test_a_failing_or_empty_model_is_recorded_as_failed_never_filled_in(tmp_path):
    trials = build_trials(make_tasks(2))
    calls = {"n": 0}

    def flaky(prompt):
        calls["n"] += 1
        if calls["n"] == 1:
            raise TimeoutError("backend down")
        return "" if calls["n"] == 2 else "fine"

    run = run_trials(trials, flaky, cfg(), tmp_path / "tr")
    assert run["failed"] == 2
    first = sorted(trials, key=lambda t: t["order_index"])[0]["trial_id"]
    rec = json.loads((tmp_path / "tr" / f"{first}.json").read_text(encoding="utf-8"))
    assert rec["status"] == "FAILED" and rec["answer"] is None and "TimeoutError" in rec["error"]


def test_run_trials_refuses_a_placeholder_model_or_a_drifted_config(tmp_path):
    trials = build_trials(make_tasks(2))
    with pytest.raises(AblationHarnessError):
        run_trials(trials, lambda p: "x", cfg(PLACEHOLDER_MODEL_ID), tmp_path / "a")
    with pytest.raises(ConfigMismatchError):
        run_trials(trials, lambda p: "x", cfg(temperature=0.9), tmp_path / "b")
    with pytest.raises(ConfigMismatchError):
        run_trials(trials, lambda p: "x", cfg(max_tokens=9999), tmp_path / "c")


# ---------------------------------------------------------------- analysis
def fake_scores(trials, with_gain, noise=0.0):
    """Scores from a deterministic fake: baseline 5 +/- a repeating wobble, plus ``with_gain`` for the real-note conditions."""
    scores = {}
    for t in trials:
        k = int(t["task_id"][1:])
        base = 5.0 + ((k % 5) - 2) * 0.5 + noise * ((k * 7) % 3 - 1)
        wobble = 1.0 + 0.2 * ((k % 4) - 1.5)             # the gain differs a little from task to task (non-zero variance)
        gain = with_gain * wobble if t["condition"] == COND_WITH else (with_gain / 2 * wobble if t["condition"] == COND_SKEPTIC else 0.0)
        scores[t["trial_id"]] = min(10.0, max(0.0, base + gain))
    return scores


def test_no_scores_means_insufficient_data_and_no_effect():
    trials = build_trials(make_tasks(45))
    res = analyze_contrast({}, trials, COND_WITH, COND_WITHOUT)
    assert res["status"] == "INSUFFICIENT_DATA" and res["pairs"] == 0
    assert "mean_diff" not in res and "p_two_sided" not in res and "verdict" not in res


def test_39_pairs_is_insufficient_and_40_is_enough():
    trials = build_trials(make_tasks(45))
    scores = fake_scores(trials, 1.5, noise=1.0)
    keep39 = {t["trial_id"] for t in trials if int(t["task_id"][1:]) <= 39}
    only39 = {k: v for k, v in scores.items() if k in keep39}
    assert analyze_contrast(only39, trials, COND_WITH, COND_WITHOUT)["status"] == "INSUFFICIENT_DATA"
    keep40 = {t["trial_id"] for t in trials if int(t["task_id"][1:]) <= 40}
    res = analyze_contrast({k: v for k, v in scores.items() if k in keep40}, trials, COND_WITH, COND_WITHOUT)
    assert res["status"] == "COMPLETE" and res["pairs"] == 40 == MIN_PAIRS


def test_a_pair_with_one_missing_score_is_dropped_not_imputed():
    trials = build_trials(make_tasks(45))
    scores = fake_scores(trials, 1.0, noise=1.0)
    del scores["B03-T07-WITH_NOTE"]
    res = analyze_contrast(scores, trials, COND_WITH, COND_WITHOUT)
    assert res["pairs"] == 44 and res["dropped_pairs"] == 1


def test_fake_model_that_ignores_the_note_shows_no_effect():
    trials = build_trials(make_tasks(45))
    res = analyze_contrast(fake_scores(trials, 0.0, noise=1.0), trials, COND_WITH, COND_WITHOUT, n_perm=2000, n_boot=500)
    assert res["mean_diff"] == 0 and res["verdict"] in ("INCONCLUSIVE", "NO_EFFECT_OF_PRACTICAL_SIZE")
    assert res["wins"] == res["losses"] == 0 and res["ties"] == 45


def test_fake_model_that_uses_the_note_shows_a_positive_effect_with_a_ci():
    trials = build_trials(make_tasks(45))
    res = analyze_contrast(fake_scores(trials, 2.0, noise=1.0), trials, COND_WITH, COND_WITHOUT,
                           n_perm=2000, n_boot=500, reliability_alpha=0.8)
    assert res["mean_diff"] == pytest.approx(2.0, abs=0.05)
    assert res["ci95"][0] > 0 and res["p_two_sided"] < 1e-6 and res["permutation"]["method"] == "monte_carlo"
    assert res["verdict"] == "SUPPORTED"


@pytest.mark.parametrize("alpha", [None, 0.5])
def test_unreliable_or_unknown_rater_agreement_cannot_support_the_result(alpha):
    trials = build_trials(make_tasks(45))
    res = analyze_contrast(fake_scores(trials, 2.0, noise=1.0), trials, COND_WITH, COND_WITHOUT,
                           n_perm=1000, n_boot=200, reliability_alpha=alpha)
    assert res["verdict"] == "UNRELIABLE_RATINGS"


def test_decision_rules_cover_the_other_outcomes():
    base = {"p_two_sided": 0.001, "ci95": [1.2, 2.0], "mean_diff": 1.6}
    assert decide(base, 0.9) == "SUPPORTED"
    assert decide({**base, "mean_diff": 0.8, "ci95": [0.3, 1.3]}, 0.9) == "INCONCLUSIVE"        # significant but below +1.0
    assert decide({"p_two_sided": 0.001, "ci95": [-2.0, -1.0], "mean_diff": -1.5}, 0.9) == "NOTE_MADE_ANSWERS_WORSE"
    assert decide({"p_two_sided": 0.7, "ci95": [-0.4, 0.5], "mean_diff": 0.05}, 0.9) == "NO_EFFECT_OF_PRACTICAL_SIZE"
    assert decide({"p_two_sided": 0.3, "ci95": [-1.5, 2.0], "mean_diff": 0.2}, 0.9) == "INCONCLUSIVE"
    assert decide({"p_two_sided": None, "ci95": [1, 1], "mean_diff": 1}, 0.9) == "INCONCLUSIVE"


def test_full_analysis_holm_adjusts_the_secondary_contrasts_and_is_stamped():
    trials = build_trials(make_tasks(45))
    res = analyze(fake_scores(trials, 2.0, noise=1.0), trials, cfg(), n_perm=500, n_boot=200, reliability_alpha=0.8)
    assert set(res["contrasts"]) == {"primary", "S1_framing", "S2_decoy", "S3_framing_contrast"}
    assert res["status"] == "COMPLETE" and config_of(res).model_id == "fake-model"
    assert "p_holm" not in res["contrasts"]["primary"]
    for name in ("S1_framing", "S3_framing_contrast"):
        assert res["contrasts"][name]["p_holm"] >= res["contrasts"][name]["p_two_sided"]
    # S2 compares two identical-score conditions (decoy == baseline in the fake): zero variance, no t statistic
    assert res["contrasts"]["S2_decoy"]["p_two_sided"] is None


def test_analysis_without_scores_is_insufficient_data_everywhere():
    trials = build_trials(make_tasks(45))
    res = analyze({}, trials, cfg())
    assert res["status"] == "INSUFFICIENT_DATA"
    assert all(c["status"] == "INSUFFICIENT_DATA" for c in res["contrasts"].values())


def test_runs_of_different_models_compare_only_when_nothing_else_differs():
    trials = build_trials(make_tasks(45))
    s = fake_scores(trials, 1.0, noise=1.0)
    a = analyze(s, trials, cfg("model-a"), n_perm=200, n_boot=100)
    b = analyze(s, trials, cfg("model-b"), n_perm=200, n_boot=100)
    assert compare_runs(a, b)["differs_in"] == ["model_id"]
    c = analyze(s, trials, cfg("model-c", temperature=0.7), n_perm=200, n_boot=100)
    with pytest.raises(ConfigMismatchError):
        compare_runs(a, c)


def test_coverage_score_is_a_bounded_overlap():
    passage = "memory systems consolidation reorganizes representations"
    assert coverage_score("Memory systems consolidation reorganizes representations over time", passage) == 1.0
    assert coverage_score("Unrelated text entirely", passage) == 0.0
    assert coverage_score("anything", "") == 0.0


# ---------------------------------------------------------------- external orchestrator flow
def test_read_external_run_needs_a_real_run_config(tmp_path):
    write_task_packet(make_tasks(3), tmp_path, prereg_path=PREREG, selection={"rule": "test"})
    with pytest.raises(AblationHarnessError, match="not comparable"):
        read_external_run(tmp_path)
    template = json.loads((tmp_path / "run_config.template.json").read_text(encoding="utf-8"))
    (tmp_path / "run_config.json").write_text(json.dumps(template), encoding="utf-8")
    with pytest.raises(AblationHarnessError, match="placeholder"):
        read_external_run(tmp_path)
    (tmp_path / "run_config.json").write_text(json.dumps({**template, "model_id": "m", "temperature": 0.8}), encoding="utf-8")
    with pytest.raises(ConfigMismatchError):
        read_external_run(tmp_path)
    (tmp_path / "run_config.json").write_text(json.dumps({**template, "model_id": "m"}), encoding="utf-8")
    assert read_external_run(tmp_path)[1] == {}          # no answers yet: nothing invented
    trials = json.loads((tmp_path / "trials.json").read_text(encoding="utf-8"))["trials"]
    (tmp_path / "answers" / Path(trials[0]["answer_path"]).name).write_text("an answer", encoding="utf-8")
    (tmp_path / "answers" / Path(trials[1]["answer_path"]).name).write_text("  ", encoding="utf-8")
    c, answers = read_external_run(tmp_path)
    assert c.model_id == "m" and list(answers) == [trials[0]["trial_id"]]


def test_analyze_cli_reports_insufficient_data_for_an_unrun_packet(tmp_path, capsys):
    spec = importlib.util.spec_from_file_location("analyze_b03_packet", REPO / "30_SCRIPTS" / "evaluation" / "analyze_b03_packet.py")
    cli = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(cli)
    write_task_packet(make_tasks(3), tmp_path, prereg_path=PREREG, selection={"rule": "test"})
    assert cli.main(["--packet", str(tmp_path), "--auto-score"]) == 2
    assert "INSUFFICIENT_DATA" in capsys.readouterr().out
    template = json.loads((tmp_path / "run_config.template.json").read_text(encoding="utf-8"))
    (tmp_path / "run_config.json").write_text(json.dumps({**template, "model_id": "m"}), encoding="utf-8")
    assert cli.main(["--packet", str(tmp_path), "--auto-score"]) == 2          # config present, zero answers
    out = capsys.readouterr().out
    assert '"status": "INSUFFICIENT_DATA"' in out and "mean_diff" not in out


# ---------------------------------------------------------------- the committed packet
@pytest.fixture(scope="module")
def packet():
    return {
        "manifest": json.loads((PACKET / "manifest.json").read_text(encoding="utf-8")),
        "trials": json.loads((PACKET / "trials.json").read_text(encoding="utf-8"))["trials"],
        "tasks": json.loads((PACKET / "scoring" / "tasks.json").read_text(encoding="utf-8")),
    }


def test_committed_packet_has_at_least_40_paired_trials(packet):
    m = packet["manifest"]
    assert m["primary_pairs"] >= 40 and m["tasks"] == len(packet["tasks"]["tasks"])
    by_cond = {c: sum(t["condition"] == c for t in packet["trials"]) for c in CONDITIONS}
    assert set(by_cond.values()) == {m["tasks"]}
    assert m["trials"] == len(packet["trials"]) == m["tasks"] * len(CONDITIONS)


def test_committed_packet_contains_prompts_but_no_answers(packet):
    assert sorted(p.name for p in (PACKET / "answers").iterdir()) == [".gitkeep"]
    for t in packet["trials"]:
        assert set(t) == {"trial_id", "task_id", "condition", "decoy_task_id", "prompt", "prompt_sha256", "answer_path", "order_index"}
        assert t["answer_path"] == f"answers/{t['trial_id']}.txt"
    assert not (PACKET / "run_config.json").exists()


def test_committed_packet_baseline_prompts_are_clean_and_hashes_match(packet):
    tasks = {t["task_id"]: t for t in packet["tasks"]["tasks"]}
    for t in packet["trials"]:
        assert hashlib.sha256(t["prompt"].encode("utf-8")).hexdigest() == t["prompt_sha256"]
        if t["condition"] == COND_WITHOUT:
            task = tasks[t["task_id"]]
            assert task["note_definition"] not in t["prompt"] and task["source_passage"] not in t["prompt"]
            assert task["note_id"] not in t["prompt"]
    m = packet["manifest"]
    assert hashlib.sha256((PACKET / "trials.json").read_bytes()).hexdigest() == m["trials_sha256"]
    assert hashlib.sha256((PACKET / "scoring" / "tasks.json").read_bytes()).hexdigest() == m["tasks_sha256"]
    assert m["template_set_hash"] == template_set_hash() == m["run_config_template"]["prompt_template_hash"]
    assert m["run_config_template"]["model_id"] == PLACEHOLDER_MODEL_ID


def test_committed_packet_is_bound_to_the_preregistration(packet):
    assert hashlib.sha256(PREREG.read_bytes()).hexdigest() == packet["manifest"]["preregistration_sha256"], \
        "the pre-registration changed after the packet was generated; record it under 'Deviations' and regenerate"
    text = PREREG.read_text(encoding="utf-8")
    assert "WRITTEN BEFORE ANY DATA EXISTS" in text and "ce724a575" in text


def test_committed_packet_selection_follows_the_rule(packet):
    sel = packet["tasks"]["selection"]
    assert sel["commit"] == "ce724a575" and sel["selected"] == len(packet["tasks"]["tasks"]) == 51
    for t in packet["tasks"]["tasks"]:
        note, reason = parse_promoted_note((REPO / t["note_path"]).read_text(encoding="utf-8"), t["note_path"])
        assert note is not None, reason
        assert note.note_id == t["note_id"] and note.passage == t["source_passage"]
        assert (REPO / t["note_path"]).name.startswith("Promoted_")


def test_committed_packet_is_reproducible_from_the_notes():
    spec = importlib.util.spec_from_file_location("generate_b03_task_packet", REPO / "30_SCRIPTS" / "evaluation" / "generate_b03_task_packet.py")
    gen = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(gen)
    assert gen.main(["--check", "--out", str(PACKET)]) == 0


def test_blind_rating_generator_accepts_the_committed_packet(tmp_path):
    from lifecycle.validation.book_to_memory_blind_packet import build_blind_packet, items_from_b03_packet
    trials = items_from_b03_packet(PACKET, tmp_path / "no_answers")
    assert all(t["answer"] == "" for t in trials) and len(trials) == 204
    # fill with synthetic text so the generator has something to package
    for t in trials:
        t["answer"] = f"synthetic text for {t['task_id']}"
    manifest = build_blind_packet(trials, tmp_path / "p", tmp_path / "k.json", seed=3)
    assert manifest["items"] == 204
