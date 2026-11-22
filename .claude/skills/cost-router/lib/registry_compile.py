#!/usr/bin/env python3
"""Compile the specialist registry for the cost-router skill (M5).

Run by install.py (or by hand), NEVER inside a hook. Stdlib only, no network.

Sources (fixed roots, nothing else is scanned). Trust split:
  TRUSTED (pinned with the release; any error is fatal):
    <S>/registry_src/skills/*/route.meta.json   sidecars shipped with the skill (copied next to the skills later)
    <S>/roles/*.json                            one sidecar per agent
  UNTRUSTED (anyone can drop a file there; a bad one is skipped and reported, never fatal; hint-only and clamped:
  tier <= sonnet, no authority, priority <= 10, no acceptance text, cannot claim a trusted id or a privileged agent):
    <skills_dir>/*/route.meta.json   user-scope skills not shipped here (dirs whose name contains `.bak-` are skipped)
Sidecars are never frontmatter: the consultant validator needs an exact frontmatter key set.

Output: <S>/registry.json, deterministic (sorted keys, stable order), <= MAX_BYTES, and <S>/registry.hashes.json with
the sha256 of every sidecar and of every artifact (SKILL.md / agent .md). A duplicate id is an error (two byte-identical copies of the
same sidecar, e.g. the registry_src one and the installed one, count as one). An unknown key anywhere is an error.

Schema v1 (all mandatory unless marked): schema_version, id, kind, domain, task_classes, triggers{ro,en}, summary,
run_in, subagent_type (iff run_in == subagent), tier{min,default,max}, tools, authority{write,execute,network,spawn},
acceptance, risk_ceiling, privacy, priority. Optional: not_for, uses, effort, escalates_to, cost{class,typical_usd},
selectable (only `false`, trusted only, with empty triggers: never selected by text, e.g. the gated fable-executor).
`approve` and `merge` are not fields: they are always false and a sidecar cannot name them.
"""
from __future__ import annotations

import hashlib
import json
import re
import sys
import unicodedata
from pathlib import Path
from typing import NamedTuple

SCHEMA = 1
MAX_BYTES = 16384
UNTRUSTED_META_MAX = 4096  # bytes: a user-scope sidecar larger than this is skipped, never read
UNTRUSTED_MAX_ENTRIES = 32
UNTRUSTED_PRIORITY_CAP = 10
TRIGGER_MIN = 4  # characters after folding
# Common RO + EN words: a trigger made only of these would match almost every prompt.
STOPWORDS = frozenset(w for w in """
the and for with from this that these those have has had are was were been will would can could should may might
not but you your our their them they its into onto over under about after before then than also just very more most
some any all each every whom whose here there out off per via
de si sa la pe cu un o ca ce nu ma te se am ai au al ale ar fi fie din dar sau mai mult foarte dupa fara spre
pentru sunt era erau fost fiind acest aceasta aceste acesta acei aceea ei ea el ele noi voi eu tu mea
tau lui lor iar deci tot toate toti orice fiecare
""".split())
PRIVILEGED_IDS = frozenset({"fable-consultant", "fable-executor", "vault-reviewer"})
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
OPTIONAL = ("subagent_type", "not_for", "uses", "effort", "escalates_to", "cost", "selectable")


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


def _trigger_problem(folded_phrase: str) -> str:
    if len(folded_phrase) < TRIGGER_MIN:
        return f"too short (< {TRIGGER_MIN} chars)"
    words = folded_phrase.split()
    if all(w in STOPWORDS for w in words):
        return "only common stopwords"
    return ""


