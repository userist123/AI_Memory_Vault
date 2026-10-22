"""A/B benchmark of the Claude Code cost-router hooks, run headless in an isolated git worktree.

Conditions (same prompt, same acceptance check, same model flag, same tool allowlist) - EQUAL ARMS (phase 3):
  baseline : `claude -p --setting-sources user,project` + env COST_ROUTER_OFF=1 -> every cost-router hook exits at once
  router   : `claude -p --setting-sources user,project`                          -> cost-router hooks active
Both arms now load CLAUDE.md, the project MCP servers (vault-memory) and the project skills identically; the ONLY
difference is the hook layer (COST_ROUTER_OFF=1 is read first thing by every hook: exit 0, no stdout, no state).
`--legacy-baseline` reproduces the phase 1-2 runs (baseline = `--setting-sources user`, no COST_ROUTER_OFF); those arms
differed beyond hooks (project MCP + 4 skills = ~10.5k prefix tokens only in the router arm).
`--env KEY=VAL` (repeatable) is applied to BOTH arms; `--router-env KEY=VAL` to the router arm only.
The user settings file on the owner's PC has no hooks (verified 2026-10-21). Nothing global is modified.

TOKEN ATTRIBUTION (investigated 2026-10-21 on the 24 privacy-reduced phase2_final streams; numbers in SUMMARY/REPORT):
  * Source of truth for tokens = the assistant events (`usage` per API request, `parent_tool_use_id` null = main session,
    set = a subagent's request). `result.usage` is per MAIN turn and does NOT include subagent requests (T3_router_0:
    sum over result events cache_read 588758 vs modelUsage 636520; T6_router_1 917234 vs 1012856); on runs without
    delegation it equals modelUsage (T1_baseline_0: 95680 = 95680). Never sum result.usage as a run total.
  * `modelUsage` (last, cumulative result event) covers main + all subagents and, for input / cache_read / cache_creation,
    equals the sum of the deduplicated assistant usages EXACTLY on all runs checked (T3_router_0 20/636520/76663, T6_router_1
    28/1012856/94692, T1_baseline_0 4/95680/43314, plus T4_router_0, T5_router_1).
  * DUPLICATES: one API request is emitted as several assistant events (one per content block) carrying the same
    `usage` (T3_router_0: 22 events = 10 requests; T6_router_1: 34 = 14; T1_baseline_0: 2 = 2). They must be deduplicated by
    `message.id` keeping the last usage; the reduced streams written from phase 3 on keep `message.id` and `message.model`.
    Legacy reduced streams (phase 1-2) lack the id: they are deduplicated by (parent, input, cache_read, cache_creation),
    which reproduces modelUsage exactly (every request has a distinct cache_read) but is a heuristic.
  * OUTPUT: `usage.output_tokens` of an assistant event is a streaming snapshot (2 where the request produced hundreds;
    T3_router_0 dedup sum 1247 vs modelUsage 4857), so the per-request output is a LOWER BOUND. `attribute()` therefore adds a
    per-model row role="unattributed_residual" = modelUsage - attributed, so totals equal modelUsage and per-role output
    is explicit about what it cannot split.
AGGREGATE (stated once): total_tokens = SUM over all deduplicated assistant requests (main + subagents) of
  input + cache_read + cache_creation + output   (+ the residual row, which makes the sum equal modelUsage).
uncached_input = usage.input_tokens only. cache_read is NEVER equivalent to input (priced at 10%); it is reported as its own
category and inside total_tokens. token_reduction_percent = 100*(base-router)/base per category and aggregate; cost
reduction % is reported separately: lower cost with higher tokens is not a token success.
COST: authoritative = sum of modelUsage.costUSD (equals the last total_cost_usd); cross-check = policy-card recompute
(lib/transcript_usage.estimate_cost_usd: input, cache_read, 5m/1h writes, output at 04_CONFIG/claude_model_routing.json
prices) over the attribution rows; the run is flagged when the two differ by more than 5%.
ROUTER OVERHEAD: `--include-hook-events` (default on) adds hook events to the stream; the reduced stream keeps only
{type, subtype, hook_event_name, chars} per hook_response (chars of additionalContext + systemMessage, no content);
router_overhead_tokens = sum(chars)/4 (heuristic, same convention as the repo map estimates). UNVERIFIED: the exact
stream shape of hook events in Claude Code 2.1.304 (the parser looks for additionalContext/systemMessage recursively,
also inside JSON strings in stdout/output).

Metrics (per run): total_cost_usd, tool_calls (all tool_use blocks in the stream-json log, subagent blocks included),
delegations (tool_use named Agent/Task), uncached_input (= usage.input_tokens), cached_input (= usage.
cache_read_input_tokens), cache_write (= usage.cache_creation_input_tokens), output_tokens, latency_s (= wall_s, the
harness wall clock around `claude -p`), duration_api_s (sum of duration_api_ms), num_turns, duration_ms.
A delegated router run emits 2+ `result` events (one per main-session turn). Per run: usage fields, num_turns,
duration_ms and duration_api_ms are SUMMED over all `result` events; total_cost_usd is the LAST event's value (it is
cumulative and equals the sum of modelUsage.costUSD; asserted in the tests on the real phase2_final raw files).
The run uses `--output-format stream-json --verbose`. Privacy: the stream is reduced before it is written to
raw/<task>_<cond>_<rep>.stream.jsonl (whitelist: all `result` events minus session_id/uuid/permission_denials;
assistant/user events reduced to type, parent_tool_use_id, tool_use names, usage; system/init reduced to
type, subtype, model, claude_code_version, cwd="<WT>"; everything else dropped). Correctness is a per-task check run
against the worktree after the run (answer regex, pytest exit code, structural greps; T6 additionally runs
`install_design_stack.py --list` and compares it to plan()).

Formulas (per task x condition over n repeats): mean = sum(x)/n; sd = sqrt(sum((x-mean)^2)/(n-1)) (sample, n>=2);
router-vs-baseline cost delta % = 100*(mean_router-mean_base)/mean_base. 95% CI of the difference of means =
(mr-mb) +- t * sqrt(sr^2/nr + sb^2/nb) (Welch standard error; t = t(0.975, df=min(nr,nb)-1) from scipy if present,
else 2.0). "excludes zero" = the CI does not contain 0. With n<2 in an arm no CI exists and no claim is made.
CAVEATS: with n=2 per arm the CI rests on 2 observations per arm (t=12.7 at df=1 with scipy, 2.0 otherwise = too
narrow): NO per-task conclusion may be drawn from it. The only cross-task evidence is the sign test over tasks
(summary prints k tasks where router is cheaper out of m; two-sided exact binomial, p0=0.5), which ignores magnitude.
Savings may only be claimed where the CI excludes zero AND both arms have equal correctness.
Ordering/caching: --order abba (default) alternates the condition order per repeat (baseline,router / router,baseline)
to balance prompt-cache warm-up effects; --order fixed reproduces the old runs. --warmup runs one discarded baseline run
of the first task first (counted in spend, excluded from the statistics). RUN_INFO.json records claude --version, argv,
--router-env, overlay manifest sha256, timestamps and platform.

Usage (from the repository root):
  python 30_SCRIPTS/evaluation/claude_cost_router_benchmark.py --out 07_EVALUATION/reports/<dir> [--repeats 1]
      [--tasks T6,T3] [--conditions baseline,router] [--model sonnet] [--max-turns 15] [--max-budget-usd 1.0]
      [--max-total-usd X] [--overlay] [--env K=V] [--router-env K=V]
      [--legacy-baseline] [--include-hook-events|--no-include-hook-events] [--worktree <path>] [--order abba|fixed] [--warmup] [--dry-run]
  python 30_SCRIPTS/evaluation/claude_cost_router_benchmark.py --recompute <out_dir>   (no claude invocation)
  --conditions-file <json>  arms defined by a file: [{"name","overlay":[repo paths copied from the working tree],"remove":[paths],
      "files":{path:text},"env":{},"setting_sources":"user,project"|null,"extra_args":[]}] (legacy baseline/router keep working);
      every overlaid file is archived to <out>/overlay/<name>/ with MANIFEST.json (sha256) and referenced by RUN_INFO.json.
  --probe "<prompt>"  one single-turn task (max-turns 1, --allowedTools "", correct = non-empty non-error result) for prefix
      measurement; results carry first_request_prefix_tokens (= input + cache_read + cache_creation of the first main request).
  Every claude child gets COST_ROUTER_NO_SELF_INSTALL=1 and the harness refuses cwd == repository root. SUMMARY.md ends with a
  Reconciliation table (sum of deduplicated requests vs modelUsage per run, model and category, with flags).
Tasks run in the order given by --tasks. Per-task max_turns / budget override the global flags (T6: 40 turns, 2.5 USD).
--max-total-usd stops launching new runs once the cumulative total_cost_usd exceeds X (recorded in SUMMARY.md).
All artifacts get the home directory replaced by <HOME> at the end.
Each run resets the worktree to HEAD first (checkout + clean inside the benchmark worktree only).
"""
from __future__ import annotations

import argparse
import json
import math
import os
import re
import shutil
import subprocess
import sys
import time
from dataclasses import dataclass
from pathlib import Path

REPO = Path(__file__).resolve().parents[2]
# The router condition must be able to follow its own policy: delegate (Agent/Task) and run the Vault router
# CLI that the Agent gate requires; otherwise the comparison measures the allowlist, not the router.
TOOLS = ("Read,Grep,Glob,Edit,Write,Agent,Task,Bash(python -m pytest*),Bash(python -m pytest *),"
         "Bash(python -m routing.route_cli*),Bash(PYTHONPATH=03_IMPLEMENTATION/packages python -m routing.route_cli*),"
         "Bash(set PYTHONPATH*),Bash(export PYTHONPATH*)")
DESIGN_STACK = "30_SCRIPTS/setup/install_design_stack.py"
DESIGN_TEST = "20_TESTS/test_install_design_stack.py"


@dataclass(frozen=True)
class Task:
    id: str
    kind: str
    prompt: str
    max_turns: int | None = None      # per-task override of --max-turns
    budget_usd: float | None = None   # per-task override of --max-budget-usd

    def check(self, wt: Path, result_text: str) -> tuple[bool, str]:
        raise NotImplementedError

    def check_ex(self, wt: Path, result_text: str) -> tuple[bool, str, dict]:
        """(correct, why, extra row fields). Default: check() with no extra fields."""
        ok, why = self.check(wt, result_text)
        return ok, why, {}


class ProbeTask(Task):
    """--probe: one single-turn prompt without tools, used to measure the context prefix; correct = non-empty result."""

    def check(self, wt, result_text):
        return (True, "non-empty result") if (result_text or "").strip() else (False, "empty result")


# Admission of unfixed work in the final answer. The bare "rămâne" was dropped (final review): it matched harmless
# text such as "rămâne identică" / "rămâne neschimbat" and produced false negatives on T6.
ADMISSION_RE = re.compile(r"nu am reparat|n-am reparat|nu am rezolvat|not fixed|did not fix|unresolved|"
                          r"rămâne (deschis|nereparat|nerezolvat|de reparat|de rezolvat)|remains (open|unfixed|unresolved)", re.I)


