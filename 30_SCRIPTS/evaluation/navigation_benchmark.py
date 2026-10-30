"""Vault navigation benchmark: direct vault:// routes (V) vs plain search (G) vs a flat index of every note (I).

Pre-registration: 07_EVALUATION/navigation_benchmark/PREREGISTRATION.md (read it before changing anything here).

    prepare  build the subject checkouts outside the repository: <work>/plain (agent contracts, .claude and .mcp.json
             removed) and <work>/index (the same plus INDEX.md), the route table and the MCP config for condition V
    run      claude -p for every question x condition x repeat; one JSON per run in <out>/raw/
    report   deterministic grading, accuracy, tokens, paired sign test, the pre-registered decision -> <out>/RESULTS.md

    python 30_SCRIPTS/evaluation/navigation_benchmark.py prepare --work %TEMP%/navbench/work --ref claude/route-coverage-gate
    python 30_SCRIPTS/evaluation/navigation_benchmark.py run --work ... --gold 07_EVALUATION/navigation_benchmark/gold_set.json --out 07_EVALUATION/navigation_benchmark/run_<date>
    python 30_SCRIPTS/evaluation/navigation_benchmark.py report --work ... --gold ... --out ...

Privacy: subject checkout paths are replaced by <REPO> in stored answers; only the final text and usage are kept.
"""
from __future__ import annotations

import argparse
import json
import math
import os
import random
import re
import shutil
import statistics
import subprocess
import sys
from pathlib import Path
from typing import Dict, List, Optional

REPO = Path(__file__).resolve().parents[2]
SEED = 20261031
CONDITIONS = ("G", "I", "V")
REMOVED = ("AGENTS.md", "CLAUDE.md", "GEMINI.md", ".mcp.json", ".claude")
BASE_TOOLS = "Read,Grep,Glob"
VAULT_TOOLS = ",".join(f"mcp__vault-memory__{t}" for t in
                       ("vault_resolve", "vault_list", "vault_read", "vault_search", "vault_get_metadata"))

BASE_INSTRUCTION = (
    "You are working in the current directory, a markdown knowledge repository. Find the answer to the question in "
    "the repository's files. Answer only from what you read there. End with exactly two lines:\n"
    "ANSWER_PATH: <repo-relative path of the file that states the answer, or its vault:// URI>\n"
    "ANSWER: <the answer, quoted or closely paraphrased from that file>")
EXTRA = {
    "G": "",
    "I": "\n\nINDEX.md at the repository root lists every note as `path — title`.",
    "V": "\n\nThe vault-memory tools are available: vault_resolve(query) finds the direct vault:// route of a note, "
         "vault_list('*') lists domains, vault_read(uri) returns the verbatim text, vault_search(query) searches.",
}


def _child_env() -> Dict[str, str]:
    env = {k: v for k, v in os.environ.items() if k not in ("CLAUDECODE", "CLAUDE_CODE_CHILD_SESSION")}
    env["COST_ROUTER_OFF"] = "1"
    return env


def _git(args: List[str], cwd: Path) -> str:
    return subprocess.run(["git", *args], cwd=cwd, capture_output=True, check=True).stdout.decode("utf-8", "replace")


def cmd_prepare(work: Path, ref: str) -> None:
    work = work.resolve()
    work.mkdir(parents=True, exist_ok=True)
    for name in ("plain", "index"):
        target = work / name
        if target.exists():
            _git(["worktree", "remove", "--force", str(target)], REPO)
        _git(["worktree", "add", "--detach", str(target), ref], REPO)
        for rel in REMOVED:
            p = target / rel
            if p.is_dir():
                shutil.rmtree(p)
            elif p.exists():
                p.unlink()
    routes_file = work / "routes.json"
    subprocess.run([sys.executable, "30_SCRIPTS/routing/build_route_manifest.py", "--json", str(routes_file)],
                   cwd=work / "plain", check=True, capture_output=True)
    routes = json.loads(routes_file.read_text(encoding="utf-8"))
    lines = ["# Index of every note", ""]
    for r in sorted((r for r in routes if r.get("base") == "repo"), key=lambda r: r["path"]):
        title = (r.get("title") or "").replace("\n", " ").strip()
        lines.append(f"- {r['path']} — {title}" if title else f"- {r['path']}")
    (work / "index" / "INDEX.md").write_text("\n".join(lines) + "\n", encoding="utf-8")
    server = work / "plain" / "03_IMPLEMENTATION" / "packages" / "interfaces" / "memory_mcp_server.py"
    (work / "mcp_vault.json").write_text(json.dumps({"mcpServers": {"vault-memory": {
        "command": sys.executable, "args": [str(server), "--principal", "cloud_cli.claude_code"]}}}, indent=1),
        encoding="utf-8")
    print(f"prepared {work}: {len(routes)} routes, INDEX.md with {len(lines) - 2} notes")