def validate(meta: dict, where: str, untrusted: bool = False) -> dict:
    """Validate one sidecar against schema v1 and return the normalized entry (triggers folded). Raises RegistryError.
    With `untrusted`, a bad trigger (too short, stopwords only, pattern-like) is dropped instead of failing; the entry
    still fails when no trigger is left."""
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
                if untrusted:
                    continue
                raise RegistryError(f"{where}.triggers.{lang}: {p!r} looks like a pattern; literal phrases only")
            f = " ".join(fold(p).split())
            problem = _trigger_problem(f)
            if problem:
                if untrusted:
                    continue
                raise RegistryError(f"{where}.triggers.{lang}: {p!r} is {problem}")
            folded.append(f)
    if "selectable" in meta:
        # selectable=false (trusted sidecars only): an entry the text can never select (the gated fable-executor);
        # it carries no trigger at all
        if meta["selectable"] is not False:
            raise RegistryError(f"{where}: selectable may only be false (omit it for a selectable entry)")
        if untrusted:
            raise RegistryError(f"{where}: selectable is not allowed in a user-scope sidecar")
        if trig["ro"] or trig["en"]:
            raise RegistryError(f"{where}: a non-selectable entry has no triggers")
    elif not folded:
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
HASHES_NAME = "registry.hashes.json"


def compact(e: dict) -> dict:
    """The selection view that goes into registry.json (16 KB budget). Dropped on purpose: summary, run_in (subagent_type present means subagent), domain, tools, uses, not_for,
    effort, escalates_to, cost (validated at compile time, read from the sidecar when needed) and the sha256 of every
    sidecar/artifact (those live in registry.hashes.json, checked by --verify). Authority lists only the non-default
    fields; privacy only when local_only. An untrusted (user-scope) entry carries `untrusted` and no acceptance text.
    Nothing here can grant more than the sidecar says."""
    out = {"id": e["id"], "kind": e["kind"], "task_classes": e["task_classes"],
           "priority": e["priority"], "tier": [e["tier"]["min"], e["tier"]["default"], e["tier"]["max"]],
           "authority": {k: v for k, v in e["authority"].items() if v != AUTH_DEFAULT[k]},
           "risk_ceiling": e["risk_ceiling"], "triggers": e["triggers"]}
    if e.get("untrusted"):
        out["untrusted"] = True
    else:
        out["acceptance"] = e["acceptance"]
    if "subagent_type" in e:
        out["subagent_type"] = e["subagent_type"]
    if e["privacy"] != "standard":
        out["privacy"] = e["privacy"]
    if e.get("selectable") is False:
        out["selectable"] = False
    return out


def clamp_untrusted(entry: dict) -> dict:
    """Hint-only view of a user-scope sidecar: only id, triggers, task_classes and run_in/subagent_type are taken from
    it. Tier is capped at sonnet, authority none, privacy standard, priority 0..10, no acceptance/summary/not_for."""
    cap = TIERS.index("sonnet")
    t = entry["tier"]
    tier = {k: TIERS[min(TIERS.index(t[k]), cap)] for k in ("min", "default", "max")}
    out = {"id": entry["id"], "kind": "external" if entry["kind"] == "external" else "skill",
           "task_classes": entry["task_classes"], "triggers": entry["triggers"], "run_in": entry["run_in"],
           "tier": tier, "authority": dict(AUTH_DEFAULT), "privacy": "standard", "risk_ceiling": "low",
           "priority": max(0, min(entry["priority"], UNTRUSTED_PRIORITY_CAP)), "untrusted": True}
    if entry["run_in"] == "subagent":
        out["subagent_type"] = entry["subagent_type"]
    return out


def _clean(text, n: int = 160) -> str:
    return "".join(c if c.isprintable() else "?" for c in str(text))[:n]


class Compiled(NamedTuple):
    registry: dict
    data: bytes
    hashes: bytes
    skipped: list


def _privileged_names(found: dict) -> frozenset:
    names = set(PRIVILEGED_IDS)
    for e in found.values():
        if e["kind"] == "agent" and (e["tier"]["default"] in ("opus", "fable") or e["tier"]["max"] in ("opus", "fable")):
            names.add(e["id"])
            names.add(str(e.get("subagent_type", "")).lower())
    return frozenset(names)


WRITE_ORDER = ("none", "scratch", "tests", "workspace")
NETWORK_ORDER = ("none", "fetch", "bridge")