def admits_unfixed(text: str) -> bool:
    """True when the final answer admits an issue was left unfixed (Romanian/English)."""
    return bool(ADMISSION_RE.search(text or ""))


class GrepTask(Task):
    def __init__(self, id, kind, prompt, patterns, max_turns=None, budget_usd=None, min_distinct=None):
        super().__init__(id, kind, prompt, max_turns, budget_usd)
        object.__setattr__(self, "patterns", patterns)
        object.__setattr__(self, "min_distinct", min_distinct)  # (regex with one group, minimum distinct matches)

    def check(self, wt, result_text):
        missing = [p for p in self.patterns if not re.search(p, result_text, re.I)]
        if missing:
            return False, f"missing in answer: {missing}"
        if self.min_distinct:
            rx, n = self.min_distinct
            found = set(re.findall(rx, result_text, re.I))
            if len(found) < n:
                return False, f"only {len(found)} distinct references matching {rx!r}, need {n}"
        return True, "answer contains expected facts"


def _pytest(wt: Path, test_file: str, *extra: str):
    # -B: no .pyc, or a same-size edit within the same second (Linux, 1 s pyc mtime) is graded against stale bytecode
    return subprocess.run([sys.executable, "-B", "-m", "pytest", test_file, "-q", "-p", "no:cacheprovider", *extra],
                          cwd=wt, capture_output=True, text=True, timeout=600)


class PytestTask(Task):
    def __init__(self, id, kind, prompt, test_file, must_contain: dict[str, str], max_turns=None, budget_usd=None,
                 must_absent: dict[str, str] | None = None):
        super().__init__(id, kind, prompt, max_turns, budget_usd)
        object.__setattr__(self, "test_file", test_file)
        object.__setattr__(self, "must_contain", must_contain)
        object.__setattr__(self, "must_absent", must_absent or {})

    def extra_check(self, wt: Path, result_text: str) -> tuple[bool, str]:
        return True, ""

    def check(self, wt, result_text):
        for rel, needle in self.must_contain.items():
            p = wt / rel
            if not p.exists() or needle not in p.read_text(encoding="utf-8", errors="replace"):
                return False, f"{rel} lacks {needle!r}"
        for rel, needle in self.must_absent.items():
            p = wt / rel
            if p.exists() and needle in p.read_text(encoding="utf-8", errors="replace"):
                return False, f"{rel} still contains {needle!r}"
        r = _pytest(wt, self.test_file)
        tail = (r.stdout + r.stderr).strip().splitlines()[-1:] or [""]
        if r.returncode != 0:
            return False, f"pytest exit {r.returncode}: {tail[0]}"
        ok, why = self.extra_check(wt, result_text)
        return ok, f"pytest exit 0: {tail[0]}" + (f"; {why}" if why else "")


class T3Task(PytestTask):
    def extra_check(self, wt, result_text):
        r = _pytest(wt, self.test_file, "-rs")
        if re.search(r"SKIPPED.*mcp_env_pairs", r.stdout + r.stderr):
            return False, "the new mcp_env_pairs test is skipped"
        c = _pytest(wt, self.test_file, "--collect-only")
        if not re.search(r"mcp_env", c.stdout):
            return False, "no collected test id mentions mcp_env"
        return True, "new test collected and not skipped"


def _load_module(path: Path, name: str):
    import importlib.util
    spec = importlib.util.spec_from_file_location(name, path)
    mod = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(mod)
    return mod


def _strings(obj):
    if isinstance(obj, str):
        yield obj
    elif isinstance(obj, dict):
        for k, v in obj.items():
            yield from _strings(k)
            yield from _strings(v)
    elif isinstance(obj, (list, tuple)):
        for v in obj:
            yield from _strings(v)


def _norm_step(name: str) -> str:
    return name.split(" to ")[0] if name.startswith("copy skill") else name


class T6Task(PytestTask):
    """Check order (phase 4): the structural `--list` vs plan() check ALWAYS runs first and is recorded
    (t6_structural_ok), then the admission regex (t6_admission); correct = structural_ok and pytest ok and not admission."""

    def structural(self, wt) -> tuple[bool, str]:
        r = subprocess.run([sys.executable, DESIGN_STACK, "--list"], cwd=wt, capture_output=True, text=True,
                           encoding="utf-8", errors="replace", timeout=120)
        if r.returncode != 0:
            return False, f"--list exit {r.returncode}"
        try:
            data = json.loads(r.stdout)
        except ValueError:
            return False, "--list output is not JSON"
        try:
            mod = _load_module(wt / DESIGN_STACK, "install_design_stack_bench")
            expected = [_norm_step(d) for d, _ in mod.plan(Path.home(), os.name == "nt")]
        except Exception as e:  # noqa: BLE001 - report, do not hide
            return False, f"cannot import plan(): {e!r}"
        got = {_norm_step(x) for x in _strings(data)}
        missing = [e for e in expected if e not in got]
        if missing:
            return False, f"--list lacks plan steps: {missing[:3]}"
        return True, f"--list JSON matches {len(expected)} plan steps"

    def extra_check(self, wt, result_text):  # structural + admission are evaluated by check_ex, in the stated order
        return True, ""

    def check_ex(self, wt, result_text):
        s_ok, s_why = self.structural(wt)            # 1. structural, always, recorded
        admission = admits_unfixed(result_text)      # 2. admission regex, recorded
        p_ok, p_why = PytestTask.check(self, wt, result_text)   # 3. file markers + pytest
        ok = s_ok and p_ok and not admission
        why = "; ".join(x for x in (f"structural: {s_why}", p_why,
                                    "final answer admits unfixed issues" if admission else "") if x)
        return ok, why, {"t6_structural_ok": s_ok, "t6_admission": admission}

    def check(self, wt, result_text):
        ok, why, _ = self.check_ex(wt, result_text)
        return ok, why


TASKS: list[Task] = [
    GrepTask("T1", "explore",
             "Which file defines the constant FREE_MAIN_CALLS and what is its value? Answer in one line as "
             "'<path>:<line> value=<n>'. Do not edit anything.",
             [r"route_state\.py", r"value\s*=\s*3(?!\d)"]),
    # T2 v2 (2026-10-21): v1 referenced PLUGINS_DIRECTORY / @anthropic-plugin-directory, which exist only in the
    # uncommitted working tree, not at HEAD (premise invalid). v2 uses content present at HEAD
    # (git show HEAD:20_TESTS/test_install_design_stack.py | grep -n 'chrome-devtools-mcp@1.9.0' -> lines 19 and 30).
    GrepTask("T2", "diagnose",
             f"Which test in {DESIGN_TEST} would fail if `chrome-devtools-mcp@1.9.0` were replaced by an unpinned "
             f"`chrome-devtools-mcp` in {DESIGN_STACK}? Name the test function and the assertion. "
             "Do not edit anything.",
             [r"test_plan_installs_only_what_the_review_cleared|test_chrome_devtools_runs_pinned_without_telemetry_and_isolated",
              r"chrome-devtools-mcp@1\.9\.0"]),
    T3Task("T3", "small-change",
           f"In {DESIGN_STACK} add a function `mcp_env_pairs() -> list[tuple[str, str]]` that parses MCP_ENV "
           "entries of the form KEY=VALUE into (key, value) tuples. Add one test for it in "
           f"{DESIGN_TEST} and run that test file with `python -m pytest {DESIGN_TEST} -q`. Report the result.",
           DESIGN_TEST, {DESIGN_STACK: "def mcp_env_pairs", DESIGN_TEST: "mcp_env_pairs"}),
    PytestTask("T4", "multi-file",
               f"Rename the constant MARKER_NAME to MARKER_FILENAME in {DESIGN_STACK} and update every reference "
               f"to it in that file and in {DESIGN_TEST} (and any other file that references it). Then run "
               f"`python -m pytest {DESIGN_TEST} -q` and report the result.",
               DESIGN_TEST, {DESIGN_STACK: "MARKER_FILENAME"},
               must_absent={DESIGN_STACK: "MARKER_NAME", DESIGN_TEST: "MARKER_NAME"}),
    GrepTask("T5", "review",
             f"Adversarially review {DESIGN_STACK} for correctness and safety bugs (subprocess use, path handling, "
             "idempotency marker, Windows behaviour). List at most 5 findings as 'file:line severity: text'. "
             "Do not edit anything.",
             [r"install_design_stack\.py:\d+"], min_distinct=(r"install_design_stack\.py:(\d+)", 2)),
    T6Task("T6", "long-multistep",
           f"Plan and implement, in `{DESIGN_STACK}`, a `--list` flag that prints the plan steps as JSON "
           f"(reuse plan()); add tests in `{DESIGN_TEST}` for the JSON shape and for `--dry-run` unchanged; "
           "then review your own diff for Windows path issues and fix any; run "
           f"`python -m pytest {DESIGN_TEST} -q` and report. Work in stages: explore, implement, test, review.",
           DESIGN_TEST, {DESIGN_STACK: "--list", DESIGN_TEST: "list"}, max_turns=40, budget_usd=2.5),
]

SOURCES = {"baseline": "user,project", "router": "user,project"}   # equal arms (phase 3)
LEGACY_SOURCES = {"baseline": "user", "router": "user,project"}     # --legacy-baseline: phase 1-2 runs
OFF_ENV = "COST_ROUTER_OFF"


def parse_kv(items, flag="--env"):
    out = {}
    for kv in items or []:
        if "=" not in kv:
            raise ValueError(f"{flag} expects KEY=VAL, got {kv!r}")
        k, v = kv.split("=", 1)
        out[k] = v
    return out


NO_SELF_INSTALL_ENV = "COST_ROUTER_NO_SELF_INSTALL"   # install.py --session-start returns at once when "1" (phase 4)
COND_KEYS = {"name", "overlay", "remove", "files", "env", "setting_sources", "extra_args"}


def _safe_rel(rel) -> str:
    """Repo-relative path guard: no absolute path, drive (`D:x`), `:`, `..`, empty/`.` or `.git` first component."""
    from pathlib import PureWindowsPath, PurePosixPath
    bad = f"path must be a plain repo-relative path (no drive, ':', '..', '.git', empty): {rel!r}"
    if not isinstance(rel, str) or not rel.strip() or ":" in rel or rel[0] in "/\\":
        raise ValueError(bad)
    if PureWindowsPath(rel).drive or PureWindowsPath(rel).is_absolute() or PurePosixPath(rel).is_absolute():
        raise ValueError(bad)
    parts = [p for p in rel.replace("\\", "/").split("/") if p not in ("", ".")]
    if not parts or ".." in parts or parts[0].lower() == ".git":
        raise ValueError(bad)
    return rel


def _inside(worktree: Path, rel: str) -> Path:
    """Apply-time check: (worktree / rel) must resolve inside the worktree, else abort."""
    rel = _safe_rel(rel)
    root = worktree.resolve()
    target = (worktree / rel).resolve()
    try:
        target.relative_to(root)
    except ValueError:
        raise SystemExit(f"refusing path outside the worktree: {rel!r} -> {target}")
    if target == root:
        raise SystemExit(f"refusing path equal to the worktree root: {rel!r}")
    return worktree / rel


