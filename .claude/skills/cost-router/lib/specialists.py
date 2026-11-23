#!/usr/bin/env python3
"""Specialist selection for the cost-router prompt hook (M6).

`select(prompt, task_class)` picks at most one specialist id (plus at most one overlay) from the compiled,
closed-set `registry.json` (lib/registry_compile.py). Pure string matching: no LLM, no embeddings, no network.

Contract (the prompt text is untrusted):
- text can only SELECT an id that already exists in the registry; it can never create one;
- the result is advisory and carries the registry's own tier/authority for display; this module returns no tool, no
  authority and no model, and the hook never feeds it into the route state, so text cannot lower a tier, add tools or
  change authority (it may only name a specialist whose registry floor is higher, which can only raise what is shown);
- any load/parse failure returns None (fail open: no hint). It never touches the Fable guard.

Order: folded literal trigger match (word-bounded) -> task_class compatibility (a class the policy does not know, i.e.
"unclassified", is compatible with everything) -> priority desc -> more trigger hits -> id.
"""
from __future__ import annotations

import json
import re
import unicodedata
from pathlib import Path

REGISTRY = Path(__file__).resolve().parent.parent / "registry.json"
MAX_REGISTRY_BYTES = 64 * 1024  # a larger file is not the compiler's output (cap 16 KB): ignore it
UNTRUSTED_PRIORITY_CAP = 10
POLICY_CLASSES = ("explore", "summarize", "implement", "review", "design", "frontier")
ID_RE = re.compile(r"^[a-z0-9-]{1,40}$")
MAX_PROMPT = 8000  # only the head of the prompt is matched: bounded work, and pasted bulk is not the request


# Romanian legacy cedilla letters -> the correct comma-below ones (both then fold to plain s/t)
_CEDILLA = str.maketrans({"ş": "ș", "Ş": "Ș", "ţ": "ț", "Ţ": "Ț"})


def normalize(text: str) -> str:
    """NFKC, Unicode format chars (Cf: zero-width space/joiners, soft hyphen, bidi marks) removed, cedilla s/t mapped to
    comma-below, casefold, whitespace collapsed. Diacritics are KEPT (see fold)."""
    s = unicodedata.normalize("NFKC", str(text))
    s = "".join(c for c in s if unicodedata.category(c) != "Cf")
    return " ".join(s.translate(_CEDILLA).casefold().split())


def fold(text: str) -> str:
    """normalize() plus diacritics folded: 'Șterge', 'şterge' and 'sterge' compare equal."""
    s = unicodedata.normalize("NFKD", normalize(text))
    s = "".join(c for c in s if not unicodedata.combining(c))
    return " ".join(unicodedata.normalize("NFKD", s).casefold().split())


# Look-alike letters (Cyrillic, Greek, IPA) -> ASCII, applied after NFKC by fold_strict: `dеploy` (Cyrillic e) and
# `аmcache` (Cyrillic a) compare equal to the ASCII words. Latin letters with diacritics are NOT touched here.
CONFUSABLES = str.maketrans({
    "а": "a", "в": "b", "е": "e", "ё": "e", "к": "k", "м": "m", "н": "h", "о": "o", "р": "p", "с": "c", "т": "t",
    "у": "y", "х": "x", "і": "i", "ї": "i", "ј": "j", "ѕ": "s", "ԁ": "d", "ԛ": "q", "ԝ": "w", "һ": "h", "ʏ": "y",
    "А": "A", "В": "B", "Е": "E", "Ё": "E", "К": "K", "М": "M", "Н": "H", "О": "O", "Р": "P", "С": "C", "Т": "T",
    "У": "Y", "Х": "X", "І": "I", "Ї": "I", "Ј": "J", "Ѕ": "S", "Ԁ": "D", "Ԛ": "Q", "Ԝ": "W", "Һ": "H",
    "α": "a", "β": "b", "ε": "e", "η": "n", "ι": "i", "κ": "k", "ν": "v", "ο": "o", "ρ": "p", "τ": "t", "υ": "u",
    "χ": "x", "ω": "w", "ϲ": "c", "ϳ": "j",
    "Α": "A", "Β": "B", "Ε": "E", "Ζ": "Z", "Η": "H", "Ι": "I", "Κ": "K", "Μ": "M", "Ν": "N", "Ο": "O", "Ρ": "P",
    "Τ": "T", "Υ": "Y", "Χ": "X", "Ϲ": "C",
    "ɡ": "g", "ı": "i", "ȷ": "j", "ɑ": "a", "ɩ": "i", "ʋ": "v",
})