def _privileged_by_default(found: dict) -> frozenset:
    """Like _privileged_names, but an agent that merely MAY escalate to opus (tier.max, e.g. vault-worker) is not
    privileged: only the closed set and agents whose default tier is opus/fable. Overlays run on the former."""
    names = set(PRIVILEGED_IDS)
    for e in found.values():
        if e["kind"] == "agent" and e["tier"]["default"] in ("opus", "fable"):
            names.add(e["id"])
            names.add(str(e.get("subagent_type", "")).lower())
    return frozenset(names)


def _check_overlay_role(e: dict, found: dict) -> None:
    """An overlay role (roles/<id>.json, kind overlay) is a brief template run on an existing trusted agent role. It
    can never name a privileged agent, run above sonnet, or claim a tool or authority its target agent lacks."""
    where = f"{e['id']}.json"
    target_name = str(e["subagent_type"]).lower()
    privileged = _privileged_by_default(found)
    if target_name in privileged:
        raise RegistryError(f"{where}: an overlay role cannot run on the privileged agent {target_name!r}")
    target = found.get(target_name)
    if target is None or target["kind"] != "agent":
        raise RegistryError(f"{where}: subagent_type {target_name!r} is an unknown agent role")
    if target["tier"]["default"] in ("opus", "fable"):
        raise RegistryError(f"{where}: an overlay role cannot run on the privileged agent {target_name!r}")
    if any(e["tier"][k] in ("opus", "fable") for k in ("min", "default", "max")):
        raise RegistryError(f"{where}: an overlay role tier is at most sonnet")
    extra = sorted(set(e["tools"]) - set(target["tools"]))
    if extra:
        raise RegistryError(f"{where}: tools {extra} are not tools of {target_name!r}")
    if e["privacy"] == "standard" and target["privacy"] == "local_only":
        raise RegistryError(f"{where}: privacy is looser than that of {target_name!r}")
    if RISKS.index(e["risk_ceiling"]) > RISKS.index(target["risk_ceiling"]):
        raise RegistryError(f"{where}: risk_ceiling is higher than that of {target_name!r}")
    for ref in [*e.get("uses", []), *([e["escalates_to"]] if "escalates_to" in e else [])]:
        if str(ref).lower() in privileged:
            raise RegistryError(f"{where}: an overlay role cannot name the privileged id {ref!r}")
    a, t = e["authority"], target["authority"]
    if (WRITE_ORDER.index(a["write"]) > WRITE_ORDER.index(t["write"])
            or NETWORK_ORDER.index(a["network"]) > NETWORK_ORDER.index(t["network"])
            or (a["execute"] and not t["execute"]) or (a["spawn"] and not t["spawn"])):
        raise RegistryError(f"{where}: authority exceeds that of {target_name!r}")
    e["artifact_sha256"] = target["artifact_sha256"]  # the template's artifact is the agent file it runs on


def _encode(found: dict) -> bytes:
    reg = {"schema_version": SCHEMA, "entries": [compact(found[k]) for k in sorted(found)]}
    return (json.dumps(reg, sort_keys=True, separators=(",", ":"), ensure_ascii=True) + "\n").encode("ascii")