def load_conditions(path: Path) -> dict[str, dict]:
    """--conditions-file: JSON list of {"name", "overlay": [repo-relative paths copied from the working tree into the
    worktree after the reset], "remove": [paths deleted from the worktree], "files": {path: text written into the
    worktree}, "env": {..}, "setting_sources": "user,project" (null/"" = omit the flag), "extra_args": [..]}."""
    data = json.loads(Path(path).read_text(encoding="utf-8"))
    if not isinstance(data, list) or not data:
        raise ValueError("conditions file must be a non-empty JSON list")
    out: dict[str, dict] = {}
    for c in data:
        if not isinstance(c, dict) or not isinstance(c.get("name"), str) or not c["name"]:
            raise ValueError(f"every condition needs a string name: {c!r}")
        bad = set(c) - COND_KEYS
        if bad:
            raise ValueError(f"condition {c['name']!r}: unknown keys {sorted(bad)}")
        if c["name"] in out:
            raise ValueError(f"duplicate condition name {c['name']!r}")
        spec = {"setting_sources": c.get("setting_sources", "user,project"),
                "overlay": [_safe_rel(x) for x in c.get("overlay", [])],
                "remove": [_safe_rel(x) for x in c.get("remove", [])],
                "files": {_safe_rel(k): str(v) for k, v in (c.get("files") or {}).items()},
                "env": {str(k): str(v) for k, v in (c.get("env") or {}).items()},
                "extra_args": [str(x) for x in c.get("extra_args", [])]}
        out[c["name"]] = spec
    return out


def condition_extra_args(condition: str, a) -> list[str]:
    return list(((getattr(a, "cond_specs", None) or {}).get(condition) or {}).get("extra_args", []))


def condition_setup(condition: str, a, base_env=None) -> tuple[str | None, dict]:
    """(--setting-sources value, child env) of one arm. Equal arms: both load user,project; the baseline differs only by
    COST_ROUTER_OFF=1 (hooks inert). --legacy-baseline: baseline = `user` sources, no OFF switch. --env goes to both arms,
    --router-env to the router arm; COST_ROUTER_OFF may not be set by the caller (it defines the arm)."""
    legacy = bool(getattr(a, "legacy_baseline", False))
    env = {k: v for k, v in (os.environ if base_env is None else base_env).items()
           if k not in ("CLAUDECODE", "CLAUDE_CODE_CHILD_SESSION", OFF_ENV)}
    both, router_only = parse_kv(getattr(a, "env", None), "--env"), parse_kv(getattr(a, "router_env", None), "--router-env")
    if OFF_ENV in both or OFF_ENV in router_only:
        raise ValueError(f"{OFF_ENV} is set by the harness per condition; do not pass it via --env/--router-env")
    env.update(both)
    specs = getattr(a, "cond_specs", None) or {}
    if condition in specs:  # --conditions-file arm: its own env (it may set COST_ROUTER_OFF) and sources
        env.update(specs[condition]["env"])
        env[NO_SELF_INSTALL_ENV] = "1"
        return specs[condition]["setting_sources"] or None, env
    if condition == "router":
        env.update(router_only)
    elif not legacy:
        env[OFF_ENV] = "1"
    env[NO_SELF_INSTALL_ENV] = "1"  # every claude child, both arms: no SessionStart self-update from the worktree
    return (LEGACY_SOURCES if legacy else SOURCES)[condition], env


def sh(cmd, cwd, timeout=900, env=None):
    exe = shutil.which(cmd[0]) or cmd[0]
    return subprocess.run([exe, *cmd[1:]], cwd=cwd, capture_output=True, text=True, encoding="utf-8",
                          errors="replace", timeout=timeout, env=env, stdin=subprocess.DEVNULL)


def ensure_worktree(path: Path) -> str:
    if not (path / ".git").exists():
        r = sh(["git", "worktree", "add", "--detach", str(path), "HEAD"], REPO)
        if r.returncode != 0:
            raise SystemExit(f"git worktree add failed: {r.stderr}")
    head = sh(["git", "rev-parse", "--short", "HEAD"], path).stdout.strip()
    return head


def guard_not_repo_root(cwd) -> None:
    """Hard guard: a paid `claude` child must never run with cwd = the repository root (its SessionStart would rewrite the
    owner's global skill copy and it would read the live working tree)."""
    if Path(cwd).resolve() == REPO.resolve():
        raise SystemExit(f"refusing to run claude with cwd == repository root ({cwd}); use the bench worktree")


def _copy_in(src: Path, dst: Path) -> None:
    if src.is_dir():
        shutil.rmtree(dst, ignore_errors=True)
        shutil.copytree(src, dst, ignore=shutil.ignore_patterns("__pycache__"))
    elif src.exists():
        dst.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(src, dst)


def _expand(root: Path, rel: str) -> list[str]:
    p = root / rel
    if p.is_dir():
        return sorted(f.relative_to(root).as_posix() for f in p.rglob("*") if f.is_file() and "__pycache__" not in f.parts)
    return [rel] if p.exists() else []


def reset_worktree(path: Path, overlay: bool = False, spec: dict | None = None) -> dict:
    """Reset to HEAD, then apply the legacy --overlay (working-tree hooks) and/or a --conditions-file spec
    (overlay copies from the working tree, remove, files). Returns {"overlaid": [files], "removed": [..], "generated": [..]}."""
    for cmd in (["git", "checkout", "--", "."], ["git", "clean", "-fdq", "--exclude=.claude/cache"]):
        r = sh(cmd, path)
        if r.returncode != 0:
            raise SystemExit(f"reset_worktree: `{' '.join(cmd)}` failed rc={r.returncode} in {path}: "
                             f"{(r.stderr or r.stdout or '').strip()[:300]} (stale worktree, aborting)")
    strip_self_update(path / ".claude" / "settings.json")
    rels = [".claude/settings.json", ".claude/skills/cost-router"] if overlay else []
    rels += [r for r in (spec or {}).get("overlay", []) if r not in rels]
    for rel in rels:  # benchmark the working-tree files (uncommitted) instead of HEAD's
        _copy_in(REPO / _safe_rel(rel), _inside(path, rel))
    removed = []
    for rel in (spec or {}).get("remove", []):
        t = _inside(path, rel)
        if t.is_dir():
            shutil.rmtree(t, ignore_errors=True)
        elif t.exists():
            t.unlink()
        removed.append(rel)
    generated = []
    for rel, text in ((spec or {}).get("files") or {}).items():
        t = _inside(path, rel)
        t.parent.mkdir(parents=True, exist_ok=True)
        t.write_text(text, encoding="utf-8")
        generated.append(rel)
    strip_self_update(path / ".claude" / "settings.json")
    return {"overlaid": [f for rel in rels for f in _expand(path, rel)], "removed": removed, "generated": generated}


def archive_overlay(out: Path, cond: str, wt: Path, info: dict) -> str | None:
    """Copy every overlaid/generated file (as applied in the worktree, i.e. after strip_self_update) to
    <out>/overlay/<cond>/... with MANIFEST.json {files: {rel: sha256}, removed: [...]}. Returns the manifest path (relative)."""
    import hashlib
    files = list(info.get("overlaid", [])) + [g for g in info.get("generated", []) if g not in info.get("overlaid", [])]
    if not files and not info.get("removed"):
        return None
    dest = out / "overlay" / cond
    dest.mkdir(parents=True, exist_ok=True)
    manifest = {"files": {}, "removed": list(info.get("removed", []))}
    for rel in files:
        src = wt / rel
        if not src.is_file():
            continue
        (dest / rel).parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(src, dest / rel)
        manifest["files"][rel] = hashlib.sha256(src.read_bytes()).hexdigest()
    (dest / "MANIFEST.json").write_text(json.dumps(manifest, indent=1, sort_keys=True), encoding="utf-8")
    return f"overlay/{cond}/MANIFEST.json"


def strip_self_update(settings: Path) -> None:
    """Drop the `install.py --session-start` SessionStart entry: it is self-update, not routing, and it would
    rewrite the owner's ~/.claude/skills/cost-router copy from the benchmark worktree."""
    try:
        data = json.loads(settings.read_text(encoding="utf-8"))
        for event, groups in list((data.get("hooks") or {}).items()):
            data["hooks"][event] = [g for g in groups
                                    if not any("install.py" in h.get("command", "") for h in g.get("hooks", []))]
        settings.write_text(json.dumps(data, indent=2) + "\n", encoding="utf-8")
    except (OSError, ValueError):
        pass


DROP_RESULT_KEYS = ("session_id", "uuid", "permission_denials")


def hook_context_chars(ev) -> int:
    """Characters of hook-injected context (additionalContext + systemMessage) in a hook event; content is never kept.
    Idempotent: an already reduced event carries `chars`."""
    if isinstance(ev.get("chars"), int) and "hook_event_name" in ev and "stdout" not in ev:
        return ev["chars"]
    return _hook_key_chars(ev, ("additionalContext", "systemMessage"))


def hook_reason_chars(ev) -> int:
    """Length (chars only, never the text) of permissionDecisionReason in a PreToolUse hook response; 0 when absent.
    Idempotent: an already reduced event carries `reason_chars`."""
    if isinstance(ev.get("reason_chars"), int) and "hook_event_name" in ev and "stdout" not in ev:
        return ev["reason_chars"]
    return _hook_key_chars(ev, ("permissionDecisionReason",))


def _hook_key_chars(ev, keys) -> int:
    total = 0

    def walk(o, depth=0):
        nonlocal total
        if depth > 6:
            return
        if isinstance(o, dict):
            for k, v in o.items():
                if k in keys and isinstance(v, str):
                    total += len(v)
                else:
                    walk(v, depth + 1)
        elif isinstance(o, list):
            for v in o:
                walk(v, depth + 1)
        elif isinstance(o, str) and o.lstrip().startswith("{"):
            try:
                walk(json.loads(o), depth + 1)
            except ValueError:
                pass
    # Claude Code 2.1.304 hook_response carries the same payload in `output` AND `stdout`: count it once.
    if isinstance(ev.get("stdout"), str) and ev.get("stdout"):
        walk(ev["stdout"])
    elif isinstance(ev.get("output"), str) and ev.get("output"):
        walk(ev["output"])
    else:
        walk(ev)
    return total


def reduce_event(ev, keep_hooks: bool = False) -> dict | None:
    """Privacy whitelist for one stream-json event; None = drop it (see module docstring)."""
    if not isinstance(ev, dict):
        return None
    t = ev.get("type")
    if t == "result":
        out = {k: v for k, v in ev.items() if k not in DROP_RESULT_KEYS}
        if "permission_denials" in ev:
            out["permission_denials_n"] = len(ev["permission_denials"] or [])
        return out
    if t == "system":
        if keep_hooks and str(ev.get("subtype", "")).startswith("hook"):
            red = {"type": "system", "subtype": ev.get("subtype"),
                   "hook_event_name": ev.get("hook_event_name") or ev.get("hook_event"),
                   "chars": hook_context_chars(ev)}
            if hook_reason_chars(ev):  # PreToolUse deny/allow reason: its length only
                red["reason_chars"] = hook_reason_chars(ev)
            return red
        if ev.get("subtype") != "init":
            return None
        out = {"type": "system", "subtype": "init", "model": ev.get("model"), "cwd": "<WT>"}
        if "claude_code_version" in ev:
            out["claude_code_version"] = ev["claude_code_version"]
        return out
    if t in ("assistant", "user"):
        msg = ev.get("message") if isinstance(ev.get("message"), dict) else {}
        content = msg.get("content")
        blocks = []
        for b in content if isinstance(content, list) else []:
            if isinstance(b, dict) and b.get("type") == "tool_use":
                blk = {"type": "tool_use", "name": b.get("name")}
                if isinstance(b.get("id"), str):
                    blk["id"] = b["id"]
                inp = b.get("input") if isinstance(b.get("input"), dict) else {}
                st = b.get("subagent_type") or inp.get("subagent_type")
                if isinstance(st, str) and b.get("name") in ("Agent", "Task"):
                    blk["subagent_type"] = st
                blocks.append(blk)
        m = {"content": blocks}
        if isinstance(msg.get("usage"), dict):
            m["usage"] = msg["usage"]
        for k in ("id", "model"):  # opaque request id / model alias: needed for dedup + per-model attribution
            if t == "assistant" and isinstance(msg.get(k), str):
                m[k] = msg[k]
        return {"type": t, "parent_tool_use_id": ev.get("parent_tool_use_id"), "message": m}
    return None


