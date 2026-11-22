#!/usr/bin/env python3
"""Compile the specialist registry for the cost-router skill (M5).

Run by install.py (or by hand), NEVER inside a hook. Stdlib only, no network.

Sources (fixed roots, nothing else is scanned):
  1. <skills_dir>/*/route.meta.json   the installed user-scope skills (dirs whose name contains `.bak-` are skipped)
  2. <S>/registry_src/skills/*/route.meta.json   sidecars shipped with the skill (copied next to the skills later)
  3. <S>/roles/*.json                 one sidecar per agent
Sidecars are never frontmatter: the consultant validator needs an exact frontmatter key set.

Output: <S>/registry.json, deterministic (sorted keys, stable order), <= MAX_BYTES, with the sha256 of every
sidecar and of every artifact (SKILL.md / agent .md). A duplicate id is an error (two byte-identical copies of the
same sidecar, e.g. the registry_src one and the installed one, count as one). An unknown key anywhere is an error.

Schema v1 (all mandatory unless marked): schema_version, id, kind, domain, task_classes, triggers{ro,en}, summary,
run_in, subagent_type (iff run_in == subagent), tier{min,default,max}, tools, authority{write,execute,network,spawn},
acceptance, risk_ceiling, privacy, priority. Optional: not_for, uses, effort, escalates_to, cost{class,typical_usd}.
`approve` and `merge` are not fields: they are always false and a sidecar cannot name them.
"""
from __future__ import annotations

import hashlib
import json
import re
import sys
import unicodedata
from pathlib import Path

SCHEMA = 1
MAX_BYTES = 8192
BAK_MARK = ".bak-"
POLICY_CLASSES = ("explore", "summarize", "implement", "review", "design", "frontier")
KINDS = ("agent", "skill", "overlay", "external")
DOMAINS = ("code", "security", "legal", "research", "media-3d", "media-video", "prompts", "dfir", "docs", "data", "ops")
TIERS = ("local", "haiku", "sonnet", "opus", "fable")
RISKS = ("low", "medium", "high", "critical")
ID_RE = re.compile(r"^[a-z0-9-]{1,40}$")
TRIGGER_BAD = set("\\^$*+?()[]{}|<>")  # literal phrases only, no regex
MANDATORY = ("schema_version", "id", "kind", "domain", "task_classes", "triggers", "summary", "run_in", "tier", "tools",
             "authority", "acceptance", "risk_ceiling", "privacy", "priority")
OPTIONAL = ("subagent_type", "not_for", "uses", "effort", "escalates_to", "cost")


class RegistryError(ValueError):
    pass


def fold(text: str) -> str:
    """casefold + strip diacritics: 'Șoferul' and 'soferul' compare equal."""
    s = unicodedata.normalize("NFKD", str(text).casefold())
    return unicodedata.normalize("NFKD", "".join(c for c in s if not unicodedata.combining(c))).casefold()


def _sha(path: Path, lf: bool = False) -> str:
    data = path.read_bytes()
    if lf:
        data = data.replace(b"\r\n", b"\n").replace(b"\r", b"\n")
    return hashlib.sha256(data).hexdigest()


def _is_str(v, n: int, lo: int = 1) -> bool:
    return isinstance(v, str) and lo <= len(v) <= n


def _keys(obj, allowed, where: str, required=()) -> None:
    if not isinstance(obj, dict):
        raise RegistryError(f"{where}: must be an object")
    unknown = sorted(set(obj) - set(allowed))
    if unknown:
        raise RegistryError(f"{where}: unknown key(s) {unknown}")
    missing = [k for k in required if k not in obj]
    if missing:
        raise RegistryError(f"{where}: missing key(s) {missing}")


def _list_of(obj, key, where, maxlen, enum=None, item_max=60) -> list:
    v = obj.get(key)
    if not isinstance(v, list) or len(v) > maxlen or any(not _is_str(x, item_max) for x in v):
        raise RegistryError(f"{where}.{key}: must be a list of <= {maxlen} short strings")
    if enum is not None and any(x not in enum for x in v):
        raise RegistryError(f"{where}.{key}: values must be in {list(enum)}")
    if len(set(v)) != len(v):
        raise RegistryError(f"{where}.{key}: duplicate values")
    return v