def compile_full(s_dir: Path, skills_dir: Path | None = None, agents_dir: Path | None = None,
                 max_bytes: int = MAX_BYTES) -> Compiled:
    """Scan the fixed roots and return registry, registry bytes, hashes bytes and the list of skipped untrusted sidecars.
    TRUSTED sources (`<S>/registry_src/skills` and `<S>/roles`, pinned with the release): any error is fatal.
    UNTRUSTED sources (every other `<skills_dir>/*/route.meta.json`): a bad one is skipped and reported, never fatal."""
    s_dir = Path(s_dir).resolve()
    skills_dir = Path(skills_dir) if skills_dir is not None else Path.home() / ".claude" / "skills"
    agents_dir = Path(agents_dir) if agents_dir is not None else s_dir.parents[1] / "agents"
    found: dict[str, dict] = {}
    skipped: list[str] = []
    overlay_roles: list[str] = []

    def add(meta_path: Path, artifact: Path | None, name: str, role: bool) -> None:
        where = str(meta_path.name if role else f"{name}/route.meta.json")
        raw = _read_json(meta_path, where)
        entry = validate(raw, where)
        if entry["id"] != name:
            raise RegistryError(f"{where}: id {entry['id']!r} must equal the artifact name {name!r}")
        if role:
            # ids are lowercase; an agent file may be capitalised (Explore.md): match case-insensitively
            cands = sorted(p for p in agents_dir.glob("*.md") if p.stem.lower() == name) if agents_dir.is_dir() else []
            artifact = cands[0] if cands else None
            if entry["kind"] == "overlay" and entry["run_in"] == "subagent":
                # a brief template that runs on an existing agent (checked against it once every role is loaded)
                if artifact is not None:
                    raise RegistryError(f"{where}: an overlay role is a brief template and has no agent file "
                                        f"({artifact.name})")
                overlay_roles.append(name)
            elif entry["kind"] != "agent" or entry["run_in"] != "subagent" or str(entry.get("subagent_type")).lower() != name:
                raise RegistryError(f"{where}: a role sidecar is kind agent, run_in subagent, subagent_type == id "
                                    "(case-insensitive), or kind overlay, run_in subagent on an existing agent")
            if entry["kind"] == "agent" and artifact is not None:
                fm = parse_frontmatter(artifact)
                tools = sorted(t.strip() for t in fm.get("tools", "").split(",") if t.strip())
                if tools != sorted(entry["tools"]):
                    raise RegistryError(f"{where}: tools {sorted(entry['tools'])} != agent file tools {tools}")
                mt = _model_tier(fm.get("model", ""))
                if mt is not None and mt != entry["tier"]["default"]:
                    raise RegistryError(f"{where}: tier.default {entry['tier']['default']} != agent model {fm.get('model')}")
        elif entry["kind"] == "agent":
            raise RegistryError(f"{where}: kind agent belongs in roles/")
        entry["src_sha256"] = _sha(meta_path, lf=True)
        entry["artifact_sha256"] = _sha(artifact, lf=True) if artifact is not None and artifact.is_file() else None
        if name in found:
            if found[name]["src_sha256"] != entry["src_sha256"]:
                raise RegistryError(f"duplicate id {name!r}: two different sidecars")
            if found[name]["artifact_sha256"] is None:
                found[name]["artifact_sha256"] = entry["artifact_sha256"]
            return
        found[name] = entry

    # 1. trusted: the sidecars shipped with the release
    for d in _dirs(s_dir / "registry_src" / "skills"):
        meta = d / "route.meta.json"
        if meta.is_file():
            art = d / "SKILL.md"
            if not art.is_file() and d.name in (s_dir.name, "cost-router"):
                art = s_dir / "SKILL.md"
            if not art.is_file():  # the artifact is the installed skill, when there is one
                art = skills_dir / d.name / "SKILL.md"
            add(meta, art if art.is_file() else None, d.name, role=False)
    roles = s_dir / "roles"
    if roles.is_dir():
        for p in sorted(roles.glob("*.json")):
            if BAK_MARK in p.name or p.is_symlink():
                continue
            add(p, None, p.stem, role=True)
    for name in overlay_roles:
        _check_overlay_role(found[name], found)
    for e in found.values():
        for ref in [*e.get("uses", []), *([e["escalates_to"]] if "escalates_to" in e else [])]:
            if ref not in found:
                raise RegistryError(f"{e['id']}: references unknown id {ref!r}")
    size = len(_encode(found))
    if size > max_bytes:
        raise RegistryError(f"registry is {size} bytes, cap {max_bytes}")
    privileged = _privileged_names(found)

    # 2. untrusted: user-scope sidecars that are not the shipped ones. Never fatal.
    trusted_ids = set(found)
    n_untrusted = 0
    for d in _dirs(skills_dir):
        meta = d / "route.meta.json"
        label = f"{_clean(d.name, 60)}/route.meta.json"
        try:
            if meta.is_symlink():
                skipped.append(f"skipped {label}: symlink")
                continue
            if not meta.is_file():
                continue
            if d.name in trusted_ids:
                if _sha(meta, lf=True) == found[d.name]["src_sha256"]:  # the installed copy of a shipped sidecar
                    if found[d.name]["artifact_sha256"] is None and (d / "SKILL.md").is_file():
                        found[d.name]["artifact_sha256"] = _sha(d / "SKILL.md", lf=True)
                else:
                    skipped.append(f"skipped {label}: claims the id of a shipped entry")
                continue
            fsize = meta.stat().st_size
            if fsize > UNTRUSTED_META_MAX:
                skipped.append(f"skipped {label}: {fsize} bytes (> {UNTRUSTED_META_MAX})")
                continue
            if n_untrusted >= UNTRUSTED_MAX_ENTRIES:
                skipped.append(f"skipped {label}: more than {UNTRUSTED_MAX_ENTRIES} user-scope sidecars")
                continue
            raw = _read_json(meta, label)
            entry = validate(raw, label, untrusted=True)
            if entry["id"] != d.name:
                raise RegistryError(f"{label}: id must equal the directory name")
            if entry["kind"] == "agent":
                raise RegistryError(f"{label}: kind agent is not allowed in user-scope skills")
            if entry["id"] in privileged or str(entry.get("subagent_type", "")).lower() in privileged:
                raise RegistryError(f"{label}: claims a privileged agent name")
            clamped = clamp_untrusted(entry)
            clamped["src_sha256"] = _sha(meta, lf=True)
            art = d / "SKILL.md"
            clamped["artifact_sha256"] = _sha(art, lf=True) if art.is_file() else None
            found[d.name] = clamped
            if len(_encode(found)) > max_bytes:
                del found[d.name]
                raise RegistryError(f"{label}: would exceed the registry cap")
            n_untrusted += 1
        except (RegistryError, OSError, UnicodeError) as exc:
            skipped.append(f"skipped {label}: {_clean(exc)}")
    hashes = {"schema_version": SCHEMA, "entries": {k: {"src_sha256": found[k]["src_sha256"],
                                                          "artifact_sha256": found[k]["artifact_sha256"]}
                                                    for k in sorted(found)}}
    hdata = (json.dumps(hashes, sort_keys=True, separators=(",", ":"), ensure_ascii=True) + "\n").encode("ascii")
    reg = {"schema_version": SCHEMA, "entries": [compact(found[k]) for k in sorted(found)]}
    return Compiled(reg, _encode(found), hdata, skipped)