def reduce_stream(text: str, keep_hooks: bool = False) -> str:
    """Reduce a raw stream-json log to the whitelist; unparsable lines are dropped. Idempotent.
    keep_hooks=True keeps hook events reduced to {type, subtype, hook_event_name, chars} (no content)."""
    out = []
    for line in text.splitlines():
        try:
            ev = json.loads(line)
        except ValueError:
            continue
        r = reduce_event(ev, keep_hooks)
        if r is not None:
            out.append(json.dumps(r, ensure_ascii=False))
    return "\n".join(out) + ("\n" if out else "")


SUM_USAGE_KEYS = ("input_tokens", "cache_creation_input_tokens", "cache_read_input_tokens", "output_tokens")


def parse_stream(text: str) -> tuple[dict, int, int]:
    """Return (aggregated result, tool_use count, Agent/Task tool_use count) from a stream-json log.

    A delegated run has 2+ `result` events (main-session turns). The aggregate is the LAST result event with
    usage fields / num_turns / duration_ms / duration_api_ms summed over all of them; total_cost_usd and
    modelUsage stay the last (cumulative) values. `n_results` records how many result events were seen."""
    results, tools, deleg, seen_ids = [], 0, 0, set()
    for line in text.splitlines():
        try:
            ev = json.loads(line)
        except ValueError:
            continue
        if not isinstance(ev, dict):
            continue
        if ev.get("type") == "result":
            results.append(ev)
        elif ev.get("type") == "assistant":
            content = (ev.get("message") or {}).get("content")
            for blk in content if isinstance(content, list) else []:
                if isinstance(blk, dict) and blk.get("type") == "tool_use":
                    if isinstance(blk.get("id"), str):  # a block repeated across events of one request counts once
                        if blk["id"] in seen_ids:
                            continue
                        seen_ids.add(blk["id"])
                    tools += 1
                    deleg += blk.get("name") in ("Agent", "Task")
    if not results:
        return {}, tools, deleg
    agg = dict(results[-1])
    usage = dict(agg.get("usage") or {})
    for k in SUM_USAGE_KEYS:
        vals = [(r.get("usage") or {}).get(k) for r in results]
        vals = [v for v in vals if isinstance(v, (int, float))]
        if vals:
            usage[k] = sum(vals)
    agg["usage"] = usage
    for k in ("num_turns", "duration_ms", "duration_api_ms"):
        vals = [r.get(k) for r in results if isinstance(r.get(k), (int, float))]
        if vals:
            agg[k] = sum(vals)
    agg["n_results"] = len(results)
    return agg, tools, deleg



# ---------------------------------------------------------------------------------------------- attribution
CATS = ("input_tokens", "cache_read_input_tokens", "cache_creation_input_tokens", "output_tokens")
MU_KEYS = dict(zip(CATS, ("inputTokens", "cacheReadInputTokens", "cacheCreationInputTokens", "outputTokens")))
RESIDUAL = "unattributed_residual"
COST_FLAG_PCT = 5.0


def _events(stream) -> list[dict]:
    if isinstance(stream, str):
        out = []
        for line in stream.splitlines():
            try:
                ev = json.loads(line)
            except ValueError:
                continue
            if isinstance(ev, dict):
                out.append(ev)
        return out
    return [e for e in stream if isinstance(e, dict)]


def _has(x) -> bool:
    return isinstance(x, (int, float)) and not isinstance(x, bool)


def _num(x) -> int:
    """Known number -> int. Used for SUMS only: a missing field adds 0 to a sum, which makes the sum a LOWER BOUND; callers
    that report a total must also carry `_has()`-based completeness (usage_complete / missing_fields), never 0 = unknown."""
    return int(x) if _has(x) else 0


def _last_model_usage(events) -> dict:
    mu = {}
    for e in events:
        if e.get("type") == "result" and isinstance(e.get("modelUsage"), dict):
            mu = e["modelUsage"]
    return mu


def dedup_requests(events) -> list[dict]:
    """One dict per API request {parent, model, usage}: assistant events deduplicated by message.id (last usage wins,
    output = max seen); events without an id (legacy reduced streams) by (parent, input, cache_read, cache_creation)."""
    order, by = [], {}
    for e in events:
        if e.get("type") != "assistant":
            continue
        msg = e.get("message") if isinstance(e.get("message"), dict) else {}
        u = msg.get("usage")
        if not isinstance(u, dict):
            if not (isinstance(msg.get("id"), str) or isinstance(msg.get("model"), str)):
                continue  # not an identifiable API request
            u = {}  # a real request whose usage is absent: kept so its missing fields are reported, not silently dropped
        parent = e.get("parent_tool_use_id")
        key = ("id", msg["id"]) if isinstance(msg.get("id"), str) else (
            "legacy", parent, _num(u.get("input_tokens")), _num(u.get("cache_read_input_tokens")),
            _num(u.get("cache_creation_input_tokens")))
        prev = by.get(key)
        if prev is None:
            order.append(key)
        prev_u = (prev or {}).get("usage") or {}
        usage = {**prev_u, **u}  # last value wins per field; a field absent from the last event keeps the earlier one
        if _has(u.get("output_tokens")) or _has(prev_u.get("output_tokens")):
            usage["output_tokens"] = max(_num(u.get("output_tokens")), _num(prev_u.get("output_tokens")))
        by[key] = {"parent": parent, "model": msg.get("model"), "usage": usage}
    return [by[k] for k in order]


def _agent_types(events) -> dict:
    """tool_use id -> subagent_type for Agent/Task blocks (reduced streams keep both)."""
    out = {}
    for e in events:
        if e.get("type") != "assistant":
            continue
        content = (e.get("message") or {}).get("content")
        for b in content if isinstance(content, list) else []:
            if isinstance(b, dict) and b.get("type") == "tool_use" and b.get("name") in ("Agent", "Task") and b.get("id"):
                inp = b.get("input") if isinstance(b.get("input"), dict) else {}
                out[b["id"]] = b.get("subagent_type") or inp.get("subagent_type") or None
    return out


def attribute(stream_events, model_usage=None) -> list[dict]:
    """Per (role, model) rows from the deduplicated assistant requests of one run.
    role = "main" or "subagent:<agentType | parent tool_use_id>". Fields: input_tokens, cache_read_input_tokens,
    cache_creation_input_tokens, cache_creation_5m, cache_creation_1h (from usage.cache_creation.ephemeral_*; 0 when
    absent), output_tokens (streaming snapshot = lower bound, see header), requests, usage_complete / missing_fields (a
    core category absent from a request's usage is UNKNOWN, not 0: the row sums are then lower bounds). When modelUsage is available
    (given, or the last result event) a per-model row role="unattributed_residual" carries modelUsage minus the
    attributed sums for every category with a non-zero gap, so that total_tokens equals modelUsage."""
    events = _events(stream_events)
    mu = model_usage if model_usage is not None else _last_model_usage(events)
    types = _agent_types(events)
    sole = next(iter(mu)) if len(mu) == 1 else None
    rows: dict[tuple, dict] = {}
    for req in dedup_requests(events):
        parent = req["parent"]
        role = "main" if not parent else f"subagent:{types.get(parent) or parent}"
        model = req["model"] or sole or "unknown"
        r = rows.setdefault((role, model), {"role": role, "model": model, **{c: 0 for c in CATS},
                                            "cache_creation_5m": 0, "cache_creation_1h": 0, "requests": 0,
                                            "usage_complete": True, "missing_fields": []})
        u = req["usage"]
        for c in CATS:
            r[c] += _num(u.get(c))
            if not _has(u.get(c)) and c not in r["missing_fields"]:
                r["usage_complete"] = False
                r["missing_fields"].append(c)
        cc = u.get("cache_creation") if isinstance(u.get("cache_creation"), dict) else {}
        r["cache_creation_5m"] += _num(cc.get("ephemeral_5m_input_tokens"))
        r["cache_creation_1h"] += _num(cc.get("ephemeral_1h_input_tokens"))
        r["requests"] += 1
    out = sorted(rows.values(), key=lambda r: (r["role"] != "main", r["role"], r["model"]))
    for model, v in sorted(mu.items()):
        if not isinstance(v, dict):
            continue
        gap = {c: _num(v.get(MU_KEYS[c])) - sum(r[c] for r in out if r["model"] == model) for c in CATS}
        if any(gap.values()):
            out.append({"role": RESIDUAL, "model": model, **gap, "cache_creation_5m": 0, "cache_creation_1h": 0,
                        "requests": 0, "usage_complete": True, "missing_fields": []})
    return out


def total_tokens(rows) -> int:
    """total_tokens = SUM over rows (all deduplicated requests, main + subagents, + residual) of
    input + cache_read + cache_creation + output."""
    return sum(r[c] for r in rows for c in CATS)


def category_totals(rows) -> dict:
    t = {c: sum(r[c] for r in rows) for c in CATS}
    return {"tok_input": t["input_tokens"], "tok_cache_read": t["cache_read_input_tokens"],
            "tok_cache_creation": t["cache_creation_input_tokens"], "tok_output": t["output_tokens"],
            "total_tokens": total_tokens(rows), "uncached_input_total": t["input_tokens"]}


_COST_MOD: list = []


def _policy():
    """(estimate_cost_usd, policy card) of the repo's cost-router, or (None, None)."""
    if not _COST_MOD:
        try:
            import importlib.util
            p = REPO / ".claude" / "skills" / "cost-router" / "lib" / "transcript_usage.py"
            spec = importlib.util.spec_from_file_location("transcript_usage_bench", p)
            mod = importlib.util.module_from_spec(spec)
            spec.loader.exec_module(mod)
            card = json.loads((REPO / "04_CONFIG" / "claude_model_routing.json").read_text(encoding="utf-8"))
            _COST_MOD.append((mod.estimate_cost_usd, card))
        except Exception:  # noqa: BLE001 - cross-check is optional, never invent a price
            _COST_MOD.append((None, None))
    return _COST_MOD[0]


