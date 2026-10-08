"""Real-model with-note / without-note ablation harness (PR #209 finding B03).

``book_to_memory_ablation.py`` checks gate logic on supplied observations. This module is the part that
produces observations from a real model and analyses them, following ``PREREGISTRATION_B03_B05.md``:

* a fixed, pre-registered **task list** built mechanically from the track's notes;
* four **conditions** per task (without note, with note, with note under a reversed framing, with a decoy
  note), each rendered from a fixed prompt template;
* :func:`run_trials`, which takes a *model callable* (a plain function ``prompt -> answer``) and a
  :class:`RunConfig`, executes the trials in the packet order and stores every raw transcript;
* a **task packet** writer, so an external orchestrator can execute the same trials without this code;
* :func:`analyze`, a paired test with a confidence interval, which reports ``INSUFFICIENT_DATA`` and no
  effect when there are too few complete pairs.

The harness never produces an answer or a score by itself. A model callable that returns nothing yields a
failed trial, not a default answer. Tests exercise the mechanics with fake callables and say so; they
are not evidence about any note.
"""
from __future__ import annotations

import hashlib
import json
import random
import re
from dataclasses import asdict, dataclass
from datetime import datetime, timezone
from pathlib import Path
from typing import Any, Callable, Dict, List, Mapping, Optional, Sequence, Tuple

from .book_to_memory_paired_stats import (
    StatsError,
    bootstrap_ci_mean,
    holm_adjust,
    paired_differences,
    paired_t_test,
    sign_flip_permutation_test,
)
from .book_to_memory_run_config import (
    ConfigMismatchError,
    RunConfig,
    RunConfigError,
    assert_comparable,
    prompt_template_hash,
    stamp,
)

PACKET_SCHEMA = "b2m-b03-task-packet/1"
RESULT_SCHEMA = "b2m-b03-result/1"
SEED = 20261008
MIN_PAIRS = 40
PRACTICAL_EFFECT = 1.0  # rubric points
ALPHA_MIN = 0.667       # Krippendorff's tentative reliability threshold
PLACEHOLDER_MODEL_ID = "TO_BE_SET_BY_ORCHESTRATOR"

COND_WITHOUT = "WITHOUT_NOTE"
COND_WITH = "WITH_NOTE"
COND_SKEPTIC = "WITH_NOTE_SKEPTIC"
COND_DECOY = "WITH_DECOY_NOTE"
CONDITIONS = (COND_WITHOUT, COND_WITH, COND_SKEPTIC, COND_DECOY)
#: (condition, baseline): the primary contrast first, then the exploratory ones of the pre-registration.
CONTRASTS = {
    "primary": (COND_WITH, COND_WITHOUT),
    "S1_framing": (COND_SKEPTIC, COND_WITHOUT),
    "S2_decoy": (COND_DECOY, COND_WITHOUT),
    "S3_framing_contrast": (COND_WITH, COND_SKEPTIC),
}

#: The prompt templates, fixed before any data. Placeholders: {question}, {note_text}.
TEMPLATES: Dict[str, str] = {
    COND_WITHOUT: "Question: {question}",
    COND_WITH: "Reference material:\n<<<\n{note_text}\n>>>\n\nQuestion: {question}",
    COND_SKEPTIC: ("Reference material (machine-extracted from a source text and not checked; it may be wrong, "
                   "incomplete or irrelevant):\n<<<\n{note_text}\n>>>\n\nQuestion: {question}"),
    COND_DECOY: "Reference material:\n<<<\n{note_text}\n>>>\n\nQuestion: {question}",
}
QUESTION_TEMPLATE = 'Explain the term "{term}"{source_clause} and the role it plays. Answer in English in 3 to 6 sentences.'

#: Human-readable titles for the source identifiers that have one. Opaque ids get no source clause.
SOURCE_TITLES: Dict[str, str] = {
    "openstax_psychology_2e_ch08": "OpenStax Psychology 2e, chapter 8 (Memory)",
    "minsky_society_of_mind": "Minsky's The Society of Mind",
    "ashby_intro_to_cybernetics": "Ashby's An Introduction to Cybernetics",
    "ashby_design_for_a_brain": "Ashby's Design for a Brain",
    "why_we_forget": "Why We Forget and How to Remember Better",
    "schacter_tulving_memory_systems_1994": "Schacter and Tulving's Memory Systems (1994)",
    "laird_soar_cognitive_architecture": "Laird's The Soar Cognitive Architecture",
    "newell_unified_theories_of_cognition": "Newell's Unified Theories of Cognition",
    "squire_kandel_mind_to_molecules": "Squire and Kandel's Memory: From Mind to Molecules",
    "memory_in_the_age_of_ai_agents": "the survey Memory in the Age of AI Agents",
}


