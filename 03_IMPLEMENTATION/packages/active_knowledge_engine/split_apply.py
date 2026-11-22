"""OWNER tool: turn a PLANNED split manifest into child notes an agent can read.

This is an authority action. It runs only as HUMAN or ADMIN; the CLI additionally needs an
interactive terminal and the parent id typed back. Nothing here runs on behalf of the owner.

For each child, in manifest order, it:
1. re-checks the manifest against the parent as it is now (stale or tampered: refused before any
   write);
2. creates the child RAW (RAW is never served to an agent), with the parent's type, category,
   confidence and provenance source type, a `part_of` relation to the parent, and the exact slice;
3. attests it with `expected_content_sha256` = the manifest hash, so a body changed between
   creation and attestation is refused (memory/controller.py, content binding);
4. reviews ("approve") and promotes it to ACTIVE;
5. reads it back through `memory_get` as an AI_AGENT and requires the exact slice.

The first failure stops the run and is reported with the child index; nothing after it is
created. A re-run skips a child already ACTIVE, verified and bound to the same hash; any other
existing note under a proposed id is a conflict. The parent note is never modified.
"""
from __future__ import annotations

import hashlib
import json
from datetime import datetime, timezone
from typing import Any, Callable, Dict, List

from memory_controller.authorizer import Principal

OWNER_PRINCIPALS = frozenset({Principal.HUMAN, Principal.ADMIN})
SUPPORTED_SCHEMAS = frozenset({"ake.split_plan/2"})


class ManifestError(ValueError):
    """The manifest does not match the parent as it is now; nothing was written."""


def _sha(text: str) -> str:
    return hashlib.sha256(text.encode("utf-8")).hexdigest()


def _manifest_sha(manifest: Dict[str, Any]) -> str:
    return _sha(json.dumps(manifest, sort_keys=True, ensure_ascii=False, separators=(",", ":")))


def _check_manifest(controller, manifest: Dict[str, Any]) -> tuple:
    if manifest.get("schema") not in SUPPORTED_SCHEMAS or manifest.get("status") != "PLANNED":
        raise ManifestError("not a PLANNED split manifest of a supported schema")
    parent_id = str(manifest.get("parent_id") or "")
    parent = controller.storage.get(parent_id)
    if not parent:
        raise ManifestError(f"parent not found: {parent_id}")
    body = str(parent.get("content") or "")
    if _sha(body) != manifest.get("parent_content_sha256"):
        raise ManifestError("stale manifest: the parent changed since it was planned; re-plan")
    children = manifest.get("children") or []
    if not children:
        raise ManifestError("manifest has no children")
    pos = 0
    for expected_index, ch in enumerate(children):
        start, end = ch.get("char_start"), ch.get("char_end")
        if ch.get("index") != expected_index or start != pos or not isinstance(end, int) or end <= start:
            raise ManifestError(f"child {expected_index}: offsets do not tile the parent")
        if _sha(body[start:end]) != ch.get("content_sha256"):
            raise ManifestError(f"child {expected_index}: hash does not match the parent slice")
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


def apply_split(controller, manifest: Dict[str, Any], *, principal: Principal,
                confirm: Callable[[str, int], bool]) -> Dict[str, Any]:
    if principal not in OWNER_PRINCIPALS:
        raise PermissionError("apply-split is an owner action (HUMAN or ADMIN)")
    parent, body, children = _check_manifest(controller, manifest)
    parent_id = str(parent["id"])
    if not confirm(parent_id, len(children)):
        return {"status": "NOT_CONFIRMED", "parent_id": parent_id, "children": []}

    from interfaces import memory_access

    evidence = f"ake.split_plan manifest sha256 {_manifest_sha(manifest)}"
    done: List[Dict[str, Any]] = []
    for ch in children:
        cid = ch["child_id_proposed"]
        piece = body[ch["char_start"]:ch["char_end"]]
        try:
            existing = controller.storage.get(cid)
            if existing:
                if (str(existing.get("lifecycle")).endswith("ACTIVE") and existing.get("verification") == "verified"
                        and existing.get("attested_content_sha256") == ch["content_sha256"]):
                    done.append({"index": ch["index"], "id": cid, "action": "already_applied"})
                    continue
                raise ValueError(f"conflict: a different note already uses id {cid}")
            controller.propose(principal, _child_note(parent, ch, piece))
            if _sha(str(controller.storage.get(cid).get("content") or "")) != ch["content_sha256"]:
                raise ValueError("stored body differs from the manifest slice")
            controller.attest(principal, cid,
                              verification_reason=f"split of {parent_id}, lines {ch['line_start']}-{ch['line_end']}",
                              evidence_reference=evidence,
                              expected_content_sha256=ch["content_sha256"])
            controller.review(principal, cid, "approve")
            controller.promote(principal, cid)
            served = memory_access.get(controller, cid).get("content")
            if served != piece:
                raise ValueError("memory_get does not serve the exact slice")
            done.append({"index": ch["index"], "id": cid, "action": "applied"})
        except Exception as exc:  # noqa: BLE001 - stop at the first failure, report it
            return {"status": "FAILED", "parent_id": parent_id, "children": done,
                    "failed_child": {"index": ch["index"], "id": cid,
                                     "error": f"{type(exc).__name__}: {exc}"}}
    return {"status": "APPLIED", "parent_id": parent_id, "children": done, "evidence_reference": evidence}