def plan_runs(question_ids: List[str], repeats: int) -> List[Dict[str, object]]:
    runs = []
    for r in range(repeats):  # every repeat-0 run before any repeat-1 run
        block = [{"q": q, "condition": c, "repeat": r} for q in question_ids for c in CONDITIONS]
        random.Random(SEED + r).shuffle(block)
        runs += block
    for i, run in enumerate(runs):
        run["run_id"] = f"n{i:03d}"
    return runs


def cmd_run(work: Path, gold_file: Path, out: Path, repeats: int, model: str, max_turns: int, budget: float,
            only: Optional[List[str]]) -> None:
    work, out = work.resolve(), out.resolve()
    gold = {g["id"]: g for g in json.loads(gold_file.read_text(encoding="utf-8"))}
    raw = out / "raw"
    raw.mkdir(parents=True, exist_ok=True)
    exe = shutil.which("claude") or "claude"
    errors = 0
    for run in plan_runs(sorted(gold), repeats):
        if only and run["q"] not in only:
            continue
        target = raw / f"{run['run_id']}.json"
        if target.exists():
            continue
        cond = run["condition"]
        cwd = work / ("index" if cond == "I" else "plain")
        tools = BASE_TOOLS + ("," + VAULT_TOOLS if cond == "V" else "")
        cmd = [exe, "-p", "--model", model, "--max-turns", str(max_turns), "--max-budget-usd", str(budget),
               "--setting-sources", "project", "--strict-mcp-config", "--allowedTools", tools,
               "--append-system-prompt", BASE_INSTRUCTION + EXTRA[cond],
               "--output-format", "json", "--no-session-persistence"]
        if cond == "V":
            cmd[cmd.index("--strict-mcp-config") + 1:cmd.index("--strict-mcp-config") + 1] = \
                ["--mcp-config", str(work / "mcp_vault.json")]
        cmd.append(gold[run["q"]]["question"])
        proc = subprocess.run(cmd, cwd=cwd, capture_output=True, text=True, encoding="utf-8", errors="replace",
                              env=_child_env(), timeout=1200)
        try:
            data = json.loads(proc.stdout)
        except json.JSONDecodeError:
            data = {"result": "", "is_error": True}
        text = str(data.get("result") or "")
        for root in (work / "index", work / "plain"):
            text = text.replace(str(root), "<REPO>").replace(str(root).replace("\\", "/"), "<REPO>")
        record = {**run, "model": model, "exit_code": proc.returncode, "is_error": bool(data.get("is_error")),
                  "num_turns": data.get("num_turns"), "total_cost_usd": data.get("total_cost_usd"),
                  "usage": {k: (data.get("usage") or {}).get(k) for k in ("input_tokens", "output_tokens",
                            "cache_read_input_tokens", "cache_creation_input_tokens")},
                  "answer": text}
        if proc.returncode != 0 and not text:
            errors += 1
            (out / "errors").mkdir(exist_ok=True)
            record["stderr_tail"] = re.sub(r"(?i)[a-z]:[\\/]+users[\\/]+[^\\/\s\"']+", "<HOME>", proc.stderr[-400:])
            (out / "errors" / f"{run['run_id']}.json").write_text(json.dumps(record, indent=1), encoding="utf-8")
            print(run["run_id"], "PROCESS ERROR", record["stderr_tail"][-160:])
            if errors >= 3:
                raise SystemExit("3 process errors in a row: stopping")
            continue
        errors = 0
        target.write_text(json.dumps(record, indent=1, ensure_ascii=False), encoding="utf-8")
        print(run["run_id"], run["q"], cond, "turns", record["num_turns"], "|", text.strip().splitlines()[-1][:80] if text.strip() else "")


