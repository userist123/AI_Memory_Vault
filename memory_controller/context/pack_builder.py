from typing import List, Dict, Any, Optional

class ContextPackBuilder:
    """Assemble the final context payload sent back to the requester.

    The contract fields:
        - requestId: identifier of the request (string).
        - agentId: identifier of the calling agent.
        - budget: dict with 'soft' and 'hard' limits used for this request.
        - results: list of disclosed note objects (already processed).
        - disclosureLevel: one of ['metadata', 'snippet', 'sections', 'full']
        - provenance: minimal provenance (source_type, source_ref) included in each result.
        - nextPageToken: optional string if pagination is needed.
        - auditRef: optional reference to an audit log entry (only if requested).
    """

    def __init__(self):
        pass

    def build(
        self,
        request_id: str,
        agent_id: str,
        budget: Dict[str, Any],
        results: List[Dict[str, Any]],
        disclosure_level: str,
        minimal_provenance: List[Dict[str, Any]] = None,
        next_page_token: Optional[str] = None,
        audit_ref: Optional[str] = None,
    ) -> Dict[str, Any]:
        pack: Dict[str, Any] = {
            "requestId": request_id,
            "agentId": agent_id,
            "budget": budget,
            "disclosureLevel": disclosure_level,
            "results": results,
        }
        if minimal_provenance:
            # Attach provenance directly to each result (already expected to have it)
            for res, prov in zip(pack["results"], minimal_provenance):
                res.setdefault("provenance", {})
                res["provenance"].setdefault("source_type", prov.get("source_type"))
                res["provenance"].setdefault("source_ref", prov.get("source_ref"))
        if next_page_token:
            pack["nextPageToken"] = next_page_token
        if audit_ref:
            pack["auditRef"] = audit_ref
        return pack
