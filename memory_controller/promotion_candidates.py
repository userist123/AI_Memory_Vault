"""memory_controller/promotion_candidates.py — Human-Gated Promotion and Retirement Candidate Recommender.

Identifies capability promotion and retirement candidates for human review
based on empirical runtime effectiveness (Wilson lower bounds, trend analysis,
and anti-gaming project dominance caps).

Core Invariants:
1. METRIC -> CANDIDATE, NOT METRIC -> AUTOMATIC ACTION:
   Never modifies, deletes, promotes, or quarantines capabilities automatically.
   All outputs require human attestation and review (status: "REVIEW_REQUIRED").
2. Statistical Rigor:
   Uses Wilson lower bounds from `memory_controller.capability_effectiveness` (not raw observed rates).
3. Trend Safety:
   Promotion requires >= 2 valid categories with Wilson > 0.85 and NO degrading trends.
4. Anti-Gaming (Project Dominance):
   If any single project accounts for > 40% of observations for a capability cell,
   it is flagged with `PROJECT_DOMINANCE` and blocked from promotion candidacy.
5. Strict Observed Evidence:
   Authoritative capability observations derive exclusively from `ObservedMemoryTrace.retrieved_memory_ids`.
6. Run-Level Deduplication:
   1 run_id contributes at most once per (capability_type, capability_id, task_category).
"""
from __future__ import annotations

import copy
from collections import defaultdict
from dataclasses import asdict, dataclass, field
from pathlib import Path
from typing import Any, Dict, List, Optional, Set, Tuple

from memory_controller.capability_effectiveness import (
    effectiveness_matrix,
    effectiveness_trend,
    normalize_capability_type,
    _classify_memory_id,
    _extract_run_capabilities,
)
from memory_controller.effectiveness_stats import MIN_SAMPLE_SIZE
from memory_controller.memory_trace import ObservedMemoryTrace, load_observed_memory_traces
from memory_controller.outcome_tracker import OutcomeRecord, OutcomeTracker
from memory_controller.task_categories import VALID_TASK_CATEGORIES, validate_task_category

PROMOTION_THRESHOLD: float = 0.85
RETIREMENT_THRESHOLD: float = 0.40
MIN_CATEGORIES_FOR_DECISION: int = 2
PROJECT_USAGE_CAP: float = 0.40


@dataclass
class CandidateCategoryMetric:
    """Detailed category-level metrics for candidate evaluation."""

    task_category: str
    total_runs: int
    success_runs: int
    observed_rate: float
    wilson_lower_bound: float
    smoothed_rate: float
    status: str
    trend: str
    project_dominance: bool
    max_project_share: float
    project_breakdown: Dict[str, int] = field(default_factory=dict)

    def to_dict(self) -> Dict[str, Any]:
        return asdict(self)


@dataclass
class CandidateEvaluation:
    """Evaluation summary for a capability recommendation."""

    capability_type: str
    capability_id: str
    eligible_categories: List[str]
    category_metrics: Dict[str, Dict[str, Any]]
    overall_reason: str
    status: str = "REVIEW_REQUIRED"
    recommendation_type: str = "NONE"  # PROMOTION_CANDIDATE, RETIREMENT_CANDIDATE, BLOCKED_PROMOTION

    def to_dict(self) -> Dict[str, Any]:
        return asdict(self)


def _compute_cell_project_distributions(
    outcome_records: List[OutcomeRecord],
    traces: List[ObservedMemoryTrace],
    project_id: Optional[str] = None,
) -> Dict[Tuple[str, str, str], Dict[str, int]]:
    """Calculates run counts per project for each (capability_type, capability_id, task_category) cell.

    Preserves 1-run-1-observation deduplication invariant.
    """
    outcomes_by_run: Dict[str, OutcomeRecord] = {r.run_id: r for r in outcome_records}
    traces_by_run: Dict[str, List[ObservedMemoryTrace]] = defaultdict(list)
    for t in traces:
        traces_by_run[t.run_id].append(t)

    all_run_ids = set(outcomes_by_run.keys()) | set(traces_by_run.keys())

    # Map: (c_type, c_id, task_category) -> {proj_id: run_count}
    cell_proj_counts: Dict[Tuple[str, str, str], Dict[str, int]] = defaultdict(lambda: defaultdict(int))

    for r_id in sorted(list(all_run_ids)):
        rec = outcomes_by_run.get(r_id)
        run_traces = traces_by_run.get(r_id, [])

        cat = rec.task_category if rec else "unknown"
        if not cat or cat not in VALID_TASK_CATEGORIES:
            cat = "unknown"

        # Determine effective project ID for this run
        proj = None
        if rec and rec.project_id:
            proj = str(rec.project_id).strip()
        elif run_traces and run_traces[0].project_id:
            proj = str(run_traces[0].project_id).strip()
        else:
            proj = "unassigned"

        if project_id is not None and proj != project_id:
            continue

        # Extract capabilities strictly from ObservedMemoryTrace
        run_caps = _extract_run_capabilities(run_traces)

        for c_type, ids in run_caps.items():
            for c_id in ids:
                cell_proj_counts[(c_type, c_id, cat)][proj] += 1

    return cell_proj_counts