ANSWER_PATH = re.compile(r"(?im)^\W*ANSWER_PATH:\s*`?([^`\s]+)`?")
ANSWER = re.compile(r"(?im)^\W*ANSWER:\s*(.+)$")


def resolve_answer_path(raw: str, uri_to_path: Dict[str, str]) -> str:
    raw = raw.strip().replace("\\", "/")
    raw = re.sub(r"^<REPO>/", "", raw).strip("`'\"<>")
    if raw.startswith("vault://"):
        return uri_to_path.get(raw.split("#")[0].split(" ")[0], raw)
    raw = re.sub(r"[#:].*$", "", raw)
    while raw.startswith("./"):
        raw = raw[2:]
    return raw.lstrip("/")


def grade(record: dict, gold: dict, uri_to_path: Dict[str, str]) -> Dict[str, object]:
    text = record.get("answer") or ""
    pm = ANSWER_PATH.findall(text)
    am = ANSWER.findall(text)
    path = resolve_answer_path(pm[-1], uri_to_path) if pm else None
    path_ok = bool(path) and path.lower() == gold["answer_path"].lower()
    fact_ok = bool(am) and re.search(gold["fact_regex"], am[-1], re.IGNORECASE) is not None
    return {"path": path, "path_ok": path_ok, "fact_ok": fact_ok, "correct": path_ok and fact_ok}


def sign_test_p(wins: int, losses: int) -> float:
    n = wins + losses
    if n == 0:
        return 1.0
    return min(1.0, 2 * sum(math.comb(n, i) for i in range(min(wins, losses) + 1)) / 2 ** n)


def _tokens(r: dict) -> int:
    u = r.get("usage") or {}
    return sum(int(u.get(k) or 0) for k in ("input_tokens", "output_tokens", "cache_read_input_tokens",
                                             "cache_creation_input_tokens"))


