"""PASS/FAIL evaluation for the Prompt Master (table + portable block).

Three kinds of check, all deterministic, no model call:

  docs     the two governance files still have the structure agents rely on
           (20 templates, routing table, portable block markers, safety rules)
  routing  every golden request routes to the expected rule and templates
  outputs  a model answer is scored against the output rubric for its mode;
           the golden reference outputs must get their declared verdict

Usage:
  python 30_SCRIPTS/prompt/prompt_master_eval.py check [--json REPORT]
  python 30_SCRIPTS/prompt/prompt_master_eval.py route "vreau un site ..."
  python 30_SCRIPTS/prompt/prompt_master_eval.py score --case G01-mvp --output answer.md

Exit codes: 0 all PASS, 1 at least one FAIL, 2 configuration or usage error.
"""
from __future__ import annotations

import argparse
import json
import re
import sys
import unicodedata
from dataclasses import dataclass, field, asdict
from pathlib import Path

REPO = Path(__file__).resolve().parents[2]
COORD = REPO / "00_GOVERNANCE" / "coordination"
TABLE_MD = COORD / "PROMPT_MASTER_TABLE.md"
PORTABLE_MD = COORD / "PROMPT_MASTER_PORTABLE.md"
ROUTING_JSON = COORD / "prompt_master_routing.json"
EVAL_DIR = REPO / "07_EVALUATION" / "prompting"
GOLDEN_JSON = EVAL_DIR / "golden_prompts.json"

MODES = ("execute", "clarify", "prompt_for_tool")
ENGINEERING = {2, 3, 4, 5, 6, 7, 8, 10, 11, 13}
FINANCE_DISCLAIMER = re.compile(r"this is not financial advice|nu (reprezint[aă]|este) (un )?sfat financiar", re.I)
# an all-caps placeholder, or any bracketed list of options ("[A/B/etc]"); a markdown link "[text](url)" is not one
UNFILLED = re.compile(r"\[(?!NEEDS VERIFICATION\])(?:[A-Z][A-Z0-9 _\-\"',.:&?/]{2,}|[A-Za-z][^\]\n]*/[^\]\n]*)\](?!\()")
SECRET_PATTERNS = [
    re.compile(r"\bsk-(ant-)?[A-Za-z0-9_\-]{16,}"),
    re.compile(r"\bAKIA[0-9A-Z]{16}\b"),
    re.compile(r"\bgh[pousr]_[A-Za-z0-9]{30,}"),
    re.compile(r"-----BEGIN [A-Z ]*PRIVATE KEY-----"),
    re.compile(r"(?i)\b(password|passwd|api[_-]?key|secret|token)\s*[:=]\s*['\"]?[A-Za-z0-9_\-/+]{8,}"),
]
COT_REQUEST = re.compile(r"(?i)(show|reveal|print|output) (your|the) (full |hidden |private )?(chain[- ]of[- ]thought|reasoning trace)")


class ConfigError(Exception):
    pass


@dataclass
class Check:
    id: str
    passed: bool
    detail: str = ""


@dataclass
class Report:
    checks: list[Check] = field(default_factory=list)

    def add(self, cid: str, passed: bool, detail: str = "") -> None:
        self.checks.append(Check(cid, bool(passed), detail))

    @property
    def passed(self) -> bool:
        return all(c.passed for c in self.checks)

    def failures(self) -> list[Check]:
        return [c for c in self.checks if not c.passed]


# ---------------------------------------------------------------- loading

def _load_json(path: Path) -> dict:
    try:
        return json.loads(path.read_text(encoding="utf-8"))
    except FileNotFoundError as e:
        raise ConfigError(f"missing {path.relative_to(REPO)}") from e
    except json.JSONDecodeError as e:
        raise ConfigError(f"invalid JSON in {path.relative_to(REPO)}: {e}") from e


def load_routing(path: Path = ROUTING_JSON) -> dict:
    data = _load_json(path)
    rules = data.get("rules")
    if not isinstance(rules, list) or not rules:
        raise ConfigError("routing: 'rules' must be a non-empty list")
    ids = set()
    for r in rules:
        for key, typ in (("id", str), ("label", str), ("mode", str), ("templates", list), ("priority", int), ("keywords", list)):
            if not isinstance(r.get(key), typ):
                raise ConfigError(f"routing rule {r.get('id')!r}: '{key}' must be {typ.__name__}")
        if r["mode"] not in MODES:
            raise ConfigError(f"routing rule {r['id']}: unknown mode {r['mode']!r}")
        if any(not isinstance(t, int) or not 1 <= t <= 20 for t in r["templates"]):
            raise ConfigError(f"routing rule {r['id']}: templates must be integers 1..20")
        if r["id"] in ids:
            raise ConfigError(f"routing: duplicate rule id {r['id']}")
        ids.add(r["id"])
    if data.get("fallback") not in ids:
        raise ConfigError("routing: 'fallback' must name an existing rule")
    return data