class AblationHarnessError(ValueError):
    """The harness refused an input (bad note, contaminated prompt, bad run record, ...)."""


def template_set_hash() -> str:
    """One hash over all four templates and the question template (the run config's prompt_template_hash)."""
    blob = json.dumps({"templates": TEMPLATES, "question": QUESTION_TEMPLATE}, sort_keys=True)
    return prompt_template_hash(blob)


# ---------------------------------------------------------------------------------------------
# Notes and tasks
# ---------------------------------------------------------------------------------------------
@dataclass(frozen=True)
class TaskNote:
    task_id: str
    note_id: str
    term: str
    source_ref: str
    definition: str
    passage: str
    path: str

    @property
    def source_clause(self) -> str:
        title = SOURCE_TITLES.get(self.source_ref)
        return f" as used in {title}" if title else ""

    @property
    def question(self) -> str:
        return QUESTION_TEMPLATE.format(term=self.term, source_clause=self.source_clause)

    @property
    def note_text(self) -> str:
        """What a model is shown: the term, the definition and the source passage; nothing else of the note."""
        return f'{self.term}\n\nDefinition: {self.definition}\n\nSource passage: "{self.passage}"'


_FM = re.compile(r"\A---\n(.*?)\n---\n", re.S)


def parse_promoted_note(text: str, path: str = "") -> Tuple[Optional[TaskNote], Optional[str]]:
    """Parse a ``Promoted_*`` note. Returns ``(note, None)`` or ``(None, reason)`` when it fails rule 2 of the pre-registration."""
    m = _FM.match(text.replace("\r\n", "\n"))
    if not m:
        return None, "no frontmatter"
    fm, body = m.group(1), text.replace("\r\n", "\n")[m.end():]

    def field(name: str) -> Optional[str]:
        mm = re.search(rf"^\s*{name}:\s*\"?([^\"\n]+?)\"?\s*$", fm, re.M)
        return mm.group(1).strip() if mm else None

    if field("lifecycle") != "REVIEW":
        return None, f"lifecycle is {field('lifecycle')!r}, not REVIEW"
    if field("verification") != "unverified":
        return None, f"verification is {field('verification')!r}, not unverified"
    note_id = field("id")
    if not note_id:
        return None, "no id"
    h = re.search(r"^#\s+(.+?)\s*$", body, re.M)
    if not h:
        return None, "no '# term' heading"
    d = re.search(r"^##\s+Canonical Definition\s*\n+(.+?)(?:\n\s*\n|\Z)", body, re.M | re.S)
    if not d or not d.group(1).strip():
        return None, "no Canonical Definition paragraph"
    q = re.search(r'^>\s*"(.+?)"\s*$', body, re.M)
    if not q:
        return None, "no block-quoted source passage"
    src = field("source_ref") or ""
    return TaskNote("", note_id, h.group(1).strip(), src, " ".join(d.group(1).split()),
                    " ".join(q.group(1).split()), path), None


def build_task_list(notes: Sequence[TaskNote]) -> List[TaskNote]:
    """Number the notes in note-id order (T01, T02, ...). Order is by id so the list is reproducible."""
    ordered = sorted(notes, key=lambda n: n.note_id)
    return [TaskNote(f"T{i:02d}", n.note_id, n.term, n.source_ref, n.definition, n.passage, n.path)
            for i, n in enumerate(ordered, 1)]


def pick_decoy(tasks: Sequence[TaskNote], i: int) -> TaskNote:
    """The next task (cyclic) whose source and term both differ from task ``i``."""
    n = len(tasks)
    for step in range(1, n):
        cand = tasks[(i + step) % n]
        if cand.source_ref != tasks[i].source_ref and cand.term.lower() != tasks[i].term.lower():
            return cand
    for step in range(1, n):  # all notes share one source: fall back to a different term
        cand = tasks[(i + step) % n]
        if cand.term.lower() != tasks[i].term.lower():
            return cand
    raise AblationHarnessError("no decoy note available")


def render_prompt(condition: str, task: TaskNote, decoy: Optional[TaskNote] = None) -> str:
    if condition not in TEMPLATES:
        raise AblationHarnessError(f"unknown condition {condition!r}")
    if condition == COND_WITHOUT:
        return TEMPLATES[condition].format(question=task.question)
    shown = decoy if condition == COND_DECOY else task
    if shown is None:
        raise AblationHarnessError("the decoy condition needs a decoy note")
    return TEMPLATES[condition].format(question=task.question, note_text=shown.note_text)