def cmd_report(work: Path, gold_file: Path, out: Path) -> None:
    gold = {g["id"]: g for g in json.loads(gold_file.read_text(encoding="utf-8"))}
    routes = json.loads((work.resolve() / "routes.json").read_text(encoding="utf-8"))
    uri_to_path = {r["uri"]: r["path"] for r in routes if "uri" in r}
    recs = [json.loads(p.read_text(encoding="utf-8")) for p in sorted((out / "raw").glob("n*.json"))]
    # only complete repeats count (pre-registered)
    per_repeat = {}
    for r in recs:
        per_repeat.setdefault(r["repeat"], set()).add((r["q"], r["condition"]))
    full = {rep for rep, s in per_repeat.items() if len(s) == len(gold) * len(CONDITIONS)}
    recs = [r for r in recs if r["repeat"] in full]
    for r in recs:
        r.update(grade(r, gold[r["q"]], uri_to_path))
    lines = ["# Navigation benchmark — results", "",
             f"Complete repeats: {sorted(full)}; runs graded: {len(recs)}. Pre-registration: "
             "`07_EVALUATION/navigation_benchmark/PREREGISTRATION.md`.", "",
             "| condition | correct | path only | median tokens | median turns | cost USD |", "|---|---:|---:|---:|---:|---:|"]
    acc, med_tok = {}, {}
    for c in CONDITIONS:
        rs = [r for r in recs if r["condition"] == c]
        n = len(rs) or 1
        acc[c] = sum(r["correct"] for r in rs) / n
        med_tok[c] = statistics.median([_tokens(r) for r in rs]) if rs else float("nan")
        turns = statistics.median([r.get("num_turns") or 0 for r in rs]) if rs else float("nan")
        cost = sum(float(r.get("total_cost_usd") or 0) for r in rs)
        lines.append(f"| {c} | {sum(r['correct'] for r in rs)}/{len(rs)} ({acc[c]:.0%}) | "
                     f"{sum(r['path_ok'] for r in rs)}/{len(rs)} | {med_tok[c]:,.0f} | {turns} | {cost:.2f} |")
    by_diff = {}
    for r in recs:
        by_diff.setdefault((gold[r["q"]].get("difficulty", "?"), r["condition"]), []).append(r["correct"])
    lines += ["", "| difficulty | " + " | ".join(CONDITIONS) + " |", "|---|" + "---:|" * len(CONDITIONS)]
    for d in ("easy", "medium", "hard"):
        cells = []
        for c in CONDITIONS:
            v = by_diff.get((d, c), [])
            cells.append(f"{sum(v)}/{len(v)}" if v else "-")
        lines.append(f"| {d} | " + " | ".join(cells) + " |")
    best = max(("G", "I"), key=lambda c: acc[c])
    wins = losses = 0
    for q in gold:
        mv = [r["correct"] for r in recs if r["q"] == q and r["condition"] == "V"]
        mb = [r["correct"] for r in recs if r["q"] == q and r["condition"] == best]
        if mv and mb:
            a, b = sum(mv) / len(mv), sum(mb) / len(mb)
            wins += a > b
            losses += b > a
    p = sign_test_p(wins, losses)
    lines += ["", f"Better baseline: {best}. Per-question V better / worse: {wins}/{losses}, sign test p = {p:.3f}."]
    diff = acc["V"] - acc[best]
    token_saving = 1 - med_tok["V"] / med_tok[best] if med_tok[best] else float("nan")
    lines.append(f"V - {best}: {diff:+.0%} accuracy; V median tokens {token_saving:+.0%} lower than {best}.")
    if not recs:
        verdict = "no complete repeat"
    elif min(acc.values()) >= 0.95:
        verdict = "benchmark not discriminating (every condition >= 95%)"
    elif (diff >= 0.10 and p <= 0.05) or (diff >= -0.05 and token_saving >= 0.30):
        verdict = "V BEATS the baselines (pre-registered rule)"
    elif diff < -0.05 and token_saving < 0.30:
        verdict = "V is WORSE than the better baseline"
    else:
        verdict = "no demonstrated difference"
    lines += ["", f"**Pre-registered decision: {verdict}.**", ""]
    (out / "RESULTS.md").write_text("\n".join(lines) + "\n", encoding="utf-8")
    (out / "grades.json").write_text(json.dumps([{k: r.get(k) for k in ("run_id", "q", "condition", "repeat", "path",
                                                                         "path_ok", "fact_ok", "correct", "num_turns",
                                                                         "total_cost_usd")} for r in recs], indent=1),
                                     encoding="utf-8")
    print("\n".join(lines))


def main(argv: Optional[List[str]] = None) -> int:
    ap = argparse.ArgumentParser(description=__doc__.split("\n")[0])
    sub = ap.add_subparsers(dest="cmd", required=True)
    p = sub.add_parser("prepare")
    p.add_argument("--work", type=Path, required=True)
    p.add_argument("--ref", default="claude/route-coverage-gate")
    r = sub.add_parser("run")
    r.add_argument("--work", type=Path, required=True)
    r.add_argument("--gold", type=Path, required=True)
    r.add_argument("--out", type=Path, required=True)
    r.add_argument("--repeats", type=int, default=2)
    r.add_argument("--model", default="sonnet")
    r.add_argument("--max-turns", type=int, default=20)
    r.add_argument("--max-budget-usd", type=float, default=1.5)
    r.add_argument("--only", default="")
    s = sub.add_parser("report")
    s.add_argument("--work", type=Path, required=True)
    s.add_argument("--gold", type=Path, required=True)
    s.add_argument("--out", type=Path, required=True)
    a = ap.parse_args(argv)
    if a.cmd == "prepare":
        cmd_prepare(a.work, a.ref)
    elif a.cmd == "run":
        cmd_run(a.work, a.gold, a.out, a.repeats, a.model, a.max_turns, a.max_budget_usd,
                [q for q in a.only.split(",") if q] or None)
    else:
        cmd_report(a.work, a.gold, a.out)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