def validate(meta: dict, where: str) -> dict:
    """Validate one sidecar against schema v1 and return the normalized entry (triggers folded). Raises RegistryError."""
    _keys(meta, MANDATORY + OPTIONAL, where, MANDATORY)
    if type(meta["schema_version"]) is not int or meta["schema_version"] != SCHEMA:
        raise RegistryError(f"{where}: schema_version must be {SCHEMA}")
    if not isinstance(meta["id"], str) or not ID_RE.match(meta["id"]):
        raise RegistryError(f"{where}: id must match [a-z0-9-]{{1,40}}")
    if meta["kind"] not in KINDS:
        raise RegistryError(f"{where}: kind must be one of {list(KINDS)}")
    dom = _list_of(meta, "domain", where, len(DOMAINS), DOMAINS)
    cls = _list_of(meta, "task_classes", where, len(POLICY_CLASSES), POLICY_CLASSES)
    if not dom or not cls:
        raise RegistryError(f"{where}: domain and task_classes must not be empty")
    trig = meta["triggers"]
    _keys(trig, ("ro", "en"), where + ".triggers", ("ro", "en"))
    folded: list[str] = []
    for lang in ("ro", "en"):
        phrases = trig[lang]
        if not isinstance(phrases, list) or len(phrases) > 24 or any(not _is_str(p, 40) for p in phrases):
            raise RegistryError(f"{where}.triggers.{lang}: list of <= 24 phrases of <= 40 chars")
        for p in phrases:
            if any(c in TRIGGER_BAD for c in p):
                raise RegistryError(f"{where}.triggers.{lang}: {p!r} looks like a pattern; literal phrases only")
            f = " ".join(fold(p).split())
            if len(f) < 2:
                raise RegistryError(f"{where}.triggers.{lang}: {p!r} is too short")
            folded.append(f)
    if not folded:
        raise RegistryError(f"{where}.triggers: at least one phrase")
    if not _is_str(meta["summary"], 160):
        raise RegistryError(f"{where}: summary must be 1..160 chars")
    if meta["run_in"] not in ("main", "subagent"):
        raise RegistryError(f"{where}: run_in must be main or subagent")
    if meta["run_in"] == "subagent":
        if not _is_str(meta.get("subagent_type"), 60):
            raise RegistryError(f"{where}: subagent_type is required when run_in is subagent")
    elif "subagent_type" in meta:
        raise RegistryError(f"{where}: subagent_type only applies to run_in subagent")
    tier = meta["tier"]
    _keys(tier, ("min", "default", "max"), where + ".tier", ("min", "default", "max"))
    if any(tier[k] not in TIERS for k in tier):
        raise RegistryError(f"{where}.tier: values must be in {list(TIERS)}")
    r = [TIERS.index(tier[k]) for k in ("min", "default", "max")]
    if not r[0] <= r[1] <= r[2]:
        raise RegistryError(f"{where}.tier: need min <= default <= max")
    _list_of(meta, "tools", where, 40)
    auth = meta["authority"]
    _keys(auth, ("write", "execute", "network", "spawn"), where + ".authority", ("write", "execute", "network", "spawn"))
    if auth["write"] not in ("none", "scratch", "tests", "workspace") or auth["network"] not in ("none", "fetch", "bridge"):
        raise RegistryError(f"{where}.authority: bad write/network value")
    if type(auth["execute"]) is not bool or type(auth["spawn"]) is not bool:
        raise RegistryError(f"{where}.authority: execute and spawn must be booleans")
    if not _is_str(meta["acceptance"], 200):
        raise RegistryError(f"{where}: acceptance must be 1..200 chars")
    if meta["risk_ceiling"] not in RISKS:
        raise RegistryError(f"{where}: risk_ceiling must be one of {list(RISKS)}")
    if meta["privacy"] not in ("standard", "local_only"):
        raise RegistryError(f"{where}: privacy must be standard or local_only")
    if type(meta["priority"]) is not int:
        raise RegistryError(f"{where}: priority must be an int")
    if "not_for" in meta:
        _list_of(meta, "not_for", where, 12, item_max=60)
    if "uses" in meta:
        for u in _list_of(meta, "uses", where, 12, item_max=40):
            if not ID_RE.match(u):
                raise RegistryError(f"{where}.uses: bad id {u!r}")
    if "effort" in meta and meta["effort"] not in ("low", "medium", "high", "xhigh"):
        raise RegistryError(f"{where}: effort must be low|medium|high|xhigh")
    if "escalates_to" in meta and not (isinstance(meta["escalates_to"], str) and ID_RE.match(meta["escalates_to"])):
        raise RegistryError(f"{where}: escalates_to must be an id")
    if "cost" in meta:
        c = meta["cost"]
        _keys(c, ("class", "typical_usd"), where + ".cost", ("class",))
        usd = c.get("typical_usd")
        if c["class"] not in ("S", "M", "L") or not (usd is None or (type(usd) in (int, float) and usd >= 0)):
            raise RegistryError(f"{where}.cost: class S|M|L and typical_usd number or null")
    entry = {k: v for k, v in meta.items() if k not in ("schema_version", "triggers")}
    entry["triggers"] = sorted(set(folded))
    return entry