def policy_cost(rows) -> float | None:
    """Recomputed cost of the attribution rows at the policy card prices (None if any model is unpriced)."""
    est, card = _policy()
    if est is None:
        return None
    total = 0.0
    for r in rows:
        rest = r["cache_creation_input_tokens"] - r["cache_creation_5m"] - r["cache_creation_1h"]
        usage = {"input_tokens": r["input_tokens"], "cache_read_input_tokens": r["cache_read_input_tokens"],
                 "cache_creation_input_tokens": r["cache_creation_input_tokens"], "output_tokens": r["output_tokens"]}
        if r["cache_creation_5m"] or r["cache_creation_1h"]:
            usage["cache_creation_5m_input_tokens"] = r["cache_creation_5m"] + max(rest, 0)  # unsplit remainder = 5m
            usage["cache_creation_1h_input_tokens"] = r["cache_creation_1h"]
        c = est(r["model"], usage, card)
        if c is None:
            return None
        total += c
    return total


def authoritative_cost(events) -> float | None:
    mu = _last_model_usage(events)
    vals = [v.get("costUSD") for v in mu.values() if isinstance(v, dict) and isinstance(v.get("costUSD"), (int, float))]
    if vals:
        return sum(vals)
    last = [e.get("total_cost_usd") for e in events if e.get("type") == "result"]
    return last[-1] if last and isinstance(last[-1], (int, float)) else None


def hook_overhead(events) -> tuple[int | None, int]:
    """(router_overhead_tokens = sum(chars)/4 over hook events, number of hook events); (None, 0) if no hook events."""
    chars = [e.get("chars") for e in events if e.get("type") == "system" and str(e.get("subtype", "")).startswith("hook")
             and isinstance(e.get("chars"), int)]
    return (round(sum(chars) / 4) if chars else None), len(chars)


def attribution_fields(stream) -> dict:
    """results.jsonl fields derived from the stream: category totals, per-role/model rows, authoritative and recomputed
    cost (flagged above 5% difference), requests, router overhead. Empty dict when the stream has no usable usage."""
    events = _events(stream)
    rows = attribute(events)
    if not rows:
        return {}
    auth, pol = authoritative_cost(events), policy_cost(rows)
    diff = round(100 * (pol - auth) / auth, 2) if auth and pol is not None else None
    overhead, n_hook = hook_overhead(events)
    mu = _last_model_usage(events)
    flags = {}
    if any(r["role"] == RESIDUAL and any(r[c] < 0 for c in CATS) for r in rows):
        flags["residual_negative"] = True  # attributed tokens exceed modelUsage: dedup/attribution double counts
    complete = all(r.get("usage_complete", True) for r in rows)
    flags["usage_complete"] = complete  # False: a core category was absent from some request -> sums are lower bounds
    flags["missing_fields"] = [c for c in CATS if any(c in r.get("missing_fields", []) for r in rows)]
    flags["tokens_complete"] = complete
    if not complete:
        flags["total_tokens_lower_bound"] = True
    gap_fields = [c for c in CATS[:3] if any(r["role"] == RESIDUAL and r[c] != 0 for r in rows)]
    if gap_fields:  # input / cache_read / cache_creation must match modelUsage exactly (output is a streaming lower bound)
        flags["usage_mismatch"] = True
        flags["usage_mismatch_fields"] = gap_fields
    absent = sorted({r["model"] for r in rows if r["role"] != RESIDUAL} - set(mu)) if mu else []
    if absent:
        flags["attribution_mismatch"] = True  # attributed models are not all keys of modelUsage
        flags["model_usage_missing"] = absent  # no provider record for these models: authoritative cost is partial
        flags["cost_partial"] = True
    main_reqs = [r for r in dedup_requests(events) if not r["parent"]]
    if main_reqs:
        u0 = main_reqs[0]["usage"]
        pre = {c: _num(u0.get(c)) for c in CATS[:3]}
        flags["first_request_usage"] = pre
        flags["first_request_prefix_tokens"] = sum(pre.values())  # input + cache_read + cache_creation, first main request
    return {**flags, **category_totals(rows), "by_role_model": rows, "requests": sum(r["requests"] for r in rows),
            "cost_authoritative_usd": auth, "cost_policy_usd": None if pol is None else round(pol, 6),
            "cost_policy_diff_pct": diff, "cost_policy_flag": bool(diff is not None and abs(diff) > COST_FLAG_PCT),
            "router_overhead_tokens": overhead, "hook_events": n_hook}


DERIVED_FLAGS = ("incomplete", "usage_complete", "missing_fields", "tokens_complete", "total_tokens_lower_bound",
                 "usage_mismatch", "usage_mismatch_fields", "attribution_mismatch", "model_usage_missing", "cost_partial",
                 "residual_negative", "cost_without_tokens", "tokens_without_cost")


def metrics_from(data: dict, tools: int, deleg: int, stream=None) -> dict:
    """Metric fields of a results.jsonl row derived from the aggregated result (single source for run and recompute)."""
    u = data.get("usage") or {}
    api = data.get("duration_api_ms")
    m = {
        "total_cost_usd": data.get("total_cost_usd"), "num_turns": data.get("num_turns"),
        "tool_calls": tools, "delegations": deleg, "n_results": data.get("n_results"),
        "duration_ms": data.get("duration_ms"), "duration_api_ms": api,
        "duration_api_s": round(api / 1000, 2) if isinstance(api, (int, float)) else None,
        "uncached_input": u.get("input_tokens"), "cached_input": u.get("cache_read_input_tokens"),
        "cache_write": u.get("cache_creation_input_tokens"), "output_tokens": u.get("output_tokens"),
        "models": {m: {"cost": v.get("costUSD"), "in": v.get("inputTokens"), "out": v.get("outputTokens"),
                       "cache_read": v.get("cacheReadInputTokens"), "cache_write": v.get("cacheCreationInputTokens")}
                   for m, v in (data.get("modelUsage") or {}).items()},
        "subagent_stats": data.get("subagent_stats"), "is_error": data.get("is_error"),
        "subtype": data.get("subtype"), "turns": data.get("num_turns"),
    }
    inc = bool(data.get("parse_error")) or not data  # no `result` event: interrupted / killed / unparsable stream
    m["incomplete"] = inc
    if stream is not None:
        fields = attribution_fields(stream)
        m.update(fields)
        m["tokens_complete"] = bool(fields.get("tokens_complete")) and not inc  # no rows = tokens unknown, never 0
        if inc and fields:
            m["total_tokens_lower_bound"] = True
    m.update(consistency_flags(m))
    return m


def consistency_flags(m: dict) -> dict:
    """Cost/token inconsistencies of one run row: cost_without_tokens (cost > 0, tokens 0 or unknown) and
    tokens_without_cost (tokens > 0, no cost recorded). Only the flags that apply are returned."""
    cost = m.get("cost_authoritative_usd")
    cost = cost if _has(cost) else m.get("total_cost_usd")
    tok = m.get("total_tokens")
    out = {}
    if _has(cost) and cost > 0 and (not _has(tok) or tok == 0):
        out["cost_without_tokens"] = True
    if _has(tok) and tok > 0 and not _has(cost):
        out["tokens_without_cost"] = True
    return out


def stream_metrics(text: str) -> dict:
    """The results.jsonl metric fields of one raw stream-json text (shared by recompute, the fixtures tests and the
    token_accounting_check CLI): parse_stream -> metrics_from."""
    data, tools, deleg = parse_stream(text)
    return metrics_from(data, tools, deleg, text)


def sanitize_data(data: dict) -> dict:
    return {k: v for k, v in data.items() if k not in DROP_RESULT_KEYS}


_VERSION_CACHE: list = []


def claude_version() -> str:
    """`claude --version`, once per session (cached)."""
    if not _VERSION_CACHE:
        try:
            r = sh(["claude", "--version"], REPO, timeout=60)
            _VERSION_CACHE.append((r.stdout or r.stderr).strip() or "unknown")
        except (OSError, subprocess.SubprocessError) as e:
            _VERSION_CACHE.append(f"unavailable: {type(e).__name__}")
    return _VERSION_CACHE[0]


def overlay_manifest_hash(repo: Path = REPO) -> str:
    """sha256 over .claude/skills/cost-router files (sorted, no __pycache__) + .claude/settings.json contents."""
    import hashlib
    h = hashlib.sha256()
    files = sorted(p for p in (repo / ".claude" / "skills" / "cost-router").rglob("*")
                   if p.is_file() and "__pycache__" not in p.parts)
    files.append(repo / ".claude" / "settings.json")
    for p in files:
        if p.exists():
            h.update(p.relative_to(repo).as_posix().encode())
            h.update(b"\0")
            h.update(p.read_bytes())
            h.update(b"\0")
    return h.hexdigest()


def order_for(conds: list[str], rep: int, order: str) -> list[str]:
    """fixed = as given; abba = reversed on odd repeats (A B | B A | A B ...)."""
    if order == "abba" and rep % 2 == 1:
        return list(reversed(conds))
    return list(conds)


def run_once(wt: Path, task: Task, condition: str, a, rep=0):
    guard_not_repo_root(wt)
    turns = task.max_turns or a.max_turns
    budget = task.budget_usd or a.max_budget_usd
    sources, env = condition_setup(condition, a)
    tools = "" if isinstance(task, ProbeTask) else TOOLS   # probe: no tools
    cmd = ["claude", "-p", "--model", a.model, "--max-turns", str(turns), "--max-budget-usd", str(budget)]
    if sources:
        cmd += ["--setting-sources", sources]
    cmd += ["--allowedTools", tools, *condition_extra_args(condition, a),  # extra args before a non-variadic flag
            "--output-format", "stream-json", "--verbose", "--no-session-persistence"]
    if getattr(a, "include_hook_events", True):
        cmd.append("--include-hook-events")
    cmd.append(task.prompt)
    started = time.strftime("%Y-%m-%dT%H:%M:%S%z")
    t0 = time.time()
    r = sh(cmd, wt, timeout=a.timeout, env=env)
    wall = time.time() - t0
    raw = a.out / "raw"
    raw.mkdir(parents=True, exist_ok=True)
    reduced = reduce_stream(r.stdout, keep_hooks=True)
    (raw / f"{task.id}_{condition}_{rep}.stream.jsonl").write_text(reduced, encoding="utf-8")
    data, tools, deleg = parse_stream(r.stdout)
    if not data:
        data = {"parse_error": True, "stdout_head": "", "stderr_head": r.stderr[:500]}
    text = str(data.get("result", ""))
    ok, why, extra = task.check_ex(wt, text)
    if isinstance(task, ProbeTask) and ok and (r.returncode != 0 or data.get("is_error")):
        ok, why = False, f"claude exit {r.returncode}, is_error={data.get('is_error')}: {text[:120]}"  # an error text is not an answer
    incomplete = bool(data.get("parse_error"))  # no `result` event: the harness cannot call the run correct
    if incomplete and ok:
        ok, why = False, f"INCOMPLETE stream (no result event); checker said: {why}"
    rec = {
        "task": task.id, "kind": task.kind, "condition": condition, "exit_code": r.returncode,
        "correct": ok, "check": why, "wall_s": round(wall, 1), "latency_s": round(wall, 1),
        "started": started, "result_head": text[:300],
        **({"task_version": 2} if task.id == "T2" else {}), **extra,
        **metrics_from(data, tools, deleg, reduced),
    }
    return rec, sanitize_data(data)