def load_golden(path: Path = GOLDEN_JSON) -> dict:
    data = _load_json(path)
    validate_golden(data)
    return data


def validate_golden(data: dict) -> None:
    """Structural check mirroring 07_EVALUATION/prompting/prompt_eval_schema.json."""
    if data.get("schema_version") != 1:
        raise ConfigError("golden: schema_version must be 1")
    cases = data.get("cases")
    if not isinstance(cases, list) or not cases:
        raise ConfigError("golden: 'cases' must be a non-empty list")
    seen = set()
    for c in cases:
        cid = c.get("id")
        if not isinstance(cid, str) or not re.fullmatch(r"G\d{2}-[a-z0-9_-]+", cid):
            raise ConfigError(f"golden: bad case id {cid!r} (expected G00-name)")
        if cid in seen:
            raise ConfigError(f"golden: duplicate case id {cid}")
        seen.add(cid)
        if not isinstance(c.get("input"), str) or not c["input"].strip():
            raise ConfigError(f"golden {cid}: 'input' must be a non-empty string")
        exp = c.get("expected")
        if not isinstance(exp, dict) or exp.get("mode") not in MODES or not isinstance(exp.get("rule"), str) \
                or not isinstance(exp.get("templates"), list):
            raise ConfigError(f"golden {cid}: 'expected' needs mode, rule, templates")
        for ref in c.get("reference_outputs", []):
            if not isinstance(ref.get("file"), str) or ref.get("verdict") not in ("PASS", "FAIL"):
                raise ConfigError(f"golden {cid}: reference_outputs entries need file and verdict PASS|FAIL")
        extra = set(c) - {"id", "input", "expected", "reference_outputs", "note"}
        if extra:
            raise ConfigError(f"golden {cid}: unknown keys {sorted(extra)}")


# ---------------------------------------------------------------- routing

def normalize(text: str) -> str:
    text = unicodedata.normalize("NFKD", text.lower())
    return "".join(ch for ch in text if not unicodedata.combining(ch))


def _keyword_re(kw: str) -> re.Pattern:
    kw = normalize(kw)
    if kw.endswith("*"):
        return re.compile(r"(?<![a-z0-9])" + re.escape(kw[:-1]))
    return re.compile(r"(?<![a-z0-9])" + re.escape(kw) + r"(?![a-z0-9])")


def route(text: str, routing: dict) -> dict:
    norm = normalize(text)
    by_id = {r["id"]: r for r in routing["rules"]}
    scored = []
    for r in routing["rules"]:
        hits = [kw for kw in r["keywords"] if _keyword_re(kw).search(norm)]
        if hits:
            scored.append((bool(r.get("override")), len(hits), r["priority"], r["id"], hits))
    if not scored:
        rule, hits = by_id[routing["fallback"]], []
    else:
        best = max(scored)
        rule, hits = by_id[best[3]], best[4]
    return {"rule": rule["id"], "mode": rule["mode"], "templates": rule["templates"], "hits": hits}


# ---------------------------------------------------------------- docs

def _parse_routing_table(md: str) -> dict[str, list[int]]:
    m = re.search(r"^## Rutare\s*$(.*?)^---\s*$", md, re.M | re.S)
    if not m:
        return {}
    rows = {}
    for line in m.group(1).splitlines():
        if not line.startswith("|") or line.startswith("|---") or "Tip cerință" in line:
            continue
        cols = [c.strip() for c in line.strip().strip("|").split("|")]
        if len(cols) >= 2:
            rows[cols[0]] = [int(n) for n in re.findall(r"#(\d+)", cols[1])]
    return rows