def fold_strict(text: str) -> str:
    """fold() after NFKC and the CONFUSABLES map: the normalizer of the fable-executor brief checks (f)/(g)."""
    return fold(unicodedata.normalize("NFKC", str(text)).translate(CONFUSABLES))


def load(path: Path | None = None) -> dict | None:
    """The registry as {id: entry}, or None when missing/invalid (fail open)."""
    try:
        p = Path(path) if path is not None else REGISTRY
        if p.stat().st_size > MAX_REGISTRY_BYTES:
            return None
        data = json.loads(p.read_text(encoding="utf-8"))
        if not isinstance(data, dict) or data.get("schema_version") != 1 or not isinstance(data.get("entries"), list):
            return None
        out: dict = {}
        for e in data["entries"]:
            if not (isinstance(e, dict) and isinstance(e.get("id"), str) and ID_RE.match(e["id"])):
                return None
            if e["kind"] not in ("agent", "skill", "overlay", "external") or e["id"] in out:
                return None
            if not (isinstance(e.get("triggers"), list) and isinstance(e.get("priority"), int)
                    and isinstance(e.get("task_classes"), list)):
                return None
            out[e["id"]] = e
        return out
    except Exception:
        return None


def _hits(folded_prompt: str, triggers) -> int:
    n = 0
    for t in triggers:
        if isinstance(t, str) and t and re.search(r"(?<![a-z0-9])" + re.escape(t) + r"(?![a-z0-9])", folded_prompt):
            n += 1
    return n


def _compatible(entry: dict, task_class: str) -> bool:
    return task_class not in POLICY_CLASSES or task_class in entry["task_classes"]


def select(prompt: str, task_class: str, registry: dict | None = None) -> dict | None:
    """-> {"id", "kind", "overlay", "acceptance", "tier", "run_in"?} or None. Never raises."""
    try:
        reg = registry if registry is not None else load()
        if not reg:
            return None
        text = fold(str(prompt)[:MAX_PROMPT])
        best = {"overlay": None, "other": None}
        for e in reg.values():
            if e.get("selectable") is False:
                continue  # a gated entry (fable-executor): never selected by text, whatever triggers it carries
            h = _hits(text, e["triggers"])
            if not h or not _compatible(e, task_class):
                continue
            slot = "overlay" if e["kind"] == "overlay" else "other"
            untrusted = bool(e.get("untrusted"))
            prio = min(e["priority"], UNTRUSTED_PRIORITY_CAP) if untrusted else e["priority"]
            key = (-prio, untrusted, -h, e["id"])  # trusted entries win a priority tie
            if best[slot] is None or key < best[slot][0]:
                best[slot] = (key, e)
        main = best["other"][1] if best["other"] else None
        over = best["overlay"][1] if best["overlay"] else None
        # the overlay "cost-router" is the router itself: naming it adds nothing
        if over is not None and over["id"] == "cost-router":
            over = None
        lead = main or over
        if lead is None:
            return None
        return {"id": lead["id"], "kind": lead["kind"],
                "overlay": over["id"] if (main is not None and over is not None) else None,
                "acceptance": "" if lead.get("untrusted") else str(lead.get("acceptance") or "")[:200],
                "tier": list(lead.get("tier") or []), "subagent_type": lead.get("subagent_type")}
    except Exception:
        return None


def local_only_hits(prompt: str, registry: dict | None = None) -> list:
    """Ids of the trusted local_only (privacy) entries whose trigger appears in `prompt`, whatever the task class.
    Used to keep the gated fable-executor off privacy work; scans the whole text with fold_strict. [] when the registry cannot be loaded (callers that need
    a guarantee check the registry themselves)."""
    try:
        reg = registry if registry is not None else load()
        if not reg:
            return []
        text = fold_strict(str(prompt))  # the WHOLE text (no MAX_PROMPT head), look-alikes folded (M12)
        return sorted(e["id"] for e in reg.values()
                      if e.get("privacy") == "local_only" and not e.get("untrusted") and _hits(text, e["triggers"]))
    except Exception:
        return []


def spec_token(sel: dict | None) -> str:
    """`spec=<id>` or `spec=<id>+<overlay>`; empty when there is no selection."""
    if not sel:
        return ""
    return "spec=" + sel["id"] + ("+" + sel["overlay"] if sel.get("overlay") else "")