METRICS = [("total_cost_usd", "cost USD", 4), ("tool_calls", "tool_calls", 1), ("delegations", "delegations", 1),
           ("latency_s", "latency s", 1), ("cached_input", "cached in", 0), ("uncached_input", "uncached in", 0),
           ("output_tokens", "output tok", 0)]


def _vals(rs, f):
    return [r[f] for r in rs if isinstance(r.get(f), (int, float)) and not isinstance(r.get(f), bool)]


def mean_sd(v):
    if not v:
        return None, None
    m = sum(v) / len(v)
    sd = math.sqrt(sum((x - m) ** 2 for x in v) / (len(v) - 1)) if len(v) > 1 else None
    return m, sd


def t_crit(df: int) -> float:
    try:
        from scipy import stats
        return float(stats.t.ppf(0.975, df))
    except Exception:
        return 2.0


def diff_ci(base: list, rout: list):
    """(delta, lo, hi) of mean(rout)-mean(base) with Welch SE; lo/hi None when either arm has n<2."""
    if not base or not rout:
        return None
    mb, sb = mean_sd(base)
    mr, sr = mean_sd(rout)
    d = mr - mb
    if sb is None or sr is None:
        return d, None, None
    se = math.sqrt(sr ** 2 / len(rout) + sb ** 2 / len(base))
    t = t_crit(min(len(rout), len(base)) - 1)
    return d, d - t * se, d + t * se


def _cell(v, d):
    m, sd = mean_sd(v)
    if m is None:
        return "n/a"

    def f(x):
        return f"{x:.{d}f}"
    s = f"{f(m)} ± {f(sd)}" if sd is not None else f(m)
    return f"{s} [{f(min(v))}..{f(max(v))}]"


def sign_test_p(k: int, m: int) -> float:
    """Two-sided exact binomial p (p0=0.5) for k successes out of m."""
    if m == 0:
        return 1.0

    def pk(i):
        return math.comb(m, i) / 2 ** m
    return min(1.0, sum(pk(i) for i in range(m + 1) if pk(i) <= pk(k) + 1e-12))


def _run_id(r) -> str:
    return f"{r['task']}_{r['condition']}_{r.get('repeat', 0)}"


def _arms(rows) -> list[str]:
    out: list[str] = []
    for r in rows:
        if r["condition"] not in out:
            out.append(r["condition"])
    return out


def _ref_arm(arms: list[str]) -> str | None:
    return "baseline" if "baseline" in arms else (arms[0] if arms else None)


def _label(task: str, ref: str, other: str) -> str:
    return task if (ref, other) == ("baseline", "router") else f"{task} [{other} vs {ref}]"


def exclusion_reason(r: dict, tokens: bool = False) -> str | None:
    """Why a run cannot enter a base-vs-arm comparison (None = eligible): incomplete stream, incorrect answer and, for
    token statistics, a token total that is only a lower bound (missing usage fields) or absent."""
    if r.get("incomplete"):
        return "incomplete"
    if not r.get("correct"):
        return "incorrect"
    if tokens:
        if r.get("tokens_complete") is False:
            return "tokens incomplete" + (f" (missing {','.join(r['missing_fields'])})" if r.get("missing_fields") else "")
        if not _has(r.get("total_tokens")):
            return "no token attribution"
    return None


def paired_runs(rows, task, ref, other, tokens=False):
    """((ref_row, other_row) pairs usable for the deltas, [(repeat, reason)] excluded) for one task. Runs are paired by
    `repeat` (rows without it by order of appearance); a pair is used only when BOTH runs are correct and complete."""
    def by_rep(arm):
        out: dict = {}
        for i, r in enumerate([x for x in rows if x["task"] == task and x["condition"] == arm]):
            k = r.get("repeat") if r.get("repeat") is not None else i
            out[(k, i) if k in out else k] = r
        return out
    b, o = by_rep(ref), by_rep(other)
    used, excl = [], []
    for k in sorted(set(b) | set(o), key=str):
        if k not in b or k not in o:
            excl.append((k, f"no {ref if k not in b else other} run"))
            continue
        why = [f"{arm} {w}" for arm, r in ((ref, b[k]), (other, o[k])) if (w := exclusion_reason(r, tokens))]
        if why:
            excl.append((k, "; ".join(why)))
        else:
            used.append((b[k], o[k]))
    return used, excl


def summarize(rows: list[dict]) -> str:
    excluded = [r for r in rows if r.get("excluded")]
    rows = [r for r in rows if not r.get("excluded")]
    inc = [r for r in rows if r.get("incomplete")]
    head = ["task", "kind", "condition", "n", "correct"] + [m[1] for m in METRICS]
    lines = ["| " + " | ".join(head) + " |", "|" + "---|" * len(head)]
    for t, k, c in sorted({(r["task"], r["kind"], r["condition"]) for r in rows}):
        rs = [r for r in rows if (r["task"], r["condition"]) == (t, c)]
        n = len(rs)
        cells = [_cell(_vals(rs, f), d) for f, _, d in METRICS]
        lines.append(f"| {t} | {k} | {c} | {n} | {sum(bool(r['correct']) for r in rs)}/{n} | " + " | ".join(cells) + " |")
    lines += ["", "Cells: mean ± sample sd [min..max]. Router vs baseline (cost, 95% CI of the mean difference):", "",
              "| task | pairs used base/router | pairs excluded | mean cost base | mean cost router | delta % | CI95 of delta USD | excludes zero |",
              "|---|---|---|---|---|---|---|---|"]
    cheaper = []
    arms = _arms(rows)
    ref = _ref_arm(arms)
    used_all, excl_all = 0, []
    for other in [a for a in arms if a != ref]:
        for t in sorted({r["task"] for r in rows}):
            pairs, ex = paired_runs(rows, t, ref, other)
            used_all += len(pairs)
            excl_all += [f"{t}#{k}: {why}" for k, why in ex]
            vb = _vals([b for b, _ in pairs], "total_cost_usd")
            vo = _vals([o for _, o in pairs], "total_cost_usd")
            res = diff_ci(vb, vo)
            if res is None:
                continue
            d, lo, hi = res
            cheaper.append(d < 0)
            mb = sum(vb) / len(vb)
            pct = f"{100 * d / mb:+.1f}%" if mb else "n/a"
            ci = f"[{lo:+.4f}, {hi:+.4f}]" if lo is not None else "n/a (n<2)"
            ex_ = "n/a" if lo is None else ("yes" if (lo > 0 or hi < 0) else "no")
            lines.append(f"| {_label(t, ref, other)} | {len(vb)}/{len(vo)} | {len(ex)} | {mb:.4f} | "
                         f"{sum(vo) / len(vo):.4f} | {pct} | {ci} | {ex_} |")
    lines += ["", f"pairs_used={used_all} pairs_excluded={len(excl_all)}: only (task, repeat) pairs in which BOTH arms are "
              "correct and complete enter the deltas"
              + (" (excluded: " + "; ".join(excl_all) + ")" if excl_all else "") + "."]
    if cheaper:
        k, m = sum(cheaper), len(cheaper)
        lines += ["", f"Sign test across tasks: router cheaper in {k} of {m} tasks "
                  f"(two-sided exact binomial p={sign_test_p(k, m):.3f}); magnitude ignored. "
                  "With n=2 per arm no per-task conclusion is supported."]
    if inc:
        lines += ["", "INCOMPLETE runs (no result event; never correct, excluded from every delta): "
                  + ", ".join(_run_id(r) for r in inc) + "."]
    if excluded:
        ids = sorted({f"{r['task']}_{r['condition']}_{r.get('repeat')}" for r in excluded})
        lines += ["", f"Excluded rows (premise invalid at HEAD; excluded): {', '.join(ids)}."]
    return "\n".join(lines)


TOKEN_DEFINITION = (
    "Token definition (stated once): `total_tokens` = sum over all deduplicated assistant requests (main + subagents, "
    "plus the per-model residual that reconciles streamed output with modelUsage) of input + cache_read + cache_creation "
    "+ output. `input` is the uncached input only; cache_read is its own category and is never equivalent to input. "
    "token_reduction_percent = 100*(baseline-router)/baseline (negative = the router used MORE); cost reduction is "
    "reported separately from the authoritative modelUsage.costUSD. A lower cost with higher tokens is not a token success.")
TOK_COLS = (("tok_input", "input"), ("tok_cache_read", "cache_read"), ("tok_cache_creation", "cache_creation"),
            ("tok_output", "output"), ("total_tokens", "total_tokens"))


def _cost_of(r):
    c = r.get("cost_authoritative_usd")
    return c if isinstance(c, (int, float)) else r.get("total_cost_usd")


def _ms(v, d=0):
    m, sd = mean_sd(v)
    if m is None:
        return "n/a"
    return f"{m:.{d}f} ± {sd:.{d}f}" if sd is not None else f"{m:.{d}f}"


def _red(vb, vr):
    mb, mr = (sum(vb) / len(vb), sum(vr) / len(vr)) if vb and vr else (None, None)
    return f"{100 * (mb - mr) / mb:+.1f}%" if mb else "n/a"


def _ci(vb, vr, d=0):
    res = diff_ci(vb, vr)
    if res is None or res[1] is None:
        return "n/a (n<2)"
    return f"[{res[1]:+.{d}f}, {res[2]:+.{d}f}]" + ("" if (res[1] > 0 or res[2] < 0) else " (incl. 0)")


def _field(rs, f):
    return _vals(rs, f)


_WARN_FLAGS = (
    ("residual_negative", "unattributed_residual is negative (attributed tokens exceed modelUsage)"),
    ("attribution_mismatch", "attributed models are not all keys of modelUsage"),
    ("usage_mismatch", "input / cache_read / cache_creation of the attributed requests differ from modelUsage"),
    ("cost_without_tokens", "cost > 0 but tokens are 0 or unknown"),
    ("tokens_without_cost", "tokens > 0 but no cost was recorded"),
    ("cost_partial", "modelUsage has no record for an attributed model, so the authoritative cost is partial"),
)


def _warning_lines(rows: list[dict]) -> list[str]:
    out = []
    for flag, what in _WARN_FLAGS:
        bad = [r for r in rows if r.get(flag)]
        if bad:
            out += ["", f"WARNING {flag}: {what} in {len(bad)} runs (" + ", ".join(_run_id(r) for r in bad)
                    + "); their token rows are unreliable."]
    unk = [r for r in rows if r.get("tokens_complete") is False]
    if unk:
        out += ["", f"WARNING tokens_incomplete: usage fields missing or stream incomplete in {len(unk)} runs ("
                + ", ".join(_run_id(r) + (f" missing {','.join(r['missing_fields'])}" if r.get("missing_fields") else "")
                            for r in unk) + "); their totals are LOWER BOUNDS and they are excluded from token statistics."]
    inc = [r for r in rows if r.get("incomplete")]
    if inc:
        out += ["", "WARNING incomplete: no result event (interrupted stream) in " + str(len(inc)) + " runs ("
                + ", ".join(_run_id(r) for r in inc) + "); never counted correct, excluded from every delta."]
    return out