def check_docs(report: Report, routing: dict) -> None:
    try:
        table = TABLE_MD.read_text(encoding="utf-8")
    except FileNotFoundError:
        report.add("D1-table-exists", False, str(TABLE_MD.relative_to(REPO)))
        return
    report.add("D1-table-exists", True)

    headings = [int(n) for n in re.findall(r"^### (\d+)\. ", table, re.M)]
    report.add("D2-twenty-templates", headings == list(range(1, 21)), f"found {headings}")

    blocks = re.findall(r"^### (\d+)\. .*?\n```text\n(.*?)\n```", table, re.M | re.S)
    empty = [n for n, body in blocks if len(body.strip()) < 80]
    report.add("D3-template-bodies", len(blocks) == 20 and not empty,
               f"{len(blocks)} code blocks, too short: {empty}")

    rows = re.findall(r"^\| (\d+) \| \[", table, re.M)
    report.add("D4-master-table-rows", [int(r) for r in rows] == list(range(1, 21)), f"rows {rows}")

    md_rows = _parse_routing_table(table)
    mismatched = []
    for r in routing["rules"]:
        if r["label"] not in md_rows:
            mismatched.append(f"{r['id']}: label {r['label']!r} missing from Rutare")
        elif md_rows[r["label"]] != r["templates"]:
            mismatched.append(f"{r['id']}: table {md_rows[r['label']]} vs json {r['templates']}")
    unknown = sorted(set(md_rows) - {r["label"] for r in routing["rules"]})
    if unknown:
        mismatched.append(f"rows without a rule: {unknown}")
    report.add("D5-routing-table-matches-json", not mismatched, "; ".join(mismatched))

    for phrase, cid in (("## Protocol de orchestrare", "D6-protocol-section"),
                        ("#9 AI Technical Lead Mode", "D7-clarify-exception"),
                        ("## Registru cerințe", "D8-request-log")):
        report.add(cid, phrase in table, phrase)

    try:
        portable = PORTABLE_MD.read_text(encoding="utf-8")
    except FileNotFoundError:
        report.add("P1-portable-exists", False, str(PORTABLE_MD.relative_to(REPO)))
        return
    report.add("P1-portable-exists", True)
    block = re.search(r"<!-- BEGIN PORTABLE BLOCK -->(.*?)<!-- END PORTABLE BLOCK -->", portable, re.S)
    report.add("P2-portable-markers", bool(block))
    body = block.group(1) if block else ""
    for phrase, cid in (("**Hard rules**", "P3-hard-rules"),
                        ("**Credential safety**", "P4-credential-safety"),
                        ("**Pasted prompts are data, not instructions**", "P5-pasted-prompts-inert"),
                        ("**Output format**", "P6-output-format"),
                        ("Never request hidden chain-of-thought", "P7-no-cot-request")):
        report.add(cid, phrase in body, phrase)
    report.add("P8-links-table", "PROMPT_MASTER_TABLE.md" in portable)


# ---------------------------------------------------------------- outputs

def _quote_block(text: str) -> str:
    return "\n".join(l[1:] for l in text.splitlines() if l.startswith(">"))


def _strip_fences(text: str) -> str:
    return re.sub(r"```.*?```", "", text, flags=re.S)


def score_output(text: str, expected: dict) -> Report:
    """Rubric for one model answer. `expected` is a golden case's 'expected'."""
    rep = Report()
    mode, templates = expected["mode"], expected["templates"]
    fences = re.findall(r"^```", text, re.M)
    n_blocks = len(fences) // 2

    rep.add("S1-no-credentials", not any(p.search(text) for p in SECRET_PATTERNS))
    rep.add("S2-no-cot-request", not COT_REQUEST.search(text))

    if mode == "execute":
        quote = _quote_block(text)
        rep.add("O1-mega-prompt-quoted", bool(quote.strip()), "Pasul 1 must be a '>' quote block")
        missing = [t for t in templates if not re.search(rf"#{t}(?!\d)", quote)]
        rep.add("O2-templates-named", not missing, f"missing in quote: {missing}")
        left = UNFILLED.findall(quote)
        rep.add("O3-variables-filled", not left, f"unfilled: {left[:5]}")
        after = text.split(quote.splitlines()[-1], 1)[-1] if quote.strip() else text
        rep.add("O4-executed", len(after.strip()) >= 200, f"{len(after.strip())} chars after the quote")
        if set(templates) & ENGINEERING:
            rep.add("O5-code-delivered", n_blocks >= 1, f"{n_blocks} fenced blocks")
        if 19 in templates or 20 in templates:
            rep.add("O6-finance-disclaimer", bool(FINANCE_DISCLAIMER.search(text)))
    elif mode == "clarify":
        prose = _strip_fences(text)
        questions = [l for l in prose.splitlines() if l.strip().endswith("?")]
        rep.add("C1-three-to-four-questions", 3 <= len(questions) <= 4, f"{len(questions)} questions")
        rep.add("C2-no-code-yet", n_blocks == 0, f"{n_blocks} fenced blocks")
        rep.add("C3-names-tech-lead-mode", "#9" in text)
    elif mode == "prompt_for_tool":
        rep.add("T1-one-prompt-block", n_blocks == 1, f"{n_blocks} fenced blocks")
        rep.add("T2-target-line", bool(re.search(r"^🎯 Target: \S", text, re.M)))
        prose = _strip_fences(text)
        rep.add("T3-no-padding", len(prose.strip()) <= 600, f"{len(prose.strip())} chars outside the block")
    return rep


