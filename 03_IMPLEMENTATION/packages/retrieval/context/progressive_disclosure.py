import re
import zlib
from typing import List, Dict, Any
from .budget import BudgetExceededError


SECURITY_FIELDS = (
    "verification",
    "security",
    "provenance",
    "integrity",
    "requirements",
    "forbidden",
    "constraints",
    "acceptance",
)


class ProgressiveDisclosure:
    """Disclose only already-verified memory at the smallest useful level.

    Disclosure is not a trust boundary. Every note must carry an explicit
    trusted verification record before any representation is returned, and
    security/provenance evidence is retained even when content is reduced.
    """

    def __init__(self, budget):
        self.budget = budget

    @staticmethod
    def _verified(note: Dict[str, Any]) -> bool:
        verification = note.get("verification")
        if not isinstance(verification, dict):
            return False
        return verification.get("status") in {"TRUSTED", "VERIFIED", "SAFE"}

    @staticmethod
    def _security_metadata(note: Dict[str, Any]) -> Dict[str, Any]:
        return {
            key: note[key]
            for key in SECURITY_FIELDS
            if key in note
        }

    @staticmethod
    def _content_text(value: Any) -> str:
        if isinstance(value, bytes):
            try:
                return zlib.decompress(value).decode("utf-8")
            except (zlib.error, UnicodeDecodeError):
                return value.decode("utf-8", errors="replace")
        return str(value)

    @staticmethod
    def _protected_content(note: Dict[str, Any]) -> bool:
        if bool(note.get("do_not_compress")) or bool(note.get("protected_content")):
            return True
        content = ProgressiveDisclosure._content_text(note.get("content", ""))
        # Textual constraints are protected spans, not a reason to make the
        # entire document indivisible. Budget reduction may compact surrounding
        # content while preserving those spans.
        return bool(
            note.get("do_not_compress")
            or note.get("protected_content")
            or any(key in note for key in ("code", "signature", "dependencies", "identifiers"))
        )

    @staticmethod
    def _protected_lines(content: str) -> List[str]:
        return [
            line for line in content.split("\n")
            if re.search(r"(?i)\b(?:MUST(?: NOT)?|NEVER|SHALL|REQUIRED|FORBIDDEN|DO_NOT_COMPRESS)\b", line)
            or "```" in line
        ]
    def _within_budget(self, usage: int) -> bool:
        try:
            self.budget.check_budget(usage)
            return True
        except Exception:
            return False

    def metadata_only(self, notes: List[Dict[str, Any]]) -> List[Dict[str, Any]]:
        result = []
        usage = 0
        for note in notes:
            if not self._verified(note):
                continue
            entry = {
                "id": note.get("id"),
                "type": note.get("type"),
                "lifecycle": note.get("lifecycle"),
                "confidence": note.get("confidence"),
                "relations": note.get("relations", []),
                **self._security_metadata(note),
            }
            result.append(entry)
            usage += 1
            if not self._within_budget(usage):
                break
        return result

    def snippet(self, notes: List[Dict[str, Any]], chars: int = 200) -> List[Dict[str, Any]]:
        result = []
        usage = 0
        for note in notes:
            if not self._verified(note):
                continue
            content = str(note.get("content", ""))
            protected = self._protected_content(note)
            has_protected_spans = bool(self._protected_lines(content))
            snippet = content if (protected or has_protected_spans) else content[:chars]
            if protected and len(snippet.encode("utf-8")) > self.budget.hard_context_budget:
                raise BudgetExceededError("Protected content exceeds hard disclosure budget")
            entry = {"id": note.get("id"), "snippet": snippet, **self._security_metadata(note)}
            if protected:
                entry["protected_content"] = True
            result.append(entry)
            usage += len(snippet)
            if not self._within_budget(usage):
                break
        return result

    def sections(self, notes: List[Dict[str, Any]], query: str) -> List[Dict[str, Any]]:
        tokens = set(query.lower().split())
        result = []
        usage = 0
        for note in notes:
            if not self._verified(note):
                continue
            content = str(note.get("content", ""))
            lines = content.split("\n")
            matched = [ln for ln in lines if any(tok in ln.lower() for tok in tokens)]
            protected = self._protected_content(note)
            protected_lines = self._protected_lines(content) if protected else []
            if protected:
                selected = list(dict.fromkeys(protected_lines + matched[:5]))
                protected_size = sum(len(line.encode("utf-8")) for line in protected_lines)
                if protected_size > self.budget.hard_context_budget:
                    raise BudgetExceededError("Protected sections exceed hard disclosure budget")
            else:
                selected = matched[:5]
            entry = {"id": note.get("id"), "sections": selected, **self._security_metadata(note)}
            if protected:
                entry["protected_content"] = True
            result.append(entry)
            usage += sum(len(line) for line in selected)
            if not self._within_budget(usage):
                break
        return result

    def full_document(self, notes: List[Dict[str, Any]]) -> List[Dict[str, Any]]:
        result = []
        usage = 0
        for note in notes:
            if not self._verified(note):
                continue
            content = self._content_text(note.get("content", ""))
            size = len(content.encode("utf-8"))
            protected = self._protected_content(note)
            if not self._within_budget(usage + size):
                if protected:
                    raise BudgetExceededError("Protected content exceeds hard disclosure budget")
                continue
            candidate = {
                "id": note.get("id"),
                "content": content,
                **self._security_metadata(note),
            }
            for key in ("compression", "reduction"):
                if key in note:
                    candidate[key] = note[key]
            if protected:
                candidate["protected_content"] = True
            if hasattr(self.budget, "estimate_tokens"):
                if self.budget.estimate_tokens(result + [candidate]) > self.budget.hard_token_budget:
                    if protected:
                        raise BudgetExceededError("Protected content exceeds hard token budget")
                    # Reduction diagnostics are useful but secondary to the
                    # model-facing content and trust/provenance evidence.
                    # Under a tight token budget, keep the compact decision
                    # contract and drop verbose per-item diagnostics first.
                    compact_candidate = dict(candidate)
                    compression = compact_candidate.get("compression")
                    if isinstance(compression, dict):
                        compact_candidate["compression"] = {
                            key: compression[key]
                            for key in (
                                "action",
                                "reason",
                                "original_tokens",
                                "estimated_tokens",
                                "protected_spans",
                                "fallback",
                                "validation",
                                "tokenizer_mode",
                                "net_tokens_saved",
                                "cost_saved",
                                "latency_saved_ms",
                            )
                            if key in compression
                        }
                    reduction = compact_candidate.get("reduction")
                    if isinstance(reduction, dict):
                        compact_candidate["reduction"] = {
                            key: reduction[key]
                            for key in (
                                "token_estimate_before",
                                "token_estimate_after",
                                "tokens_saved",
                            )
                            if key in reduction
                        }
                    if self.budget.estimate_tokens(result + [compact_candidate]) > self.budget.hard_token_budget:
                        minimal_candidate = {"id": compact_candidate.get("id"), "content": compact_candidate.get("content", ""), **self._security_metadata(note), "compression": {"action": compression.get("action", "NO_OP"), "reason": compression.get("reason", "budget_compaction"), "tokenizer_mode": compression.get("tokenizer_mode", "heuristic_fallback"), "protected_spans": compression.get("protected_spans", 0), "net_tokens_saved": compression.get("net_tokens_saved", 0)}}
                        if self.budget.estimate_tokens(result + [minimal_candidate]) > self.budget.hard_token_budget:
                            continue
                        compact_candidate = minimal_candidate
                    candidate = compact_candidate
            result.append(candidate)
            usage += size
        return result

    def provenance_on_demand(self, note_ids: List[str], storage_engine) -> List[Dict[str, Any]]:
        return [storage_engine.get_provenance(nid) for nid in note_ids]
