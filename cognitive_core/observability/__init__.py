"""
Cognitive Core Observability & Developer Traceability Package (Antigravity R001)

Provides non-intrusive retrieval tracing across all 14 pipeline stages,
score distribution benchmarking, controlled A/B experimentation
(Base vs Base+Activation, Lifecycle impacts), and memory-use to outcome mapping.
"""

from .retrieval_tracer import (
    RetrievalTracer,
    RetrievalTrace,
    StageTrace,
    CandidateTraceStage
)
from .ab_comparison_engine import (
    ABComparisonEngine,
    ABComparisonResult
)
from .memory_outcome_tracer import (
    MemoryOutcomeTracer,
    MemoryOutcomeLinkage,
    MemoryUtilityTier
)

__all__ = [
    "RetrievalTracer",
    "RetrievalTrace",
    "StageTrace",
    "CandidateTraceStage",
    "ABComparisonEngine",
    "ABComparisonResult",
    "MemoryOutcomeTracer",
    "MemoryOutcomeLinkage",
    "MemoryUtilityTier"
]
