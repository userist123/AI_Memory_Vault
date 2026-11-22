"""OWNER tool: turn a PLANNED split manifest into child notes an agent can read.

What the owner attests: that each child is an EXACT slice of a parent that is already ACTIVE and
verified. The tool never attests new text: a parent that is not ACTIVE + verified is refused, so
an agent cannot route its own unverified note through this tool into a verified state.

Authority limits, stated plainly: the principal is an argument, as for every controller call in
this vault (the runtime authority layer is not wired, VAULT_STATE.md). Any code that can import
this module can call it with HUMAN. The CLI's interactive-terminal check and the typed parent id
guard against accidental or scripted runs from a pipe; they do not prove the owner is present.

Pre-flight, before any write (a failure raises ManifestError and writes nothing):
- schema and status; the parent exists, is ACTIVE + verified, and its body hash equals the plan's;
- offsets tile the body, slice hashes match, line numbers match the offsets;
- child ids equal the deterministic derivation (uuid5 of parent id, parent hash, index), are
  unique, and differ from the parent id;
- the parent's provenance source type can be carried by an owner-created child (not "ai").

Per child, in order: create RAW (never served to agents) -> check every written field -> attest
with expected_content_sha256 -> review -> promote -> re-check every written field plus
verification, verification_source and the attested hash -> read back through memory_get as an
AI_AGENT and require the exact slice. The first failure stops the run and reports the child and
its current state; a child that fails an integrity check (fields or served text not what was
attested) is archived so it is no longer served. A re-run resumes the tool's own partial children from the state they are in;
an existing note that is not the tool's own child is a conflict. The parent is never modified.
"""
from __future__ import annotations

import hashlib
import json
import uuid
from datetime import datetime, timezone
from typing import Any, Callable, Dict, List

from memory_controller.authorizer import Principal

OWNER_PRINCIPALS = frozenset({Principal.HUMAN, Principal.ADMIN})
SUPPORTED_SCHEMAS = frozenset({"ake.split_plan/2"})
#: provenance source types an owner principal may not claim (memory/controller.py)
OWNER_UNCLAIMABLE_SOURCE_TYPES = frozenset({"ai"})


class ManifestError(ValueError):
    """The manifest or the parent is not eligible; nothing was written."""


class IntegrityFailure(ValueError):
    """A created child is not what the owner attested; the tool archives it (fail closed)."""


def _sha(text: str) -> str:
    return hashlib.sha256(text.encode("utf-8")).hexdigest()


def _manifest_sha(manifest: Dict[str, Any]) -> str:
    return _sha(json.dumps(manifest, sort_keys=True, ensure_ascii=False, separators=(",", ":")))


def _lc(note: Dict[str, Any]) -> str:
    value = note.get("lifecycle")
    return str(getattr(value, "value", value) or "").upper()


def expected_child_id(parent_id: str, parent_sha: str, index: int) -> str:
    return str(uuid.uuid5(uuid.NAMESPACE_URL, f"ake-split/{parent_id}/{parent_sha}/{index + 1}"))


def _line_span(body: str, start: int, end: int) -> tuple:
    first = body.count("\n", 0, start) + 1
    last = body.count("\n", 0, end) if body[end - 1] == "\n" else body.count("\n", 0, end) + 1
    return first, last