def parse_frontmatter(path: Path) -> dict:
    lines = path.read_text(encoding="utf-8-sig").splitlines()
    out: dict[str, str] = {}
    if not lines or lines[0].strip() != "---":
        return out
    for line in lines[1:]:
        if line.strip() == "---":
            break
        m = re.match(r"^([A-Za-z_][\w-]*):\s*(.*)$", line)
        if m:
            out[m.group(1)] = m.group(2).strip()
    return out


def _model_tier(model: str) -> str | None:
    m = model.strip().lower()
    for t in ("haiku", "sonnet", "opus", "fable"):
        if t in m:
            return t
    return None


def _read_json(path: Path, where: str) -> dict:
    try:
        return json.loads(path.read_text(encoding="utf-8-sig"))
    except (OSError, ValueError) as exc:
        raise RegistryError(f"{where}: cannot read JSON ({exc})") from exc


def _dirs(root: Path):
    if not root.is_dir():
        return []
    return sorted(p for p in root.iterdir() if p.is_dir() and not p.is_symlink() and BAK_MARK not in p.name)


AUTH_DEFAULT = {"write": "none", "execute": False, "network": "none", "spawn": False}


def compact(e: dict) -> dict:
    """The selection view that goes into registry.json (8 KB budget). Dropped on purpose: summary, run_in (subagent_type present means subagent), domain, tools, uses, not_for,
    effort, escalates_to, cost (validated at compile time, read from the sidecar when needed). Authority lists only the
    non-default fields; privacy only when local_only. Nothing here can grant more than the sidecar says."""
    out = {"id": e["id"], "kind": e["kind"], "task_classes": e["task_classes"],
           "priority": e["priority"], "tier": [e["tier"]["min"], e["tier"]["default"], e["tier"]["max"]],
           "authority": {k: v for k, v in e["authority"].items() if v != AUTH_DEFAULT[k]},
           "risk_ceiling": e["risk_ceiling"], "triggers": e["triggers"],
           "acceptance": e["acceptance"], "src_sha256": e["src_sha256"], "artifact_sha256": e["artifact_sha256"]}
    if "subagent_type" in e:
        out["subagent_type"] = e["subagent_type"]
    if e["privacy"] != "standard":
        out["privacy"] = e["privacy"]
    return out


