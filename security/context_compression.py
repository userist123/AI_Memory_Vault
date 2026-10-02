"""Adaptive, structure-aware context compression.

Compression is an optimization, never a trust mechanism. Verified content may be
compressed only when the router predicts a likely net benefit.
"""
from __future__ import annotations
import re
from dataclasses import dataclass, field
from typing import Any, Callable, Iterable, Mapping

@dataclass(frozen=True)
class ProtectedSpan:
    text: str
    start: int
    end: int
    reason: str

@dataclass(frozen=True)
class CompressionDecision:
    action: str
    reason: str
    original_tokens: int
    estimated_tokens: int
    redundancy: float
    tokenizer_mode: str = "heuristic_fallback"
    net_tokens_saved: int = 0
    cost_saved: float | None = None
    latency_saved_ms: float | None = None

@dataclass(frozen=True)
class CompressionResult:
    content: str
    decision: CompressionDecision
    protected_spans: tuple[ProtectedSpan, ...] = ()
    removed_segments: tuple[str, ...] = ()
    validation: Mapping[str, Any] = field(default_factory=dict)
    fallback: bool = False

class CompressionRouter:
    def __init__(self, *, min_tokens: int = 800, min_redundancy: float = 0.15,
                 estimated_overhead_tokens: int = 120, chars_per_token: float = 4.0,
                 tokenizer: Callable[[str], int] | None = None,
                 cost_per_input_token: float | None = None,
                 latency_ms_per_input_token: float | None = None) -> None:
        self.min_tokens = max(1, int(min_tokens))
        self.min_redundancy = max(0.0, min(1.0, float(min_redundancy)))
        self.estimated_overhead_tokens = max(0, int(estimated_overhead_tokens))
        self.chars_per_token = max(1.0, float(chars_per_token))
        self.tokenizer = tokenizer
        self.cost_per_input_token = cost_per_input_token
        self.latency_ms_per_input_token = latency_ms_per_input_token

    @property
    def tokenizer_mode(self) -> str:
        return "real" if self.tokenizer is not None else "heuristic_fallback"

    def estimate_tokens(self, text: str) -> int:
        if not text:
            return 0
        if self.tokenizer is not None:
            try:
                value = int(self.tokenizer(text))
            except (TypeError, ValueError, OverflowError) as exc:
                raise ValueError("tokenizer_invalid") from exc
            if value < 0:
                raise ValueError("tokenizer_returned_negative")
            return value
        return max(1, round(len(text) / self.chars_per_token))

    @staticmethod
    def redundancy(text: str) -> float:
        lines = [re.sub(r"\s+", " ", line).strip().casefold() for line in text.splitlines() if line.strip()]
        if not lines:
            return 0.0
        return max(0.0, 1.0 - len(set(lines)) / len(lines))

    def decide(self, text: str, *, query: str = "", do_not_compress: bool = False) -> CompressionDecision:
        original_tokens = self.estimate_tokens(text)
        redundancy = self.redundancy(text)
        if do_not_compress:
            return CompressionDecision("NO_OP", "DO_NOT_COMPRESS", original_tokens, original_tokens, redundancy, self.tokenizer_mode)
        if original_tokens < self.min_tokens:
            return CompressionDecision("NO_OP", "context_below_compression_threshold", original_tokens, original_tokens, redundancy, self.tokenizer_mode)
        if original_tokens <= self.estimated_overhead_tokens:
            return CompressionDecision("NO_OP", "compressor_overhead_not_justified", original_tokens, original_tokens, redundancy, self.tokenizer_mode)
        if redundancy < self.min_redundancy and not query.strip():
            return CompressionDecision("NO_OP", "low_redundancy_without_query", original_tokens, original_tokens, redundancy, self.tokenizer_mode)
        return CompressionDecision("COMPRESS", "long_or_query_relevant_context", original_tokens, original_tokens, redundancy, self.tokenizer_mode)

class ProtectedSpanDetector:
    _PATTERNS = (
        (re.compile(r"\b(?:MUST|MUST NOT|NEVER|SHALL|REQUIRED|FORBIDDEN)\b[^\n]*", re.I), "constraint"),
        (re.compile(r"\`\`\`[\s\S]*?\`\`\`", re.M), "code_block"),
        (re.compile(r"\b(?:sha256|sha1|md5):?[0-9a-f]{16,}\b", re.I), "hash"),
        (re.compile(r"\b(?:v?\d+\.\d+(?:\.\d+)*)\b"), "version"),
        (re.compile(r"(?<![\w-])(?:--[a-z][\w-]*|/[A-Za-z][\w-]*)(?:=[^\s]+)?"), "cli_identifier"),
    )
    def detect(self, text: str) -> tuple[ProtectedSpan, ...]:
        found, occupied = [], []
        for pattern, reason in self._PATTERNS:
            for match in pattern.finditer(text):
                if any(match.start() < end and match.end() > start for start, end in occupied):
                    continue
                found.append(ProtectedSpan(match.group(0), match.start(), match.end(), reason))
                occupied.append((match.start(), match.end()))
        return tuple(sorted(found, key=lambda span: span.start))

