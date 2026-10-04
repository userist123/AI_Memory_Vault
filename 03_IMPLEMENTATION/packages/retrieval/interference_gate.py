"""03_IMPLEMENTATION/packages/retrieval/interference_gate.py — Cognitive Interference Gate.

Implements high-precision working memory admission control under indirect cues
and context budget constraints based on ACT-R and Soar cognitive principles.

Key capabilities:
1. ACT-R Base-Level Activation (recency & frequency power-law decay).
2. Cue-driven Activation with Fan Effect suppression.
3. Competitor Density & Proactive Interference suppression.
4. Set-level Maximal Marginal Relevance (MMR) knapsack token budgeting.
5. Confidence margin gating to abstain on unresolved ambiguous memory clusters.
"""
from __future__ import annotations

import math
import time
from dataclasses import dataclass, field
from typing import Any, Callable, Dict, List, Optional, Sequence, Set, Tuple


@dataclass
class CandidateMemory:
    """Represents a candidate memory item evaluated by the gate."""
    id: str
    title: str
    text: str
    reranker_score: float = 0.5
    access_timestamps: List[float] = field(default_factory=list)
    cue_matches: Dict[str, float] = field(default_factory=dict)
    cue_fans: Dict[str, int] = field(default_factory=dict)
    graph_activation: float = 0.0
    embedding: Optional[List[float]] = None
    lifecycle: str = "ACTIVE"
    is_superseded: bool = False
    tokens: int = 100
    metadata: Dict[str, Any] = field(default_factory=dict)


@dataclass
class CandidateGateEvaluation:
    """Detailed score breakdown for a single candidate memory."""
    memory_id: str
    title: str
    reranker_component: float
    base_level_component: float
    cue_component: float
    graph_component: float
    mismatch_penalty: float
    competitor_penalty: float
    temporal_penalty: float
    raw_activation: float
    admitted: bool = False
    set_score: float = 0.0
    exclusion_reason: Optional[str] = None


@dataclass
class InterferenceGateResult:
    """Result returned by the Cognitive Interference Gate."""
    admitted: List[CandidateMemory]
    evaluations: Dict[str, CandidateGateEvaluation]
    total_tokens: int
    execution_time_ms: float
    abstained: bool = False
    abstain_reason: Optional[str] = None


