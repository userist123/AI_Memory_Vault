from typing import List, Dict, Any

class ProgressiveDisclosure:
    """Utility to progressively disclose memory content based on budget.

    The workflow:
        1. metadata_only – returns identifiers and minimal metadata.
        2. snippet – returns a short excerpt (e.g., first 200 chars).
        3. sections – returns relevant sections based on query highlights.
        4. full_document – returns the full note content.
        5. provenance_on_demand – fetches raw provenance when requested.
    """

    def __init__(self, budget):
        self.budget = budget  # Instance of ContextBudget or similar

    def _within_budget(self, usage: int) -> bool:
        try:
            self.budget.check_budget(usage)
            return True
        except Exception:
            return False

    def metadata_only(self, notes: List[Dict[str, Any]]) -> List[Dict[str, Any]]:
        # Return only id, type, lifecycle, and confidence
        result = []
        usage = 0
        for note in notes:
            entry = {
                "id": note.get("id"),
                "type": note.get("type"),
                "lifecycle": note.get("lifecycle"),
                "confidence": note.get("confidence"),
                "verification": note.get("verification"),
                "provenance": note.get("provenance", {}),
                "relations": note.get("relations", [])
            }
            result.append(entry)
            usage += 1  # Count each metadata as 1 unit
            if not self._within_budget(usage):
                break
        return result

    def snippet(self, notes: List[Dict[str, Any]], chars: int = 200) -> List[Dict[str, Any]]:
        result = []
        usage = 0
        for note in notes:
            content = note.get("content", "")
            snippet = content[:chars]
            entry = {"id": note.get("id"), "snippet": snippet}
            result.append(entry)
            usage += chars
            if not self._within_budget(usage):
                break
        return result

    def sections(self, notes: List[Dict[str, Any]], query: str) -> List[Dict[str, Any]]:
        # Very naive: return lines containing any query token
        tokens = set(query.lower().split())
        result = []
        usage = 0
        for note in notes:
            content = note.get("content", "")
            lines = content.split("\n")
            matched = [ln for ln in lines if any(tok in ln.lower() for tok in tokens)]
            entry = {"id": note.get("id"), "sections": matched[:5]}
            result.append(entry)
            usage += len(matched)
            if not self._within_budget(usage):
                break
        return result

    def full_document(self, notes: List[Dict[str, Any]]) -> List[Dict[str, Any]]:
        # Return full content respecting hard budget (bytes)
        result = []
        usage = 0
        for note in notes:
            content = note.get("content", "")
            size = len(content.encode("utf-8"))
            if not self._within_budget(usage + size):
                # A single oversized high-ranked note must not prevent smaller
                # later candidates from being disclosed.
                continue
            candidate = {"id": note.get("id"), "content": content}
            if hasattr(self.budget, "estimate_tokens"):
                if self.budget.estimate_tokens(result + [candidate]) > self.budget.hard_token_budget:
                    continue
            result.append(candidate)
            usage += size
        return result

    def provenance_on_demand(self, note_ids: List[str], storage_engine) -> List[Dict[str, Any]]:
        # Retrieve raw provenance records for given ids via storage engine
        prov = []
        for nid in note_ids:
            prov.append(storage_engine.get_provenance(nid))
        return prov