class QueryAwareSelector:
    @staticmethod
    def _terms(query: str) -> set[str]:
        return {term.casefold() for term in re.findall(r"[A-Za-z0-9_./:-]{2,}", query)}

    def select(self, text: str, *, query: str, protected: Iterable[ProtectedSpan],
               target_chars: int) -> tuple[str, tuple[str, ...]]:
        segments = [line.strip() for line in text.splitlines() if line.strip()]
        if not segments:
            return text, ()
        terms = self._terms(query)
        protected_text = {span.text.strip() for span in protected}
        protected_segments = {
            segment for segment in segments
            if segment in protected_text or any(segment in span.text for span in protected)
        }
        scored = []
        for index, segment in enumerate(segments):
            lowered = segment.casefold()
            score = sum(1 for term in terms if term in lowered)
            if segment in protected_segments:
                score += 1000
            scored.append((score, -index, segment))
        scored.sort(reverse=True)
        selected, selected_set, size = [], set(), 0
        for score, _, segment in scored:
            if score <= 0 and selected:
                continue
            extra = len(segment) + (1 if selected else 0)
            if size + extra > target_chars and selected:
                continue
            selected.append(segment)
            selected_set.add(segment)
            size += extra
        if not selected:
            selected = segments[:1]
            selected_set.add(selected[0])
        ordered = [segment for segment in segments if segment in selected_set]
        removed = tuple(segment for segment in segments if segment not in selected_set)
        return "\n".join(ordered), removed

class CompressionValidator:
    def validate(self, original: str, compressed: str, protected: Iterable[ProtectedSpan], *,
                 downstream_validator: Callable[[str], bool] | None = None) -> dict[str, Any]:
        protected_list = tuple(protected)
        missing = [span.text for span in protected_list if span.text not in compressed]
        passed_downstream = True
        if downstream_validator is not None:
            try:
                passed_downstream = bool(downstream_validator(compressed))
            except Exception:
                passed_downstream = False
        return {
            "passed": not missing and passed_downstream and bool(compressed.strip()),
            "protected_recall": 1.0 if not protected_list else (len(protected_list) - len(missing)) / len(protected_list),
            "missing_protected": missing,
            "downstream_valid": passed_downstream,
            "original_chars": len(original),
            "compressed_chars": len(compressed),
        }

class AdaptiveContextCompressor:
    def __init__(self, *, router=None, protector=None, selector=None, validator=None) -> None:
        self.router = router or CompressionRouter()
        self.protector = protector or ProtectedSpanDetector()
        self.selector = selector or QueryAwareSelector()
        self.validator = validator or CompressionValidator()

    def compress(self, text: str, *, query: str = "", target_chars: int | None = None,
                 downstream_validator: Callable[[str], bool] | None = None,
                 do_not_compress: bool = False,
                 cost_per_input_token: float | None = None,
                 latency_ms_per_input_token: float | None = None) -> CompressionResult:
        decision = self.router.decide(text, query=query, do_not_compress=do_not_compress)
        protected = self.protector.detect(text)
        if decision.action == "NO_OP":
            validation = self.validator.validate(text, text, protected, downstream_validator=downstream_validator)
            return CompressionResult(text, decision, protected, (), validation, False)
        limit = target_chars or max(1, int(len(text) * 0.65))
        candidate, removed = self.selector.select(text, query=query, protected=protected, target_chars=limit)
        validation = self.validator.validate(text, candidate, protected, downstream_validator=downstream_validator)
        if not validation["passed"]:
            fallback_validation = self.validator.validate(text, text, protected, downstream_validator=downstream_validator)
            return CompressionResult(
                text,
                CompressionDecision("FALLBACK", "compressed_context_failed_validation",
                                    decision.original_tokens, self.router.estimate_tokens(text), decision.redundancy,
                                    decision.tokenizer_mode, 0, None, None),
                protected, removed, fallback_validation, True)
        after = self.router.estimate_tokens(candidate)
        gross_saved = max(0, decision.original_tokens - after)
        net_saved = max(0, gross_saved - self.router.estimated_overhead_tokens)
        cost_rate = cost_per_input_token if cost_per_input_token is not None else self.router.cost_per_input_token
        latency_rate = latency_ms_per_input_token if latency_ms_per_input_token is not None else self.router.latency_ms_per_input_token
        if net_saved <= 0:
            noop = CompressionDecision(
                "NO_OP", "net_benefit_below_overhead", decision.original_tokens, decision.original_tokens,
                decision.redundancy, decision.tokenizer_mode, 0, 0.0, 0.0
            )
            return CompressionResult(text, noop, protected, removed, self.validator.validate(
                text, text, protected, downstream_validator=downstream_validator
            ), False)
        return CompressionResult(
            candidate,
            CompressionDecision("COMPRESS", decision.reason, decision.original_tokens, after, decision.redundancy,
                                decision.tokenizer_mode, net_saved,
                                net_saved * cost_rate if cost_rate is not None else None,
                                net_saved * latency_rate if latency_rate is not None else None),
            protected, removed, validation, False)