def compile_registry(s_dir: Path, skills_dir: Path | None = None, agents_dir: Path | None = None,
                     max_bytes: int = MAX_BYTES) -> tuple[dict, bytes]:
    """Scan the fixed roots and return (registry, bytes). `s_dir` is the cost-router directory (source or installed);
    `skills_dir` defaults to ~/.claude/skills, `agents_dir` to the `agents` directory two levels above `s_dir`
    (<repo>/.claude/agents or ~/.claude/agents)."""
    s_dir = Path(s_dir).resolve()
    skills_dir = Path(skills_dir) if skills_dir is not None else Path.home() / ".claude" / "skills"
    agents_dir = Path(agents_dir) if agents_dir is not None else s_dir.parents[1] / "agents"
    found: dict[str, dict] = {}

    def add(meta_path: Path, artifact: Path | None, name: str, role: bool) -> None:
        where = str(meta_path.name if role else f"{name}/route.meta.json")
        raw = _read_json(meta_path, where)
        entry = validate(raw, where)
        if entry["id"] != name:
            raise RegistryError(f"{where}: id {entry['id']!r} must equal the artifact name {name!r}")
        if role:
            if entry["kind"] != "agent" or entry["run_in"] != "subagent" or str(entry.get("subagent_type")).lower() != name:
                raise RegistryError(f"{where}: a role sidecar is kind agent, run_in subagent, subagent_type == id (case-insensitive)")
            # ids are lowercase; an agent file may be capitalised (Explore.md): match case-insensitively
            cands = sorted(p for p in agents_dir.glob("*.md") if p.stem.lower() == name) if agents_dir.is_dir() else []
            artifact = cands[0] if cands else None
            if artifact is not None:
                fm = parse_frontmatter(artifact)
                tools = sorted(t.strip() for t in fm.get("tools", "").split(",") if t.strip())
                if tools != sorted(entry["tools"]):
                    raise RegistryError(f"{where}: tools {sorted(entry['tools'])} != agent file tools {tools}")
                mt = _model_tier(fm.get("model", ""))
                if mt is not None and mt != entry["tier"]["default"]:
                    raise RegistryError(f"{where}: tier.default {entry['tier']['default']} != agent model {fm.get('model')}")
        elif entry["kind"] == "agent":
            raise RegistryError(f"{where}: kind agent belongs in roles/")
        src_sha = _sha(meta_path, lf=True)
        entry["src_sha256"] = src_sha
        entry["artifact_sha256"] = _sha(artifact, lf=True) if artifact is not None and artifact.is_file() else None
        if name in found:
            if found[name]["src_sha256"] != src_sha:
                raise RegistryError(f"duplicate id {name!r}: two different sidecars")
            if found[name]["artifact_sha256"] is None:
                found[name]["artifact_sha256"] = entry["artifact_sha256"]
            return
        found[name] = entry

    for root in (skills_dir, s_dir / "registry_src" / "skills"):
        for d in _dirs(root):
            meta = d / "route.meta.json"
            if meta.is_file():
                art = d / "SKILL.md"
                if not art.is_file() and d.name == s_dir.name:
                    art = s_dir / "SKILL.md"
                if not art.is_file() and d.name == "cost-router":
                    art = s_dir / "SKILL.md"
                if not art.is_file():  # a registry_src sidecar: the artifact is the installed skill, when there is one
                    art = skills_dir / d.name / "SKILL.md"
                add(meta, art if art.is_file() else None, d.name, role=False)
    roles = s_dir / "roles"
    if roles.is_dir():
        for p in sorted(roles.glob("*.json")):
            if BAK_MARK in p.name or p.is_symlink():
                continue
            add(p, None, p.stem, role=True)
    for e in found.values():
        for ref in [*e.get("uses", []), *([e["escalates_to"]] if "escalates_to" in e else [])]:
            if ref not in found:
                raise RegistryError(f"{e['id']}: references unknown id {ref!r}")
    reg = {"schema_version": SCHEMA, "entries": [compact(found[k]) for k in sorted(found)]}
    data = (json.dumps(reg, sort_keys=True, separators=(",", ":"), ensure_ascii=True) + "\n").encode("ascii")
    if len(data) > max_bytes:
        raise RegistryError(f"registry is {len(data)} bytes, cap {max_bytes}")
    return reg, data


def write_registry(s_dir: Path, skills_dir: Path | None = None, agents_dir: Path | None = None,
                   out: Path | None = None, max_bytes: int = MAX_BYTES) -> Path:
    _, data = compile_registry(s_dir, skills_dir, agents_dir, max_bytes)
    out = Path(out) if out is not None else Path(s_dir) / "registry.json"
    tmp = out.with_name(out.name + ".tmp")
    tmp.write_bytes(data)
    tmp.replace(out)
    return out


def main(argv=None) -> int:
    import argparse
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("--s-dir", default=str(Path(__file__).resolve().parent.parent))
    ap.add_argument("--skills-dir")
    ap.add_argument("--agents-dir")
    ap.add_argument("--out")
    ap.add_argument("--check", action="store_true", help="compile and compare with the existing file; write nothing")
    args = ap.parse_args(argv)
    try:
        s_dir = Path(args.s_dir)
        if args.check:
            _, data = compile_registry(s_dir, args.skills_dir, args.agents_dir)
            cur = Path(args.out) if args.out else s_dir / "registry.json"
            ok = cur.is_file() and cur.read_bytes() == data
            print("registry current" if ok else "registry stale or missing")
            return 0 if ok else 1
        out = write_registry(s_dir, args.skills_dir, args.agents_dir, args.out)
    except RegistryError as exc:
        print(f"registry_compile: {exc}", file=sys.stderr)
        return 2
    print(f"wrote {out} ({out.stat().st_size} bytes)")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