def compile_registry(s_dir: Path, skills_dir: Path | None = None, agents_dir: Path | None = None,
                     max_bytes: int = MAX_BYTES, report: list | None = None) -> tuple[dict, bytes]:
    """compile_full without the hashes file: (registry, bytes). Skipped untrusted sidecars are appended to `report`."""
    c = compile_full(s_dir, skills_dir, agents_dir, max_bytes)
    if report is not None:
        report.extend(c.skipped)
    return c.registry, c.data


def write_registry(s_dir: Path, skills_dir: Path | None = None, agents_dir: Path | None = None,
                   out: Path | None = None, max_bytes: int = MAX_BYTES, report: list | None = None) -> Path:
    c = compile_full(s_dir, skills_dir, agents_dir, max_bytes)
    if report is not None:
        report.extend(c.skipped)
    out = Path(out) if out is not None else Path(s_dir) / "registry.json"
    for target, data in ((out, c.data), (out.with_name(HASHES_NAME), c.hashes)):
        tmp = target.with_name(target.name + ".tmp")
        tmp.write_bytes(data)
        tmp.replace(target)
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
            c = compile_full(s_dir, args.skills_dir, args.agents_dir)
            for line in c.skipped:
                print(line)
            cur = Path(args.out) if args.out else s_dir / "registry.json"
            hcur = cur.with_name(HASHES_NAME)
            ok = cur.is_file() and cur.read_bytes() == c.data and hcur.is_file() and hcur.read_bytes() == c.hashes
            print("registry current" if ok else "registry stale or missing")
            return 0 if ok else 1
        skipped: list = []
        out = write_registry(s_dir, args.skills_dir, args.agents_dir, args.out, report=skipped)
        for line in skipped:
            print(line)
    except RegistryError as exc:
        print(f"registry_compile: {exc}", file=sys.stderr)
        return 2
    print(f"wrote {out} ({out.stat().st_size} bytes)")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