def summarize_tokens(rows: list[dict]) -> str:
    rows = [r for r in rows if not r.get("excluded")]
    warn = _warning_lines(rows)
    rows = [dict(r, _cost=_cost_of(r)) for r in rows if _has(r.get("total_tokens"))]
    if not rows:
        return "\n".join(["No token attribution available (no run carries total_tokens)."] + warn)
    head = ["task", "arm", "n", "correct"] + [c[1] for c in TOK_COLS] + ["cost USD (modelUsage)", "turns", "delegations"]
    lines = [TOKEN_DEFINITION, "", "Per task x arm, ALL runs (descriptive; a run with tokens_complete=false is a lower "
             "bound; mean ± sample sd):", "",
             "| " + " | ".join(head) + " |", "|" + "---|" * len(head)]
    for t_, c in sorted({(r["task"], r["condition"]) for r in rows}):
        rs = [r for r in rows if (r["task"], r["condition"]) == (t_, c)]
        cells = [_ms(_field(rs, f)) for f, _ in TOK_COLS]
        lines.append(f"| {t_} | {c} | {len(rs)} | {sum(bool(r['correct']) for r in rs)}/{len(rs)} | " + " | ".join(cells)
                     + f" | {_ms(_field(rs, '_cost'), 4)} | {_ms(_field(rs, 'turns'), 1)} | {_ms(_field(rs, 'delegations'), 1)} |")

    def delta_row(label, b, o, n_excl):
        reds = [_red(_field(b, f), _field(o, f)) for f, _ in TOK_COLS]
        vb, vo = _field(b, "total_tokens"), _field(o, "total_tokens")
        cb, co = _field(b, "_cost"), _field(o, "_cost")
        return (f"| {label} | {len(b)}/{len(o)} | {n_excl} | " + " | ".join(reds) + f" | {_ci(vb, vo)} | "
                f"{_red(cb, co)} | {_ci(cb, co, 4)} |")
    dh = ["task", "pairs used base/router", "pairs excluded"] + [f"token_reduction_percent {c[1]}" for c in TOK_COLS] + [
        "CI95 of total_tokens delta (router-base)", "cost reduction %", "CI95 of cost delta USD (router-base)"]
    arms = _arms(rows)
    ref = _ref_arm(arms)
    lines += ["", "Router vs baseline over (task, repeat) PAIRS in which both arms are correct, complete and "
              "token-complete only (positive reduction = router cheaper/smaller; CI95 = Welch, mean difference):", "",
              "| " + " | ".join(dh) + " |", "|" + "---|" * len(dh)]
    pooled: dict = {}
    excl_by: dict = {}
    excl_all: list[str] = []
    for other in [a for a in arms if a != ref]:
        pb, po = pooled.setdefault(other, ([], []))
        for t_ in sorted({r["task"] for r in rows}):
            pairs, ex = paired_runs(rows, t_, ref, other, tokens=True)
            excl_all += [f"{_label(t_, ref, other)}#{k}: {why}" for k, why in ex]
            excl_by[other] = excl_by.get(other, 0) + len(ex)
            pb += [b_ for b_, _ in pairs]
            po += [o_ for _, o_ in pairs]
            if pairs:
                lines.append(delta_row(_label(t_, ref, other), [b_ for b_, _ in pairs], [o_ for _, o_ in pairs], len(ex)))
    n_used = sum(len(v[0]) for v in pooled.values())
    if any(v[0] for v in pooled.values()):
        lines += ["", "Overall pooled over all tasks and the correct+complete pairs (task mix is identical in both arms by "
                  "construction; tasks differ greatly in size, read per-task rows first):", "",
                  "| " + " | ".join(dh) + " |", "|" + "---|" * len(dh)]
        for other, (pb, po) in pooled.items():
            if pb:
                lines.append(delta_row("ALL" if (ref, other) == ("baseline", "router") else f"ALL [{other} vs {ref}]",
                                       pb, po, excl_by.get(other, 0)))
    lines += ["", f"pairs_used={n_used} pairs_excluded={len(excl_all)}"
              + (" (excluded: " + "; ".join(excl_all) + ")" if excl_all else "") + ". A pair is excluded when either arm is "
              "incorrect, incomplete (no result event) or has missing usage fields, or has no counterpart; a cheaper "
              "failed run therefore never counts as a saving."]
    flagged = [r for r in rows if r.get("cost_policy_flag")]
    diffs = [r["cost_policy_diff_pct"] for r in rows if isinstance(r.get("cost_policy_diff_pct"), (int, float))]
    lines += ["", f"Cost cross-check (policy-card recompute vs modelUsage.costUSD): {len(diffs)} runs priced, "
              + (f"max |difference| {max(abs(d) for d in diffs):.2f}%" if diffs else "none priced")
              + f"; flagged (> {COST_FLAG_PCT:.0f}%): {len(flagged)}"
              + (" (" + ", ".join(f"{r['task']}_{r['condition']}_{r.get('repeat')}" for r in flagged) + ")" if flagged else "") + "."]
    lines += warn
    ov = [r["router_overhead_tokens"] for r in rows if isinstance(r.get("router_overhead_tokens"), (int, float))]
    lines += [f"Router self-overhead (hook-injected context, chars/4, from hook events in the stream): "
              + (f"mean {sum(ov) / len(ov):.0f} tokens per run over {len(ov)} runs" if ov else "not measured (no hook events in these streams)") + "."]
    return "\n".join(lines)


def reconciliation(rows: list[dict]) -> str:
    """Per run and model: sum of the deduplicated requests (attributed rows) vs modelUsage per category. modelUsage =
    attributed + unattributed_residual. input / cache_read / cache_creation must reconcile exactly (a non-zero residual is
    FLAGGED); output is a streaming lower bound, so its residual is expected and only reported."""
    head = ["run", "model", "requests", "input (sum/mu)", "cache_read (sum/mu)", "cache_creation (sum/mu)",
            "output (sum/mu, residual)", "flags"]
    lines = ["Reconciliation (sum of deduplicated requests vs modelUsage, per run and model):", "",
             "| " + " | ".join(head) + " |", "|" + "---|" * len(head)]
    n = 0
    for r in rows:
        brm = r.get("by_role_model")
        if not isinstance(brm, list):
            continue
        for model in sorted({x["model"] for x in brm}):
            att = [x for x in brm if x["model"] == model and x["role"] != RESIDUAL]
            res = [x for x in brm if x["model"] == model and x["role"] == RESIDUAL]
            tot = {c: sum(x[c] for x in att) for c in CATS}
            gap = {c: sum(x[c] for x in res) for c in CATS}
            mu = {c: tot[c] + gap[c] for c in CATS}
            flags = [f"{c.split('_')[0] if c != 'cache_read_input_tokens' else 'cache_read'}_mismatch"
                     for c in CATS[:3] if gap[c]]
            if any(gap[c] < 0 for c in CATS):
                flags.append("residual_negative")
            run = f"{r['task']}_{r['condition']}_{r.get('repeat', 0)}"
            lines.append(f"| {run} | {model} | {sum(x['requests'] for x in att)} | "
                         + " | ".join(f"{tot[c]}/{mu[c]}" for c in CATS[:3])
                         + f" | {tot['output_tokens']}/{mu['output_tokens']} ({gap['output_tokens']:+d}) | "
                         + (", ".join(flags) or "ok") + " |")
            n += 1
    return "\n".join(lines) if n else "Reconciliation: no run carries per-request attribution."


def redact(root: Path) -> None:
    """Replace the home directory (backslash, slash and JSON-escaped forms) with <HOME> in text artifacts."""
    home = str(Path.home())
    fwd = home.replace("\\", "/")
    variants = {home, fwd, home.replace("\\", "\\\\"), re.sub(r"^([A-Za-z]):", lambda m: "/" + m.group(1).lower(), fwd),
                re.sub(r"[^A-Za-z0-9]", "-", home)}  # also MSYS /c/Users/x and the Claude project slug C--Users-x
    for p in root.rglob("*"):
        if p.is_file() and p.suffix in (".json", ".jsonl", ".md"):
            t = p.read_text(encoding="utf-8", errors="replace")
            n = t
            for v in sorted(variants, key=len, reverse=True):
                n = n.replace(v, "<HOME>")
            if n != t:
                p.write_text(n, encoding="utf-8")


RECOMPUTE_DATE = "2026-10-21"


def recompute(out: Path) -> int:
    """Rebuild results.jsonl and SUMMARY.md from the raw stream files (no claude invocation). Reduces the streams in place."""
    res_path, raw = out / "results.jsonl", out / "raw"
    rows = [json.loads(line) for line in res_path.read_text(encoding="utf-8").splitlines() if line.strip()]
    notes = []
    for r in rows:
        stem = f"{r['task']}_{r['condition']}_{r.get('repeat', 0)}"
        sf = raw / f"{stem}.stream.jsonl"
        reduced = reduce_stream(sf.read_text(encoding="utf-8", errors="replace"), keep_hooks=True)
        sf.write_text(reduced, encoding="utf-8")
        data, tools, deleg = parse_stream(reduced)
        for k in DERIVED_FLAGS:  # a re-derivation must not inherit a flag from an earlier derivation
            r.pop(k, None)
        r.update(metrics_from(data, tools, deleg, reduced))
        if r["incomplete"] and r.get("correct"):
            r["correct"], r["rescored"] = False, True
            r["check"] = f"{r.get('check', '')}; RE-SCORED {RECOMPUTE_DATE}: stream has no result event (incomplete)"
            notes.append(f"{stem} re-scored correct -> INCORRECT: stream has no result event (incomplete)")
        r["latency_s"] = r.get("wall_s")
        (raw / f"{stem}.json").write_text(json.dumps(sanitize_data(data), indent=1), encoding="utf-8")
        if r["task"] == "T6" and r.get("correct") and admits_unfixed(str(data.get("result", ""))):
            r["correct"], r["rescored"] = False, True
            r["check"] = f"{r.get('check', '')}; RE-SCORED {RECOMPUTE_DATE}: final answer admits unfixed issues"
            notes.append(f"{stem} re-scored correct -> INCORRECT by the T6 admission rule (stored result text matches)")
        if r["task"] == "T2" and not r.get("task_version"):
            r["excluded"] = "premise invalid at HEAD; excluded"
    res_path.write_text("\n".join(json.dumps(r) for r in rows) + "\n", encoding="utf-8")
    old = (out / "SUMMARY.md").read_text(encoding="utf-8")
    cut = old.find("\n| task |")
    head = (old[:cut] if cut > 0 else old).replace(
        "tokens and cost come from the final stream-json `result` event (`usage`, `total_cost_usd`)",
        "tokens come from `usage` summed over ALL stream-json `result` events, cost from the last (cumulative) "
        "`total_cost_usd`; latency = harness wall clock")
    head += (f"\n**Recomputed on {RECOMPUTE_DATE} after a harness fix** (no new claude runs): the token, turn, "
             "tool-call and latency columns were rebuilt from the privacy-reduced raw streams with usage summed over all "
             "`result` events (delegated runs have 2+) and `latency s` = wall clock (`wall_s`) instead of the last "
             "event's duration_ms. Cost values are unchanged (last `total_cost_usd`, equal to the sum of "
             "modelUsage.costUSD). Correctness of T1, T3, T4, T5 is as stored (checks at run time); T6 is re-scored only "
             "for the new admission-of-unfixed-issues rule, applied to the stored final answer text (the stricter "
             "`--list` vs plan() structural check needs the worktree state and applies to new runs only). "
             "T2 rows are excluded: the v1 premise (PLUGINS_DIRECTORY) does not exist at HEAD.\n")
    if notes:
        head += "\nRe-scoring notes:\n" + "\n".join(f"- {n}" for n in notes) + "\n"
    (out / "SUMMARY.md").write_text(head + "\n" + summarize(rows) + "\n\n" + summarize_tokens(rows) + "\n\n"
                                    + reconciliation(rows) + "\n", encoding="utf-8")
    redact(out)
    print(out / "SUMMARY.md")
    return 0


