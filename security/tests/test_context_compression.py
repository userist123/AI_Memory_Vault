"""Tests for adaptive prompt/context compression."""
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
if str(ROOT) not in sys.path:
    sys.path.insert(0, str(ROOT))

from security.context_compression import AdaptiveContextCompressor, CompressionRouter, ProtectedSpanDetector


def test_short_context_is_no_op():
    router = CompressionRouter(min_tokens=100)
    result = AdaptiveContextCompressor(router=router).compress("short context")
    assert result.decision.action == "NO_OP"
    assert result.content == "short context"


def test_query_aware_compression_keeps_protected_constraint():
    text = "\n".join([
        "irrelevant architecture detail " * 4,
        "MUST NOT bypass the security gate.",
        "token economy is measured by final model input.",
        "unrelated deployment history " * 4,
    ])
    result = AdaptiveContextCompressor(
        router=CompressionRouter(min_tokens=10, min_redundancy=0.0),
    ).compress(text, query="token economy")
    assert result.decision.action == "COMPRESS"
    assert "MUST NOT bypass the security gate." in result.content
    assert result.validation["protected_recall"] == 1.0


def test_failed_validation_falls_back_to_original():
    text = "\n".join(["token economy " * 10, "MUST preserve this constraint."])
    result = AdaptiveContextCompressor(
        router=CompressionRouter(min_tokens=5, min_redundancy=0.0),
    ).compress(text, query="token", downstream_validator=lambda _: False)
    assert result.decision.action == "FALLBACK"
    assert result.fallback is True
    assert result.content == text


def test_protected_detector_marks_code_and_versions():
    text = "Use v2.4.1 and run --safe-mode."
    code = "python print('x')"
    text = text + "\n" + code
    spans = ProtectedSpanDetector().detect(text)
    reasons = {span.reason for span in spans}
    assert "version" in reasons
    assert "cli_identifier" in reasons


def test_do_not_compress_is_hard_no_op():
    text = "\n".join(["repeated context " * 50, "MUST NOT bypass security."])
    result = AdaptiveContextCompressor(
        router=CompressionRouter(min_tokens=5, min_redundancy=0.0),
    ).compress(text, do_not_compress=True)
    assert result.decision.action == "NO_OP"
    assert result.decision.reason == "DO_NOT_COMPRESS"
    assert result.content == text
    assert result.decision.net_tokens_saved == 0


def test_real_tokenizer_is_used_when_supplied():
    tokenizer = lambda value: len(value.split())
    router = CompressionRouter(min_tokens=3, min_redundancy=0.0, tokenizer=tokenizer)
    result = AdaptiveContextCompressor(router=router).compress(
        ("alpha beta gamma\n" * 6) + "delta epsilon zeta",
        query="alpha",
    )
    assert result.decision.tokenizer_mode == "real"


def test_net_cost_and_latency_savings_are_reported():
    tokenizer = lambda value: len(value.split())
    router = CompressionRouter(
        min_tokens=3, min_redundancy=0.0, tokenizer=tokenizer,
        cost_per_input_token=0.01, latency_ms_per_input_token=2.0,
    )
    result = AdaptiveContextCompressor(router=router).compress(
        "alpha beta gamma\nalpha beta gamma\nalpha beta gamma\ndelta epsilon zeta",
        query="alpha",
        target_chars=40,
    )
    assert result.decision.net_tokens_saved >= 0
    assert result.decision.cost_saved is not None
    assert result.decision.latency_saved_ms is not None

def test_compression_with_insufficient_net_benefit_is_no_op():
    tokenizer = lambda value: len(value.split())
    router = CompressionRouter(
        min_tokens=3, min_redundancy=0.0, tokenizer=tokenizer,
        estimated_overhead_tokens=19,
    )
    result = AdaptiveContextCompressor(router=router).compress(
        "alpha beta gamma\nalpha beta gamma\ndelta epsilon zeta",
        query="alpha",
        target_chars=20,
    )
    assert result.decision.action == "NO_OP"
    assert result.decision.reason == "net_benefit_below_overhead"
    assert result.content == "alpha beta gamma\nalpha beta gamma\ndelta epsilon zeta"
    assert result.decision.net_tokens_saved == 0