def assert_without_note_is_clean(prompt: str, task: TaskNote) -> None:
    """The baseline prompt must carry none of the note's definition, passage or id."""
    for label, needle in (("definition", task.definition), ("source passage", task.passage), ("note id", task.note_id)):
        if needle and needle.lower() in prompt.lower():
            raise AblationHarnessError(f"WITHOUT_NOTE prompt of {task.task_id} contains the note's {label}")


def build_trials(tasks: Sequence[TaskNote], seed: int = SEED) -> List[Dict[str, Any]]:
    """All trials (tasks x conditions) in a seeded, condition-interleaved execution order."""
    trials: List[Dict[str, Any]] = []
    for i, t in enumerate(tasks):
        decoy = pick_decoy(tasks, i)
        for cond in CONDITIONS:
            prompt = render_prompt(cond, t, decoy)
            if cond == COND_WITHOUT:
                assert_without_note_is_clean(prompt, t)
            trial_id = f"B03-{t.task_id}-{cond}"
            trials.append({
                "trial_id": trial_id, "task_id": t.task_id, "condition": cond,
                "decoy_task_id": decoy.task_id if cond == COND_DECOY else None,
                "prompt": prompt, "prompt_sha256": hashlib.sha256(prompt.encode("utf-8")).hexdigest(),
                "answer_path": f"answers/{trial_id}.txt",
            })
    random.Random(seed).shuffle(trials)
    for k, tr in enumerate(trials, 1):
        tr["order_index"] = k
    return trials


# ---------------------------------------------------------------------------------------------
# Packet
# ---------------------------------------------------------------------------------------------
PACKET_README = """# B03 task packet (pre-registered; contains prompts only, no answers)

Study: `08_RESEARCH/BOOK_TO_MEMORY/PREREGISTRATION_B03_B05.md`. Nothing in this directory has been run.

## What to do (orchestrator)

1. Read `manifest.json` (`execution_rules`) and `run_config.template.json`.
2. For every entry of `trials.json`, in `order_index` order, send `prompt` verbatim to ONE model, in a fresh context,
   under the run config of the template (temperature, seed, max_tokens are fixed; you set only `model_id`).
3. Write the raw answer, unedited, to `answer_path` (relative to this directory), e.g. `answers/B03-T01-WITH_NOTE.txt`.
   A trial that failed gets an empty file; do not retry it to get a better answer.
4. Save the run config you used, with `model_id` filled in, as `run_config.json` next to `answers/`.
5. Do NOT open `scoring/`: it holds the reference material the raters score against.

A second model is a second run: copy this directory, give it its own `answers/` and `run_config.json`.

## Afterwards

* Build the blind rating packet: `python 30_SCRIPTS/evaluation/b2m_blind_rating_packet.py build --b03-packet <this dir> --out <packet> --key <key.json>`
* Score it with at least two independent raters, then `... score --ratings ... --key <key.json> --out scores.json`
* Analyse: `python 30_SCRIPTS/evaluation/analyze_b03_packet.py --packet <this dir> --scores scores.json`
  With too few complete pairs the analysis prints `INSUFFICIENT_DATA` and reports no effect.

## Files

| File | Content |
|---|---|
| `manifest.json` | counts, hashes, the run-config template, the execution rules |
| `trials.json` | one entry per trial: `trial_id`, `condition`, `prompt`, `answer_path`, `order_index` |
| `run_config.template.json` | the run config every run must use (except `model_id`) |
| `scoring/tasks.json` | per task: question, note id, definition, source passage (for the raters only) |
| `answers/` | empty; the orchestrator writes `<trial_id>.txt` here |
"""


def run_config_template() -> Dict[str, Any]:
    """The run config every run of the packet must use, except that ``model_id`` is the orchestrator's."""
    return {"model_id": PLACEHOLDER_MODEL_ID, "temperature": 0.0, "seed": SEED, "max_tokens": 400,
            "prompt_template_hash": template_set_hash(), "controls": {"study": "B03", "trials_per_run": None}}