def preflight(controller, manifest: Dict[str, Any]) -> tuple:
    if manifest.get("schema") not in SUPPORTED_SCHEMAS or manifest.get("status") != "PLANNED":
        raise ManifestError("not a PLANNED split manifest of a supported schema")
    parent_id = str(manifest.get("parent_id") or "")
    parent = controller.storage.get(parent_id)
    if not parent:
        raise ManifestError(f"parent not found: {parent_id}")
    if _lc(parent) != "ACTIVE" or parent.get("verification") != "verified":
        raise ManifestError("the parent must be ACTIVE and verified: the owner attests exact slices of a "
                            f"verified note, never new text (found {_lc(parent)}/{parent.get('verification')})")
    provenance = parent.get("provenance") if isinstance(parent.get("provenance"), dict) else {}
    if provenance.get("source_type") in OWNER_UNCLAIMABLE_SOURCE_TYPES:
        raise ManifestError(f"parent provenance source_type {provenance.get('source_type')!r} cannot be "
                            "carried by an owner-created child")
    body = str(parent.get("content") or "")
    parent_sha = _sha(body)
    if parent_sha != manifest.get("parent_content_sha256"):
        raise ManifestError("stale manifest: the parent changed since it was planned; re-plan")
    children = manifest.get("children") or []
    if not children:
        raise ManifestError("manifest has no children")
    pos, seen = 0, set()
    for i, ch in enumerate(children):
        start, end = ch.get("char_start"), ch.get("char_end")
        if ch.get("index") != i or start != pos or not isinstance(end, int) or end <= start:
            raise ManifestError(f"child {i}: offsets do not tile the parent")
        if _sha(body[start:end]) != ch.get("content_sha256"):
            raise ManifestError(f"child {i}: hash does not match the parent slice")
        if (ch.get("line_start"), ch.get("line_end")) != _line_span(body, start, end):
            raise ManifestError(f"child {i}: line numbers do not match the offsets")
        cid = ch.get("child_id_proposed")
        if cid != expected_child_id(parent_id, parent_sha, i) or cid in seen or cid == parent_id:
            raise ManifestError(f"child {i}: id is not the deterministic child id")
        seen.add(cid)
        pos = end
    if pos != len(body):
        raise ManifestError("children do not cover the whole parent")
    return parent, body, children


def _child_note(parent: Dict[str, Any], child: Dict[str, Any], piece: str) -> Dict[str, Any]:
    today = datetime.now(timezone.utc).date().isoformat()
    provenance = parent.get("provenance") if isinstance(parent.get("provenance"), dict) else {}
    tags = [str(t) for t in (parent.get("tags") or [])]
    return {
        "id": child["child_id_proposed"],
        "type": parent.get("type"),
        "category": parent.get("category"),
        "tags": tags + (["split-child"] if "split-child" not in tags else []),
        "created": today,
        "updated": today,
        "provenance": {
            "source_type": provenance.get("source_type", "user"),
            "source_ref": f"{parent['id']}#L{child['line_start']}-L{child['line_end']}",
        },
        "confidence": parent.get("confidence", "high"),
        "verification": "unverified",
        "relations": [{"type": "part_of", "target_id": str(parent["id"])}],
        "lifecycle": "RAW",
        "content": piece,
    }


_CHECKED_FIELDS = ("type", "category", "tags", "relations", "confidence")


def _mismatch(stored: Dict[str, Any], expected: Dict[str, Any]) -> List[str]:
    bad = [f for f in _CHECKED_FIELDS if stored.get(f) != expected.get(f)]
    prov = stored.get("provenance") if isinstance(stored.get("provenance"), dict) else {}
    for key in ("source_type", "source_ref"):
        if prov.get(key) != expected["provenance"][key]:
            bad.append(f"provenance.{key}")
    if _sha(str(stored.get("content") or "")) != _sha(expected["content"]):
        bad.append("content")
    return bad


def _is_own_child(stored: Dict[str, Any], expected: Dict[str, Any]) -> bool:
    return not _mismatch(stored, expected)


def _state(note: Dict[str, Any]) -> str:
    return f"{_lc(note)}/{note.get('verification')}"