def flag_review_candidates(
    outcome_records: Optional[List[OutcomeRecord]] = None,
    traces: Optional[List[ObservedMemoryTrace]] = None,
    project_id: Optional[str] = None,
    promotion_threshold: float = PROMOTION_THRESHOLD,
    retirement_threshold: float = RETIREMENT_THRESHOLD,
    min_categories: int = MIN_CATEGORIES_FOR_DECISION,
    project_usage_cap: float = PROJECT_USAGE_CAP,
    confidence: float = 0.95,
    telemetry_dir: Optional[Path | str] = None,
    outcomes_ledger: Optional[Path | str] = None,
    traces_ledger: Optional[Path | str] = None,
) -> Dict[str, Any]:
    """Evaluates observed capability metrics and flags candidates for human review.

    This function is purely analytical and does NOT mutate records or execute actions.

    Returns:
        Dictionary containing:
        - "promotion_candidates": List of capabilities qualifying for promotion review
        - "retirement_candidates": List of capabilities qualifying for retirement review
        - "blocked_candidates": List of capabilities near promotion threshold but blocked by trend or dominance
        - "metadata": Execution parameters, sample thresholds, and review guidance
    """
    # 1. Load outcome records and traces if not provided directly
    if outcome_records is None:
        if outcomes_ledger:
            tracker = OutcomeTracker(ledger_path=Path(outcomes_ledger))
        else:
            base = Path(telemetry_dir) if telemetry_dir else Path("telemetry")
            tracker = OutcomeTracker(ledger_path=base / "outcomes" / "council_outcomes.jsonl")
        outcome_records = tracker.list_records(project_id=project_id)
    elif project_id is not None:
        outcome_records = [r for r in outcome_records if r.project_id == project_id]

    if traces is None:
        t_dir = Path(telemetry_dir) if telemetry_dir else None
        traces = load_observed_memory_traces(
            project_id=project_id,
            telemetry_dir=t_dir,
        )
    elif project_id is not None:
        traces = [t for t in traces if t.project_id == project_id]

    # 2. Compute base capability effectiveness matrix
    matrix_res = effectiveness_matrix(
        outcome_records=outcome_records,
        traces=traces,
        project_id=project_id,
        confidence=confidence,
    )
    raw_matrix = matrix_res["matrix"]

    # 3. Compute project usage distributions for anti-gaming checks
    proj_distributions = _compute_cell_project_distributions(
        outcome_records=outcome_records,
        traces=traces,
        project_id=project_id,
    )

    # 4. Group matrix cells by (capability_type, capability_id)
    grouped_cells: Dict[Tuple[str, str], Dict[str, Dict[str, Any]]] = defaultdict(dict)
    for cell_key, cell_data in raw_matrix.items():
        c_type = cell_data["capability_type"]
        c_id = cell_data["capability_id"]
        cat = cell_data["task_category"]
        grouped_cells[(c_type, c_id)][cat] = cell_data

    promotion_candidates: List[Dict[str, Any]] = []
    retirement_candidates: List[Dict[str, Any]] = []
    blocked_candidates: List[Dict[str, Any]] = []

    # 5. Evaluate each capability across its observed categories
    for (c_type, c_id), cat_dict in sorted(grouped_cells.items()):
        category_metrics: Dict[str, Dict[str, Any]] = {}
        valid_promotion_cats: List[str] = []
        valid_retirement_cats: List[str] = []
        degrading_cats: List[str] = []
        dominant_project_cats: List[str] = []

        for cat, cell in sorted(cat_dict.items()):
            tot = cell["total_runs"]
            status = cell["status"]
            wilson = cell["wilson_lower_bound"]

            # Compute trend for this capability in this category
            trend_res = effectiveness_trend(
                capability_type=c_type,
                capability_id=c_id,
                task_category=cat,
                outcome_records=outcome_records,
                traces=traces,
                project_id=project_id,
            )
            cat_trend = trend_res["trend"]

            # Compute project dominance for anti-gaming
            proj_breakdown = proj_distributions.get((c_type, c_id, cat), {})
            max_share = 0.0
            project_dominance = False
            if tot > 0 and proj_breakdown:
                max_proj_runs = max(proj_breakdown.values())
                max_share = max_proj_runs / float(tot)
                if max_share > project_usage_cap:
                    project_dominance = True

            cat_metric = CandidateCategoryMetric(
                task_category=cat,
                total_runs=tot,
                success_runs=cell["success_runs"],
                observed_rate=cell["observed_rate"],
                wilson_lower_bound=wilson,
                smoothed_rate=cell["smoothed_rate"],
                status=status,
                trend=cat_trend,
                project_dominance=project_dominance,
                max_project_share=round(max_share, 4),
                project_breakdown=dict(proj_breakdown),
            )
            category_metrics[cat] = cat_metric.to_dict()

            # Filter candidates based strictly on VALID status and sample size
            if status == "VALID":
                # Promotion candidate check: Wilson > threshold (strictly greater than)
                if wilson > promotion_threshold:
                    if not project_dominance and cat_trend != "DEGRADING":
                        valid_promotion_cats.append(cat)
                    if project_dominance:
                        dominant_project_cats.append(cat)
                    if cat_trend == "DEGRADING":
                        degrading_cats.append(cat)

                # Retirement candidate check: Wilson < threshold (strictly less than)
                if wilson < retirement_threshold:
                    valid_retirement_cats.append(cat)

        # Evaluate promotion qualification
        if len(valid_promotion_cats) >= min_categories:
            eval_record = CandidateEvaluation(
                capability_type=c_type,
                capability_id=c_id,
                eligible_categories=valid_promotion_cats,
                category_metrics=category_metrics,
                overall_reason=(
                    f"Meets promotion criteria in {len(valid_promotion_cats)} categories "
                    f"(Wilson lower bound > {promotion_threshold:.2f}, no degrading trends, "
                    f"balanced project distribution)."
                ),
                status="REVIEW_REQUIRED",
                recommendation_type="PROMOTION_CANDIDATE",
            )
            promotion_candidates.append(eval_record.to_dict())
        else:
            # Check if blocked by project dominance or degrading trend despite high Wilson
            potential_high_cats = [
                c for c, m in category_metrics.items()
                if m["status"] == "VALID" and m["wilson_lower_bound"] > promotion_threshold
            ]
            if len(potential_high_cats) >= min_categories and (degrading_cats or dominant_project_cats):
                reasons = []
                if degrading_cats:
                    reasons.append(f"degrading trend in {degrading_cats}")
                if dominant_project_cats:
                    reasons.append(f"project dominance (> {project_usage_cap:.0%}) in {dominant_project_cats}")
                eval_record = CandidateEvaluation(
                    capability_type=c_type,
                    capability_id=c_id,
                    eligible_categories=potential_high_cats,
                    category_metrics=category_metrics,
                    overall_reason=f"Promotion blocked due to: {', '.join(reasons)}.",
                    status="REVIEW_REQUIRED",
                    recommendation_type="BLOCKED_PROMOTION",
                )
                blocked_candidates.append(eval_record.to_dict())

        # Evaluate retirement qualification
        if len(valid_retirement_cats) >= min_categories:
            eval_record = CandidateEvaluation(
                capability_type=c_type,
                capability_id=c_id,
                eligible_categories=valid_retirement_cats,
                category_metrics=category_metrics,
                overall_reason=(
                    f"Underperforming in {len(valid_retirement_cats)} categories "
                    f"(Wilson lower bound < {retirement_threshold:.2f} with adequate sample size)."
                ),
                status="REVIEW_REQUIRED",
                recommendation_type="RETIREMENT_CANDIDATE",
            )
            retirement_candidates.append(eval_record.to_dict())

    return {
        "promotion_candidates": promotion_candidates,
        "retirement_candidates": retirement_candidates,
        "blocked_candidates": blocked_candidates,
        "summary": {
            "total_capabilities_evaluated": len(grouped_cells),
            "promotion_candidates_count": len(promotion_candidates),
            "retirement_candidates_count": len(retirement_candidates),
            "blocked_candidates_count": len(blocked_candidates),
            "action_taken": "NONE (Recommendations for human review only)",
        },
        "metadata": {
            "project_id": project_id,
            "promotion_threshold": promotion_threshold,
            "retirement_threshold": retirement_threshold,
            "min_categories": min_categories,
            "project_usage_cap": project_usage_cap,
            "confidence": confidence,
            "min_sample_size": MIN_SAMPLE_SIZE,
            "human_gated": True,
        },
    }