def write_task_packet(tasks: Sequence[TaskNote], out_dir: Path, *, prereg_path: Path, selection: Mapping[str, Any],
                      seed: int = SEED, dropped: Sequence[Mapping[str, str]] = ()) -> Dict[str, Any]:
    """Write the self-contained task packet. It contains prompts and answer paths, never an answer."""
    out_dir = Path(out_dir)
    (out_dir / "scoring").mkdir(parents=True, exist_ok=True)
    (out_dir / "answers").mkdir(exist_ok=True)
    trials = build_trials(tasks, seed)
    cfg = run_config_template()
    cfg["controls"]["trials_per_run"] = len(trials)
    trials_doc = {"schema": PACKET_SCHEMA, "trials": sorted(trials, key=lambda t: t["order_index"])}
    (out_dir / "trials.json").write_text(json.dumps(trials_doc, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    (out_dir / "run_config.template.json").write_text(json.dumps(cfg, indent=2) + "\n", encoding="utf-8")
    tasks_doc = {
        "schema": PACKET_SCHEMA,
        "note": "Scoring material. It must NOT be given to the model under test or to its orchestrator's prompts.",
        "selection": dict(selection),
        "dropped": list(dropped),
        "tasks": [{"task_id": t.task_id, "note_id": t.note_id, "note_ids": [t.note_id], "term": t.term,
                   "source_ref": t.source_ref, "question": t.question, "note_definition": t.definition,
                   "source_passage": t.passage, "note_path": t.path} for t in tasks],
    }
    (out_dir / "scoring" / "tasks.json").write_text(json.dumps(tasks_doc, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    (out_dir / "answers" / ".gitkeep").write_text("", encoding="utf-8")
    (out_dir / "README.md").write_text(PACKET_README, encoding="utf-8")
    manifest = {
        "schema": PACKET_SCHEMA,
        "study": "B03/B05 pre-registered with/without-note ablation",
        "preregistration": str(prereg_path).replace("\\", "/"),
        "preregistration_sha256": hashlib.sha256(Path(prereg_path).read_bytes()).hexdigest(),
        "tasks": len(tasks), "trials": len(trials), "conditions": list(CONDITIONS),
        "primary_pairs": len(tasks), "min_pairs_for_analysis": MIN_PAIRS,
        "order_seed": seed, "template_set_hash": template_set_hash(),
        "trials_sha256": hashlib.sha256((out_dir / "trials.json").read_bytes()).hexdigest(),
        "tasks_sha256": hashlib.sha256((out_dir / "scoring" / "tasks.json").read_bytes()).hexdigest(),
        "run_config_template": cfg,
        "execution_rules": [
            "Run every trial in order_index order, each in a fresh context with no memory of other trials.",
            "Send trial.prompt verbatim as the only user message; no system prompt beyond the model's default.",
            "Use the run config of run_config.template.json; set only model_id. Save it as run_config.json next to answers/.",
            "Write the raw answer text, unedited, to trial.answer_path (relative to this directory).",
            "Do not open scoring/. Do not retry a trial to get a better answer; record a failure as an empty file.",
        ],
        "answer_format": "UTF-8 text file per trial at answers/<trial_id>.txt",
    }
    (out_dir / "manifest.json").write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")
    return manifest


# ---------------------------------------------------------------------------------------------
# Running a model callable
# ---------------------------------------------------------------------------------------------
ModelCallable = Callable[[str], str]


def run_trials(trials: Sequence[Mapping[str, Any]], model_fn: ModelCallable, run_config: RunConfig,
               transcript_dir: Path) -> Dict[str, Any]:
    """Execute trials with ``model_fn`` and store every raw transcript.

    The callable sees only the prompt. A trial whose callable raises or returns a non-string/empty value is
    recorded as ``FAILED`` with no answer: nothing is substituted. Returns a stamped run record.
    """
    if run_config.model_id == PLACEHOLDER_MODEL_ID:
        raise AblationHarnessError("run_config.model_id is still the placeholder")
    expected = run_config_template()
    assert_comparable([RunConfig.from_dict(expected), run_config], ["model_id", "controls.trials_per_run"])
    transcript_dir = Path(transcript_dir)
    transcript_dir.mkdir(parents=True, exist_ok=True)
    rows = []
    for tr in sorted(trials, key=lambda t: t["order_index"]):
        rec: Dict[str, Any] = {"trial_id": tr["trial_id"], "task_id": tr["task_id"], "condition": tr["condition"],
                               "prompt": tr["prompt"], "prompt_sha256": tr["prompt_sha256"],
                               "timestamp": datetime.now(timezone.utc).isoformat()}
        try:
            answer = model_fn(tr["prompt"])
            if not isinstance(answer, str) or not answer.strip():
                raise AblationHarnessError("the model callable returned no text")
            rec.update(status="OK", answer=answer, answer_sha256=hashlib.sha256(answer.encode("utf-8")).hexdigest())
        except Exception as exc:  # noqa: BLE001 - any model failure is recorded, never papered over
            rec.update(status="FAILED", answer=None, error=f"{type(exc).__name__}: {exc}")
        stamp(rec, run_config)
        (transcript_dir / f"{tr['trial_id']}.json").write_text(json.dumps(rec, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
        rows.append({k: rec[k] for k in ("trial_id", "status")})
    run = {"schema": RESULT_SCHEMA, "trials": len(rows), "failed": sum(r["status"] != "OK" for r in rows),
           "transcript_dir": str(transcript_dir).replace("\\", "/")}
    return stamp(run, run_config)


def read_external_run(packet_dir: Path, answers_dir: Optional[Path] = None) -> Tuple[RunConfig, Dict[str, str]]:
    """Read the answers an external orchestrator wrote, with its ``run_config.json``.

    Refuses a run whose config is missing, still carries the placeholder model id, or differs from the
    packet's template in anything but ``model_id``. Returns the config and ``trial_id -> answer`` for the
    non-empty answers (empty files are failed trials and are left out).
    """
    packet_dir = Path(packet_dir)
    answers_dir = Path(answers_dir) if answers_dir else packet_dir / "answers"
    cfg_file = answers_dir.parent / "run_config.json" if answers_dir.name == "answers" else answers_dir / "run_config.json"
    if not cfg_file.exists():
        raise AblationHarnessError(f"{cfg_file} is missing: a run without a recorded run config is not comparable")
    cfg = RunConfig.from_dict(json.loads(cfg_file.read_text(encoding="utf-8")))
    if cfg.model_id == PLACEHOLDER_MODEL_ID:
        raise AblationHarnessError("run_config.json still has the placeholder model_id")
    template = RunConfig.from_dict(json.loads((packet_dir / "run_config.template.json").read_text(encoding="utf-8")))
    assert_comparable([template, cfg], ["model_id"])
    trials = json.loads((packet_dir / "trials.json").read_text(encoding="utf-8"))["trials"]
    answers: Dict[str, str] = {}
    for tr in trials:
        f = answers_dir / Path(tr["answer_path"]).name
        if f.exists() and f.read_text(encoding="utf-8").strip():
            answers[tr["trial_id"]] = f.read_text(encoding="utf-8")
    return cfg, answers


# ---------------------------------------------------------------------------------------------
# Automatic exploratory score
# ---------------------------------------------------------------------------------------------
_STOP = frozenset("the and for that with this from are was were have has had not but all any can its into than then "
                  "which their there these those been being also such may more most other some only over under".split())


def _content_words(text: str) -> set:
    return {w for w in re.findall(r"[a-z0-9]+", text.lower()) if len(w) > 3 and w not in _STOP}


def coverage_score(answer: str, passage: str) -> float:
    """Share of the source passage's content words found in the answer (0..1). Exploratory, not the primary metric."""
    ref = _content_words(passage)
    return len(ref & _content_words(answer)) / len(ref) if ref else 0.0


# ---------------------------------------------------------------------------------------------
# Analysis
# ---------------------------------------------------------------------------------------------
def pair_scores(scores: Mapping[str, float], trials: Sequence[Mapping[str, Any]], cond_a: str, cond_b: str
                ) -> Tuple[List[str], List[float], List[float], List[str]]:
    """Complete pairs ``(task ids, scores under a, scores under b)`` and the task ids dropped for a missing score."""
    by_task: Dict[str, Dict[str, str]] = {}
    for t in trials:
        by_task.setdefault(t["task_id"], {})[t["condition"]] = t["trial_id"]
    kept, a, b, dropped = [], [], [], []
    for task_id in sorted(by_task):
        ta, tb = by_task[task_id].get(cond_a), by_task[task_id].get(cond_b)
        if ta in scores and tb in scores and scores[ta] is not None and scores[tb] is not None:
            kept.append(task_id)
            a.append(float(scores[ta]))
            b.append(float(scores[tb]))
        else:
            dropped.append(task_id)
    return kept, a, b, dropped


def analyze_contrast(scores: Mapping[str, float], trials: Sequence[Mapping[str, Any]], cond_a: str, cond_b: str, *,
                     min_pairs: int = MIN_PAIRS, seed: int = SEED, n_perm: int = 100000, n_boot: int = 10000,
                     reliability_alpha: Optional[float] = None) -> Dict[str, Any]:
    """Paired comparison ``cond_a - cond_b``. Below ``min_pairs`` complete pairs it reports INSUFFICIENT_DATA and no effect."""
    if min_pairs < 5:
        raise AblationHarnessError("min_pairs below 5 is not a meaningful analysis")
    tasks, a, b, dropped = pair_scores(scores, trials, cond_a, cond_b)
    res: Dict[str, Any] = {"contrast": f"{cond_a} - {cond_b}", "pairs": len(tasks), "dropped_pairs": len(dropped),
                           "min_pairs": min_pairs, "status": "COMPLETE"}
    if len(tasks) < min_pairs:
        res["status"] = "INSUFFICIENT_DATA"
        res["reason"] = f"{len(tasks)} complete pair(s), {min_pairs} required; no effect is reported"
        return res
    diffs = paired_differences(a, b)
    t = paired_t_test(diffs)
    perm = sign_flip_permutation_test(diffs, seed, n_draws=n_perm)
    lo, hi = bootstrap_ci_mean(diffs, seed, n_boot=n_boot)
    res.update({
        "mean_with_or_a": sum(a) / len(a), "mean_baseline_or_b": sum(b) / len(b),
        "mean_diff": t["mean_diff"], "sd_diff": t["sd_diff"], "t": t["t"], "df": t["df"],
        "p_two_sided": t["p_two_sided"], "ci95": [t["ci_low"], t["ci_high"]], "dz": t["dz"],
        "permutation": perm, "bootstrap_ci95": [lo, hi],
        "wins": sum(d > 0 for d in diffs), "losses": sum(d < 0 for d in diffs), "ties": sum(d == 0 for d in diffs),
    })
    res["verdict"] = decide(res, reliability_alpha)
    return res


def decide(res: Mapping[str, Any], reliability_alpha: Optional[float]) -> str:
    """The pre-registered decision rule (section 7)."""
    p, (lo, hi), md = res["p_two_sided"], res["ci95"], res["mean_diff"]
    if p is None:
        return "INCONCLUSIVE"
    meets = p < 0.05 and lo > 0 and md >= PRACTICAL_EFFECT
    if meets:
        return "SUPPORTED" if (reliability_alpha is not None and reliability_alpha >= ALPHA_MIN) else "UNRELIABLE_RATINGS"
    if p < 0.05 and hi < 0:
        return "NOTE_MADE_ANSWERS_WORSE"
    if lo > -PRACTICAL_EFFECT and hi < PRACTICAL_EFFECT:
        return "NO_EFFECT_OF_PRACTICAL_SIZE"
    return "INCONCLUSIVE"


def analyze(scores: Mapping[str, float], trials: Sequence[Mapping[str, Any]], run_config: RunConfig, *,
            min_pairs: int = MIN_PAIRS, reliability_alpha: Optional[float] = None, seed: int = SEED,
            n_perm: int = 100000, n_boot: int = 10000, preregistration_sha256: str = "") -> Dict[str, Any]:
    """Full pre-registered analysis of one run: the primary contrast plus the three exploratory ones (Holm-adjusted)."""
    out: Dict[str, Any] = {"schema": RESULT_SCHEMA, "preregistration_sha256": preregistration_sha256,
                           "reliability_alpha": reliability_alpha, "contrasts": {}}
    for name, (ca, cb) in CONTRASTS.items():
        out["contrasts"][name] = analyze_contrast(scores, trials, ca, cb, min_pairs=min_pairs, seed=seed,
                                                  n_perm=n_perm, n_boot=n_boot, reliability_alpha=reliability_alpha)
    secondary = [n for n in CONTRASTS if n != "primary" and out["contrasts"][n]["status"] == "COMPLETE"]
    if secondary:
        adj = holm_adjust([out["contrasts"][n]["p_two_sided"] for n in secondary
                           if out["contrasts"][n]["p_two_sided"] is not None])
        for n, p_adj in zip([n for n in secondary if out["contrasts"][n]["p_two_sided"] is not None], adj):
            out["contrasts"][n]["p_holm"] = p_adj
    out["status"] = out["contrasts"]["primary"]["status"]
    return stamp(out, run_config)


def compare_runs(a: Mapping[str, Any], b: Mapping[str, Any]) -> Dict[str, Any]:
    """Two analysed runs may be set side by side only if they differ in ``model_id`` and nothing else."""
    from .book_to_memory_run_config import compare_results
    return compare_results(a, b, ["model_id"])
