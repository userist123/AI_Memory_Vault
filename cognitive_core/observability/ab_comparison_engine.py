import math
from typing import List, Dict, Any, Tuple
from dataclasses import dataclass, field, asdict
from cognitive_core.semantic import DeterministicSemanticProvider

@dataclass
class RankShiftItem:
    note_id: str
    base_rank: int
    base_score: float
    treatment_rank: int
    treatment_score: float
    rank_delta: int
    score_delta: float
    activation_boost: float

@dataclass
class ABComparisonResult:
    query: str
    condition_a: str  # "BASE"
    condition_b: str  # "BASE + ACTIVATION"
    sample_size: int
    top1_flipped: bool
    top1_a: str
    top1_b: str
    kendall_tau: float
    spearman_rho: float
    mean_absolute_rank_delta: float
    items: List[RankShiftItem] = field(default_factory=list)

    def to_dict(self) -> Dict[str, Any]:
        return asdict(self)


class ABComparisonEngine:
    """
    Antigravity Controlled A/B Measurement Engine (R001).
    Evaluates:
    1. BASE (activation = 0) vs BASE + ACTIVATION (ACT-R base level primed)
    2. LIFECYCLE DEGRADATION (ACTIVE vs REVIEW vs SUPERSEDED vs ARCHIVED)
    """
    def __init__(self):
        self.semantic_provider = DeterministicSemanticProvider()

    def compare_base_vs_activation(self,
                                  query: str,
                                  notes: List[Dict[str, Any]],
                                  access_histories: Dict[str, float]) -> ABComparisonResult:
        """
        Runs identical notes under BASE (act weight = 0.0) vs BASE+ACTIVATION (act weight = 0.25).
        """
        # Condition A: Base
        scores_a = []
        for n in notes:
            nid = n.get("id")
            sim = self.semantic_provider.compute_similarity(query, n.get("content", ""))
            conf = 0.4  # baseline
            # Weights: sim=0.50, conf=0.50
            score_a = 0.50 * sim + 0.50 * conf
            scores_a.append((nid, score_a))
        scores_a.sort(key=lambda x: x[1], reverse=True)
        ranks_a = {item[0]: (i + 1, item[1]) for i, item in enumerate(scores_a)}

        # Condition B: Base + Activation
        scores_b = []
        for n in notes:
            nid = n.get("id")
            sim = self.semantic_provider.compute_similarity(query, n.get("content", ""))
            conf = 0.4
            act = access_histories.get(nid, 0.0)
            # Weights: sim=0.40, conf=0.35, activation=0.25
            score_b = 0.40 * sim + 0.35 * conf + 0.25 * act
            scores_b.append((nid, score_b, act))
        scores_b.sort(key=lambda x: x[1], reverse=True)
        ranks_b = {item[0]: (i + 1, item[1], item[2]) for i, item in enumerate(scores_b)}

        # Measure rank shifts
        shift_items = []
        n_items = len(notes)
        sum_abs_delta = 0.0
        d_sq_sum = 0.0

        for nid in [x[0] for x in scores_a]:
            ra, sa = ranks_a[nid]
            rb, sb, act = ranks_b[nid]
            delta = ra - rb  # positive if moved up in B
            sum_abs_delta += abs(delta)
            d_sq_sum += (ra - rb) ** 2
            shift_items.append(RankShiftItem(
                note_id=nid,
                base_rank=ra,
                base_score=sa,
                treatment_rank=rb,
                treatment_score=sb,
                rank_delta=delta,
                score_delta=sb - sa,
                activation_boost=act
            ))

        # Spearman Rho: 1 - 6*sum(d^2) / (n*(n^2 - 1))
        spearman = 1.0
        if n_items > 1:
            spearman = 1.0 - (6.0 * d_sq_sum) / (n_items * (n_items ** 2 - 1))

        # Kendall Tau calculation
        concordant = 0
        discordant = 0
        items_list = list(ranks_a.keys())
        for i in range(len(items_list)):
            for j in range(i + 1, len(items_list)):
                id_i, id_j = items_list[i], items_list[j]
                a_diff = ranks_a[id_i][0] - ranks_a[id_j][0]
                b_diff = ranks_b[id_i][0] - ranks_b[id_j][0]
                if a_diff * b_diff > 0:
                    concordant += 1
                elif a_diff * b_diff < 0:
                    discordant += 1
        total_pairs = (n_items * (n_items - 1)) / 2.0 if n_items > 1 else 1.0
        kendall = (concordant - discordant) / total_pairs

        top1_a = scores_a[0][0] if scores_a else ""
        top1_b = scores_b[0][0] if scores_b else ""

        return ABComparisonResult(
            query=query,
            condition_a="BASE (Activation Weight 0.0)",
            condition_b="BASE + ACTIVATION (Activation Weight 0.25)",
            sample_size=n_items,
            top1_flipped=(top1_a != top1_b),
            top1_a=top1_a,
            top1_b=top1_b,
            kendall_tau=kendall,
            spearman_rho=spearman,
            mean_absolute_rank_delta=sum_abs_delta / n_items if n_items > 0 else 0.0,
            items=shift_items
        )

    def evaluate_lifecycle_degradation(self, query: str, base_content: str) -> Dict[str, float]:
        """
        Measures exact score drop across lifecycle stages on identical content.
        """
        sim = self.semantic_provider.compute_similarity(query, base_content)
        conf = 0.5
        raw_score = 0.6 * sim + 0.4 * conf
        return {
            "ACTIVE": raw_score * 1.0,
            "REVIEW": raw_score * 1.0,  # multiplier 1.0, flagged unverified
            "SUPERSEDED": raw_score * 0.3,
            "ARCHIVED": raw_score * 0.1
        }