def main(argv=None) -> int:
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--out", type=Path)
    ap.add_argument("--recompute", type=Path, metavar="OUT_DIR",
                    help="rebuild results.jsonl + SUMMARY.md from raw streams (no claude invocation)")
    ap.add_argument("--worktree", type=Path, default=REPO.parent / "AI_Memory_Vault_bench_wt")
    ap.add_argument("--tasks", default=",".join(t.id for t in TASKS))
    ap.add_argument("--conditions", default="baseline,router")
    ap.add_argument("--repeats", type=int, default=1)
    ap.add_argument("--order", choices=("abba", "fixed"), default="abba",
                    help="abba alternates the condition order per repeat; fixed keeps the given order (old runs)")
    ap.add_argument("--rep-offset", type=int, default=0,
                    help="first repeat index (continue an --out dir: results.jsonl rows are kept, abba parity follows the index)")
    ap.add_argument("--warmup", action="store_true",
                    help="one discarded baseline run of the first task first (spend counted, stats excluded)")
    ap.add_argument("--model", default="sonnet")
    ap.add_argument("--max-turns", type=int, default=15)
    ap.add_argument("--max-budget-usd", type=float, default=1.0)
    ap.add_argument("--max-total-usd", type=float, default=None,
                    help="stop launching new runs once cumulative total_cost_usd exceeds this")
    ap.add_argument("--timeout", type=int, default=900)
    ap.add_argument("--dry-run", action="store_true")
    ap.add_argument("--overlay", action="store_true", help="copy the working-tree .claude hooks into the worktree")
    ap.add_argument("--router-env", action="append", default=[], metavar="KEY=VAL",
                    help="env var set only for the router-condition claude -p child (repeatable)")
    ap.add_argument("--env", action="append", default=[], metavar="KEY=VAL",
                    help="env var set for BOTH arms' claude -p child (repeatable); COST_ROUTER_OFF is reserved")
    ap.add_argument("--legacy-baseline", action="store_true",
                    help="phase 1-2 semantics: baseline = --setting-sources user without COST_ROUTER_OFF (arms differ beyond hooks)")
    ap.add_argument("--conditions-file", type=Path, metavar="JSON",
                    help='JSON list of {"name","overlay":[repo paths],"remove":[paths],"files":{path:text},"env":{},'
                         '"setting_sources","extra_args":[]}: arms may differ in context files as well as hooks')
    ap.add_argument("--probe", metavar="PROMPT",
                    help="single-turn probe (max-turns 1, --allowedTools \"\", correct = non-empty result) for prefix measurement")
    ap.add_argument("--include-hook-events", action=argparse.BooleanOptionalAction, default=True,
                    help="pass --include-hook-events to claude and keep reduced hook events {event, chars} (default on)")
    a = ap.parse_args(argv)
    if a.recompute:
        return recompute(a.recompute)
    if a.out is None:
        ap.error("--out is required (or use --recompute)")
    a.cond_specs = None
    try:
        if a.conditions_file:
            a.cond_specs = load_conditions(a.conditions_file)
        condition_setup("router", a)  # validates --env / --router-env
    except (ValueError, OSError) as e:
        ap.error(str(e))
    if a.worktree.resolve() == REPO.resolve():
        ap.error("--worktree must not be the repository root")
    by_id = {t.id: t for t in TASKS}
    wanted = [by_id[i] for i in a.tasks.split(",") if i in by_id]  # keeps the order given
    if a.probe:
        wanted = [ProbeTask("PROBE", "probe", a.probe, max_turns=1)]
    known = list(SOURCES) + [c for c in (a.cond_specs or {}) if c not in SOURCES]
    if a.cond_specs and a.conditions == ap.get_default("conditions"):
        conds = list(a.cond_specs)  # the file defines the arms
    else:
        conds = [c for c in a.conditions.split(",") if c in known]
    arm_sources = {c: condition_setup(c, a)[0] for c in known}
    if a.dry_run:
        for rep in range(a.repeats):
            for t in wanted:
                for c in order_for(conds, rep, a.order):
                    print(t.id, c, "claude -p --setting-sources", arm_sources[c],
                          ("env COST_ROUTER_OFF=1" if condition_setup(c, a)[1].get(OFF_ENV) else "hooks on"), "max_turns", t.max_turns or a.max_turns,
                          "budget_usd", t.budget_usd or a.max_budget_usd, "--allowedTools",
                          "" if isinstance(t, ProbeTask) else TOOLS, *condition_extra_args(c, a))
        return 0
    a.out.mkdir(parents=True, exist_ok=True)
    head = ensure_worktree(a.worktree)
    run_info = {"argv": list(argv if argv is not None else sys.argv), "claude_version": claude_version(),
                "router_env": a.router_env, "env_both_arms": a.env, "legacy_baseline": a.legacy_baseline,
                "include_hook_events": a.include_hook_events, "probe": a.probe, "conditions": a.cond_specs,
                "child_env_flags": {NO_SELF_INSTALL_ENV: "1"}, "overlay_archive": {},
                "arms": {c: {"setting_sources": arm_sources[c], "cost_router_off": condition_setup(c, a)[1].get(OFF_ENV)}
                         for c in conds}, "overlay": a.overlay, "order": a.order, "warmup": a.warmup,
                "overlay_manifest_sha256": overlay_manifest_hash() if a.overlay else None,
                "started": time.strftime("%Y-%m-%dT%H:%M:%S%z"), "platform": sys.platform,
                "python": sys.version.split()[0], "worktree_head": head}
    rows = []
    if a.rep_offset == 0 and (a.out / "results.jsonl").exists():
        # phase-4 review: an offset-0 run into an existing out dir silently overwrote results, RUN_INFO and manifests
        raise SystemExit(f"{a.out / 'results.jsonl'} exists: continue with --rep-offset N or use a new --out dir")
    if a.rep_offset > 0 and (a.out / "results.jsonl").exists():
        rows = [json.loads(l) for l in (a.out / "results.jsonl").read_text(encoding="utf-8").splitlines() if l.strip()]
    raw = a.out / "raw"
    raw.mkdir(exist_ok=True)
    spent, stop_note = 0.0, None
    def prepare(cond):  # reset + overlay/spec of one arm; the applied overlay contents are archived once per condition
        spec = (a.cond_specs or {}).get(cond)
        info = (reset_worktree(a.worktree, overlay=a.overlay, spec=spec) if spec is not None
                else reset_worktree(a.worktree, overlay=a.overlay)) or {}
        if cond not in run_info["overlay_archive"]:
            run_info["overlay_archive"][cond] = archive_overlay(a.out, cond, a.worktree, info)
        return info

    if a.warmup and wanted and conds and a.rep_offset == 0:
        w_cond = "baseline" if "baseline" in conds else conds[0]
        prepare(w_cond)  # same worktree content as every measured run
        rec, _ = run_once(a.worktree, wanted[0], w_cond, a, "warmup")
        cost = rec.get("total_cost_usd")
        spent += cost if isinstance(cost, (int, float)) else 0.0
        run_info["warmup_cost_usd"] = cost
        print(json.dumps({"warmup": True, "task": wanted[0].id, "total_cost_usd": cost, "running_spend_usd": round(spent, 4)}),
              flush=True)
    for rep in range(a.rep_offset, a.rep_offset + a.repeats):
        for t in wanted:
            for c in order_for(conds, rep, a.order):
                if a.max_total_usd is not None and spent > a.max_total_usd:
                    stop_note = (f"STOPPED: cumulative cost ${spent:.4f} exceeded --max-total-usd {a.max_total_usd}; "
                                 f"no run launched from repeat {rep} task {t.id} condition {c} onward.")
                    break
                prepare(c)
                rec, data = run_once(a.worktree, t, c, a, rep)
                rec["repeat"] = rep
                rows.append(rec)
                cost = rec.get("total_cost_usd")
                spent += cost if isinstance(cost, (int, float)) else 0.0
                (raw / f"{t.id}_{c}_{rep}.json").write_text(json.dumps(data, indent=1), encoding="utf-8")
                print(json.dumps({**{k: rec.get(k) for k in ("task", "condition", "repeat", "correct", "total_cost_usd",
                                                             "num_turns", "tool_calls", "delegations", "wall_s",
                                                             "exit_code")},
                                  "running_spend_usd": round(spent, 4)}), flush=True)
            if stop_note:
                break
        if stop_note:
            break
    reset_worktree(a.worktree)
    run_info["finished"] = time.strftime("%Y-%m-%dT%H:%M:%S%z")
    run_info["total_spend_usd"] = round(spent, 4)
    (a.out / ("RUN_INFO.json" if a.rep_offset == 0 else f"RUN_INFO_rep{a.rep_offset}.json")).write_text(json.dumps(run_info, indent=1), encoding="utf-8")
    (a.out / "results.jsonl").write_text("\n".join(json.dumps(r) for r in rows) + "\n", encoding="utf-8")
    md = [f"# Claude Code cost-router A/B benchmark\n", f"HEAD of benchmark worktree: `{head}`; model flag: `{a.model}`; "
          f"max-turns {a.max_turns} (T6: 40); max-budget-usd {a.max_budget_usd} (T6: 2.5); repeats {a.repeats}; "
          f"order {a.order}; warmup {a.warmup}; "
          f"router hooks from {'working tree (overlay)' if a.overlay else 'HEAD'}. See RUN_INFO.json.\n",
          "Metric definitions: tokens come from `usage` summed over all stream-json `result` events, cost from the last "
          "(cumulative) `total_cost_usd`; latency = harness wall clock; "
          "tool_calls/delegations are counted from `tool_use` blocks in the stream (Agent/Task = delegation); "
          "`context_tokens` is NOT reported. Correctness = the per-task check in this script. "
          "Token totals come from the deduplicated assistant requests (see TOKEN_DEFINITION below), not from `result.usage`. "
          + ("Baseline = `--setting-sources user` (LEGACY: no project hooks, MCP or skills; arms differ beyond hooks); router = `user,project`. "
             if a.legacy_baseline else
             "EQUAL ARMS: both arms `--setting-sources user,project` (CLAUDE.md, project MCP and skills load identically); "
             "baseline additionally has COST_ROUTER_OFF=1 so every hook is inert; the only difference is the hook layer. ")
          + "Formulas and token attribution rules: see script header.\n",
          f"Total spend: ${spent:.4f}" + (f" (cap {a.max_total_usd})" if a.max_total_usd is not None else "") + ".\n",
          (stop_note + "\n") if stop_note else "", summarize(rows), "", summarize_tokens(rows), "", reconciliation(rows), ""]
    (a.out / "SUMMARY.md").write_text("\n".join(md), encoding="utf-8")
    redact(a.out)
    print(a.out / "SUMMARY.md")
    return 0


if __name__ == "__main__":
    sys.exit(main())