class CognitiveInterferenceGate:
    """Cognitive Interference Gate controlling working memory admission."""

    def __init__(
        self,
        alpha_reranker: float = 1.0,
        beta_base_level: float = 0.3,
        chi_cue: float = 0.4,
        delta_graph: float = 0.3,
        lambda_mismatch: float = 0.5,
        lambda_competitor: float = 0.4,
        lambda_temporal: float = 0.8,
        lambda_redundancy: float = 0.5,
        decay_d: float = 0.5,
        epsilon_decay: float = 0.1,
        b_max: float = 3.0,
        s_max: float = 2.0,
        abs_threshold: float = 0.2,
        margin_threshold: float = 0.05,
        max_tokens: int = 1500,
        max_items: int = 5,
    ) -> None:
        self.alpha_reranker = alpha_reranker
        self.beta_base_level = beta_base_level
        self.chi_cue = chi_cue
        self.delta_graph = delta_graph
        self.lambda_mismatch = lambda_mismatch
        self.lambda_competitor = lambda_competitor
        self.lambda_temporal = lambda_temporal
        self.lambda_redundancy = lambda_redundancy
        self.decay_d = decay_d
        self.epsilon_decay = epsilon_decay
        self.b_max = b_max
        self.s_max = s_max
        self.abs_threshold = abs_threshold
        self.margin_threshold = margin_threshold
        self.max_tokens = max_tokens
        self.max_items = max_items

    def compute_base_level_activation(self, access_timestamps: Sequence[float], current_time: float) -> float:
        """Computes ACT-R base-level activation: B_i = min(B_max, ln(sum (t_now - t_k + eps)^(-d)))."""
        if not access_timestamps:
            return 0.0

        accum = 0.0
        for t_k in access_timestamps:
            delta_t = max(0.0, current_time - t_k)
            accum += (delta_t + self.epsilon_decay) ** (-self.decay_d)

        if accum <= 0.0:
            return 0.0
        return min(self.b_max, math.log(accum))

    def compute_cue_activation(self, cue_matches: Dict[str, float], cue_fans: Dict[str, int]) -> Tuple[float, float]:
        """Computes fan-effect moderated cue support (C_i) and mismatch penalty (M_i)."""
        if not cue_matches:
            return 0.0, 0.0

        c_i = 0.0
        m_i = 0.0
        total_weight = 0.0

        for cue, sim in cue_matches.items():
            fan = max(1, cue_fans.get(cue, 1))
            weight = 1.0
            total_weight += weight
            fan_penalty = math.log(1.0 + fan)
            effective_strength = max(0.0, self.s_max - fan_penalty)
            c_i += weight * sim * effective_strength

            if sim < 0.5:
                m_i += weight * (1.0 - sim)

        if total_weight > 0.0:
            m_i = m_i / total_weight

        return c_i, m_i

    def compute_competitor_density(
        self,
        candidate: CandidateMemory,
        all_candidates: Sequence[CandidateMemory],
        sim_func: Optional[Callable[[CandidateMemory, CandidateMemory], float]] = None,
    ) -> float:
        """Computes competitor density penalty F_i representing proactive interference."""
        if len(all_candidates) <= 1:
            return 0.0

        crowding = 0.0
        mu = 0.7
        tau_s = 0.2

        for other in all_candidates:
            if other.id == candidate.id:
                continue

            if sim_func:
                sim = sim_func(candidate, other)
            elif candidate.embedding and other.embedding:
                sim = self._cosine_sim(candidate.embedding, other.embedding)
            else:
                sim = 0.0

            if sim > mu:
                crowding += math.exp((sim - mu) / tau_s)

        return math.log(1.0 + crowding)

    def _cosine_sim(self, v1: Sequence[float], v2: Sequence[float]) -> float:
        if not v1 or not v2 or len(v1) != len(v2):
            return 0.0
        dot = sum(a * b for a, b in zip(v1, v2))
        norm1 = math.sqrt(sum(a * a for a in v1))
        norm2 = math.sqrt(sum(b * b for b in v2))
        if norm1 <= 0.0 or norm2 <= 0.0:
            return 0.0
        return max(-1.0, min(1.0, dot / (norm1 * norm2)))

    def evaluate_candidates(
        self,
        candidates: Sequence[CandidateMemory],
        current_time: Optional[float] = None,
    ) -> Dict[str, CandidateGateEvaluation]:
        """Evaluates raw activation components for all candidate memories."""
        t_now = current_time if current_time is not None else time.time()
        evals: Dict[str, CandidateGateEvaluation] = {}

        for cand in candidates:
            # 1. Reranker component
            r_comp = self.alpha_reranker * max(0.0, min(1.0, cand.reranker_score))

            # 2. Base level component
            b_act = self.compute_base_level_activation(cand.access_timestamps, t_now)
            b_comp = self.beta_base_level * b_act

            # 3. Cue & mismatch components
            c_act, m_act = self.compute_cue_activation(cand.cue_matches, cand.cue_fans)
            c_comp = self.chi_cue * c_act
            m_comp = self.lambda_mismatch * m_act

            # 4. Graph component
            g_comp = self.delta_graph * max(0.0, cand.graph_activation)

            # 5. Competitor density
            f_act = self.compute_competitor_density(cand, candidates)
            f_comp = self.lambda_competitor * f_act

            # 6. Temporal penalty
            t_comp = 0.0
            if cand.is_superseded:
                t_comp += self.lambda_temporal * 1.0
            if cand.lifecycle == "DEPRECATED":
                t_comp += self.lambda_temporal * 0.8
            elif cand.lifecycle == "REVIEW":
                t_comp += self.lambda_temporal * 0.2

            # Combined raw activation
            raw_act = r_comp + b_comp + c_comp + g_comp - m_comp - f_comp - t_comp

            evals[cand.id] = CandidateGateEvaluation(
                memory_id=cand.id,
                title=cand.title,
                reranker_component=r_comp,
                base_level_component=b_comp,
                cue_component=c_comp,
                graph_component=g_comp,
                mismatch_penalty=m_comp,
                competitor_penalty=f_comp,
                temporal_penalty=t_comp,
                raw_activation=raw_act,
            )

        return evals

    def select(
        self,
        candidates: Sequence[CandidateMemory],
        current_time: Optional[float] = None,
    ) -> InterferenceGateResult:
        """Executes admission gating with MMR knapsack token budgeting."""
        start_time = time.perf_counter()
        if not candidates:
            return InterferenceGateResult(
                admitted=[],
                evaluations={},
                total_tokens=0,
                execution_time_ms=(time.perf_counter() - start_time) * 1000.0,
            )

        evals = self.evaluate_candidates(candidates, current_time=current_time)
        cand_by_id = {c.id: c for c in candidates}

        # Filter by absolute threshold
        viable_ids = [
            cid for cid, ev in evals.items()
            if ev.raw_activation >= self.abs_threshold
        ]

        for cid, ev in evals.items():
            if cid not in viable_ids:
                ev.exclusion_reason = "SUB_THRESHOLD"

        if not viable_ids:
            return InterferenceGateResult(
                admitted=[],
                evaluations=evals,
                total_tokens=0,
                execution_time_ms=(time.perf_counter() - start_time) * 1000.0,
            )

        # Sort viable candidates by raw activation descending
        viable_ids.sort(key=lambda cid: evals[cid].raw_activation, reverse=True)

        # Confidence margin check for top ambiguous pair
        if len(viable_ids) >= 2:
            top_1 = evals[viable_ids[0]]
            top_2 = evals[viable_ids[1]]
            diff = top_1.raw_activation - top_2.raw_activation
            c1 = cand_by_id[viable_ids[0]]
            c2 = cand_by_id[viable_ids[1]]
            sim_pair = 0.0
            if c1.embedding and c2.embedding:
                sim_pair = self._cosine_sim(c1.embedding, c2.embedding)

            # If two candidates are highly similar but unresolved margin
            if sim_pair > 0.85 and diff < self.margin_threshold:
                return InterferenceGateResult(
                    admitted=[],
                    evaluations=evals,
                    total_tokens=0,
                    execution_time_ms=(time.perf_counter() - start_time) * 1000.0,
                    abstained=True,
                    abstain_reason=f"AMBIGUOUS_COMPETING_CLUSTER: diff={diff:.4f} < margin={self.margin_threshold}",
                )

        # Iterative MMR selection under token budget
        admitted: List[CandidateMemory] = []
        admitted_ids: Set[str] = set()
        consumed_tokens = 0
        pool_ids = set(viable_ids)

        while pool_ids and len(admitted) < self.max_items:
            best_id: Optional[str] = None
            best_set_score = -float("inf")

            for cid in pool_ids:
                cand = cand_by_id[cid]
                if consumed_tokens + cand.tokens > self.max_tokens:
                    continue

                raw_score = evals[cid].raw_activation

                # Redundancy penalty against already admitted items
                max_redundancy = 0.0
                for adm in admitted:
                    if cand.embedding and adm.embedding:
                        sim = self._cosine_sim(cand.embedding, adm.embedding)
                    else:
                        sim = 0.0
                    if sim > max_redundancy:
                        max_redundancy = sim

                set_score = raw_score - (self.lambda_redundancy * max_redundancy)

                if set_score > best_set_score:
                    best_set_score = set_score
                    best_id = cid

            if best_id is None:
                # No more candidates fit token budget
                for cid in pool_ids:
                    if not evals[cid].exclusion_reason:
                        evals[cid].exclusion_reason = "BUDGET_EXCEEDED"
                break

            chosen = cand_by_id[best_id]
            admitted.append(chosen)
            admitted_ids.add(best_id)
            consumed_tokens += chosen.tokens
            pool_ids.remove(best_id)

            evals[best_id].admitted = True
            evals[best_id].set_score = best_set_score

        # Mark non-admitted viable items
        for cid in viable_ids:
            if cid not in admitted_ids and not evals[cid].exclusion_reason:
                evals[cid].exclusion_reason = "KNAPSACK_CUT"

        elapsed_ms = (time.perf_counter() - start_time) * 1000.0

        return InterferenceGateResult(
            admitted=admitted,
            evaluations=evals,
            total_tokens=consumed_tokens,
            execution_time_ms=elapsed_ms,
        )