def apply_split(controller, manifest: Dict[str, Any], *, principal: Principal,
                confirm: Callable[[str, int], bool]) -> Dict[str, Any]:
    if principal not in OWNER_PRINCIPALS:
        raise PermissionError("apply-split is an owner action (HUMAN or ADMIN)")
    parent, body, children = preflight(controller, manifest)
    parent_id = str(parent["id"])
    if not confirm(parent_id, len(children)):
        return {"status": "NOT_CONFIRMED", "parent_id": parent_id, "children": []}

    from interfaces import memory_access

    evidence = f"ake.split_plan manifest sha256 {_manifest_sha(manifest)}"
    done: List[Dict[str, Any]] = []
    for ch in children:
        cid = ch["child_id_proposed"]
        piece = body[ch["char_start"]:ch["char_end"]]
        expected = _child_note(parent, ch, piece)
        action = "applied"
        try:
            stored = controller.storage.get(cid)
            if stored is not None:
                if not _is_own_child(stored, expected):
                    raise IntegrityFailure(f"conflict: a different note already uses id {cid}")
                action = "resumed"
            else:
                controller.propose(principal, expected)
                stored = controller.storage.get(cid)
            lc, ver = _lc(stored), stored.get("verification")
            if lc == "ACTIVE" and ver == "verified" and stored.get("attested_content_sha256") == ch["content_sha256"]:
                done.append({"index": ch["index"], "id": cid, "action": "already_applied"})
                continue
            if ver != "verified":
                if lc != "RAW":
                    raise ValueError(f"cannot resume from {_state(stored)}")
                bad = _mismatch(stored, expected)
                if bad:
                    raise IntegrityFailure(f"fields changed before attestation: {bad}")
                controller.attest(principal, cid,
                                  verification_reason=(f"exact slice of verified parent {parent_id}, "
                                                       f"lines {ch['line_start']}-{ch['line_end']}"),
                                  evidence_reference=evidence,
                                  expected_content_sha256=ch["content_sha256"])
                lc = _lc(controller.storage.get(cid))
            if lc == "RAW":
                controller.review(principal, cid, "approve")
                lc = _lc(controller.storage.get(cid))
            if lc == "REVIEW":
                controller.promote(principal, cid)
            final = controller.storage.get(cid)
            bad = _mismatch(final, expected)
            if _lc(final) != "ACTIVE":
                bad.append("lifecycle")
            if final.get("verification") != "verified":
                bad.append("verification")
            # any owner principal's attestation is the owner's (an ADMIN-attested child may be
            # resumed by HUMAN; attest() keeps the first stamp when state and hash already match)
            if final.get("verification_source") not in {p.value for p in OWNER_PRINCIPALS}:
                bad.append("verification_source")
            if final.get("attested_content_sha256") != ch["content_sha256"]:
                bad.append("attested_content_sha256")
            if bad:
                raise IntegrityFailure(f"child is not what was attested after promotion: {bad}")
            if memory_access.get(controller, cid).get("content") != piece:
                raise IntegrityFailure("memory_get does not serve the exact slice")
            done.append({"index": ch["index"], "id": cid, "action": action})
        except Exception as exc:  # noqa: BLE001 - stop at the first failure, report it
            remediation = None
            current = controller.storage.get(cid)
            conflict = isinstance(exc, IntegrityFailure) and str(exc).startswith("conflict")
            if isinstance(exc, IntegrityFailure) and current is not None and not conflict                     and _lc(current) in {"ACTIVE", "REVIEW", "RAW"}:
                # the tool's own child is not what was attested: stop serving it
                try:
                    controller.archive(principal, cid, reason=f"apply-split integrity failure: {exc}")
                    remediation = "archived"
                except Exception as arch_exc:  # noqa: BLE001
                    remediation = f"archive failed: {type(arch_exc).__name__}: {arch_exc}"
                current = controller.storage.get(cid)
            return {"status": "FAILED", "parent_id": parent_id, "children": done,
                    "failed_child": {"index": ch["index"], "id": cid,
                                     "state": _state(current) if current else "absent",
                                     "remediation": remediation,
                                     "error": f"{type(exc).__name__}: {exc}"}}
    return {"status": "APPLIED", "parent_id": parent_id, "children": done, "evidence_reference": evidence}