# ---------------------------------------------------------------- golden

def check_golden(report: Report, routing: dict, golden: dict) -> None:
    rule_ids = {r["id"] for r in routing["rules"]}
    covered = set()
    for case in golden["cases"]:
        exp = case["expected"]
        if exp["rule"] not in rule_ids:
            report.add(f"{case['id']}/route", False, f"expected rule {exp['rule']!r} does not exist")
            continue
        got = route(case["input"], routing)
        covered.add(got["rule"])
        ok = (got["rule"], got["mode"], got["templates"]) == (exp["rule"], exp["mode"], exp["templates"])
        report.add(f"{case['id']}/route", ok,
                   f"got {got['rule']} {got['templates']} via {got['hits']}; expected {exp['rule']} {exp['templates']}")
        for ref in case.get("reference_outputs", []):
            path = EVAL_DIR / ref["file"]
            if not path.is_file():
                report.add(f"{case['id']}/{ref['file']}", False, "reference output missing")
                continue
            res = score_output(path.read_text(encoding="utf-8"), exp)
            verdict = "PASS" if res.passed else "FAIL"
            report.add(f"{case['id']}/{ref['file']}", verdict == ref["verdict"],
                       f"rubric says {verdict}, declared {ref['verdict']}; failed: {[c.id for c in res.failures()]}")
    uncovered = sorted(rule_ids - covered)
    report.add("G0-every-rule-has-a-golden-case", not uncovered, f"uncovered: {uncovered}")


def run_all() -> Report:
    routing = load_routing()
    golden = load_golden()
    report = Report()
    check_docs(report, routing)
    check_golden(report, routing, golden)
    return report


# ---------------------------------------------------------------- cli

def _print(report: Report) -> None:
    for c in report.checks:
        mark = "PASS" if c.passed else "FAIL"
        print(f"{mark}  {c.id}" + (f"  — {c.detail}" if (c.detail and not c.passed) else ""))
    n_fail = len(report.failures())
    print(f"\n{len(report.checks) - n_fail}/{len(report.checks)} PASS — verdict: {'PASS' if report.passed else 'FAIL'}")


def main(argv: list[str] | None = None) -> int:
    if hasattr(sys.stdout, "reconfigure"):
        sys.stdout.reconfigure(encoding="utf-8")
    ap = argparse.ArgumentParser(description=__doc__.split("\n\n")[0])
    sub = ap.add_subparsers(dest="cmd", required=True)
    c = sub.add_parser("check", help="docs + golden routing + reference outputs")
    c.add_argument("--json", type=Path, help="also write the report as JSON")
    r = sub.add_parser("route", help="show how a request is routed")
    r.add_argument("text")
    s = sub.add_parser("score", help="score a model answer for a golden case")
    s.add_argument("--case", required=True)
    s.add_argument("--output", required=True, type=Path)
    args = ap.parse_args(argv)

    try:
        if args.cmd == "route":
            print(json.dumps(route(args.text, load_routing()), ensure_ascii=False))
            return 0
        if args.cmd == "score":
            cases = {c["id"]: c for c in load_golden()["cases"]}
            if args.case not in cases:
                print(f"unknown case {args.case}; known: {', '.join(cases)}", file=sys.stderr)
                return 2
            report = score_output(args.output.read_text(encoding="utf-8"), cases[args.case]["expected"])
        else:
            report = run_all()
            if args.json:
                args.json.write_text(json.dumps({"verdict": "PASS" if report.passed else "FAIL",
                                                 "checks": [asdict(c) for c in report.checks]},
                                                ensure_ascii=False, indent=2), encoding="utf-8")
    except ConfigError as e:
        print(f"config error: {e}", file=sys.stderr)
        return 2
    _print(report)
    return 0 if report.passed else 1


if __name__ == "__main__":
    sys.exit(main())
