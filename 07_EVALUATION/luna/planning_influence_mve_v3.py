"""
Deterministic Planning Influence MVE Harness V3.

Reconstructed on a strictly isolated control with balanced branch order,
position-independent tie-breaking, explicit verification-as-action modeling,
and multi-scenario parametric accuracy grids.

Zero oracle leakage: the compiler and planner have zero access to scenario optimal/suboptimal.
"""
from __future__ import annotations

from dataclasses import dataclass
from enum import Enum
import hashlib
import math
from typing import Dict, List, Sequence, Tuple
import numpy as np

# ==============================================================================
# FROZEN CONSTANTS FROM PLANNING_INFLUENCE_UNCERTAINTY_POLICY_V1.md (Section 3)
# ==============================================================================
BASE_PRIOR: float = 0.25
INFLUENCE_BUDGET: float = 0.40

APPLICABILITY_STRENGTH: Dict[str, float] = {
    "APPLICABLE": 1.00,
    "APPLICABLE_WITH_VERIFICATION": 0.35,
    "INSUFFICIENTLY_KNOWN": 0.15,
    "NOT_APPLICABLE": 0.00,
}

VALID_CONTRADICTION_STATES = {
    "NONE",
    "POSSIBLE_CONTRADICTION",
    "CONFIRMED_CONTRADICTION",
}


class TerminalStatus(str, Enum):
    RESOLVED = "RESOLVED"
    EXHAUSTED = "EXHAUSTED"


@dataclass(frozen=True)
class Scenario:
    scenario_id: str
    branches_order: Tuple[str, ...]
    _optimal_branch: str
    _suboptimal_branches: Tuple[str, ...]
    _fatal_branches: Tuple[str, ...]
    memory_recommended: str
    memory_applicability: str
    memory_evidence_strength: float
    memory_contradiction_state: str

    @property
    def branches(self) -> Tuple[str, ...]:
        return self.branches_order

    def oracle(self, branch: str) -> Tuple[bool, float, bool]:
        """
        Deterministic execution oracle.
        Returns (is_optimal, reward, is_fatal).
        """
        if branch == self._optimal_branch:
            return True, 1.0, False
        if branch in self._suboptimal_branches:
            return False, 0.25, False
        if branch in self._fatal_branches:
            return False, -1.0, True
        return False, 0.0, False

    def verify_action(self, branch: str) -> bool:
        """
        Isolated verification oracle for explicit verification action.
        Returns True if branch is non-fatal/safe, False if fatal.
        """
        return branch not in self._fatal_branches


@dataclass(frozen=True)
class MemoryInfluenceState:
    memory_id: str
    applicability: str
    evidence_strength: float
    contradiction_state: str
    priors: Dict[str, float]
    source_branch: str
    influence_strength: float
    verification_required: bool
    verification_cost: float


@dataclass(frozen=True)
class PlannerTrace:
    selected_branches: Tuple[str, ...]
    node_visits: int
    fatal_visits: int
    verification_actions: int
    verification_nodes: float
    total_effective_nodes: float
    success: bool
    terminal_status: TerminalStatus


def compile_memory_v3(
    scenario: Scenario,
    policy: str = "v1_uncertainty",
) -> MemoryInfluenceState:
    """
    Compiles memory recommendation into soft priors without oracle leakage.
    Strict adherence to frozen V1 Uncertainty Policy constants.
    """
    recommended = scenario.memory_recommended
    applicability = scenario.memory_applicability
    evidence = float(scenario.memory_evidence_strength)
    contradiction = scenario.memory_contradiction_state
    k = len(scenario.branches)

    if applicability not in APPLICABILITY_STRENGTH:
        raise ValueError(f"Unknown applicability state: {applicability}")
    if contradiction not in VALID_CONTRADICTION_STATES:
        raise ValueError(f"Unknown contradiction state: {contradiction}")

    # Baseline & Advisory policies enforce strictly uniform priors
    if policy in {"baseline", "advisory"}:
        return MemoryInfluenceState(
            memory_id=f"MEM_{scenario.scenario_id}",
            applicability=applicability,
            evidence_strength=evidence,
            contradiction_state=contradiction,
            priors={b: 1.0 / k for b in scenario.branches},
            source_branch=recommended,
            influence_strength=0.0,
            verification_required=False,
            verification_cost=0.0,
        )

    # Confirmed contradiction or NOT_APPLICABLE acts as a safety veto with absolute priority
    if contradiction == "CONFIRMED_CONTRADICTION" or applicability == "NOT_APPLICABLE":
        priors = {b: 1.0 / k for b in scenario.branches}
        influence_strength = 0.0
        verification_required = False
        verification_cost = 0.0
    else:
        app_strength = APPLICABILITY_STRENGTH[applicability]
        verification_required = (applicability == "APPLICABLE_WITH_VERIFICATION")
        verification_cost = 1.0 if verification_required else 0.0
        influence_strength = app_strength * evidence
        base_prior_val = 1.0 / k
        winner_prior = base_prior_val + (INFLUENCE_BUDGET * influence_strength)
        winner_prior = min(winner_prior, 0.95)  # never clamp completely
        loser_mass = 1.0 - winner_prior
        loser_prior = loser_mass / max(1, k - 1)
        priors = {
            b: (winner_prior if b == recommended else loser_prior)
            for b in scenario.branches
        }

    return MemoryInfluenceState(
        memory_id=f"MEM_{scenario.scenario_id}",
        applicability=applicability,
        evidence_strength=evidence,
        contradiction_state=contradiction,
        priors=priors,
        source_branch=recommended,
        influence_strength=influence_strength,
        verification_required=verification_required,
        verification_cost=verification_cost,
    )


def puct_score(q: float, visits: int, parent_visits: int, prior: float, exploration: float) -> float:
    return q + exploration * prior * math.sqrt(max(1, parent_visits)) / (1 + visits)


def get_position_independent_hash(scenario_id: str, candidate: str, step: int, seed: int) -> float:
    """
    Returns a deterministic pseudo-random float in [0, 1) based on SHA-256 hash.
    Completely orthogonal to branch index or position.
    """
    key = f"{scenario_id}:{candidate}:{step}:{seed}".encode("utf-8")
    digest = hashlib.sha256(key).hexdigest()
    # take first 8 hex characters as an integer
    val = int(digest[:8], 16)
    return val / 0xFFFFFFFF


def run_planner_v3(
    scenario: Scenario,
    policy: str = "baseline",
    *,
    rollouts: int = 16,
    exploration: float = 1.414,
    seed: int = 42,
) -> PlannerTrace:
    """
    Runs MCTS/PUCT planner with position-independent tie-breaking and
    optional verification-as-action.
    """
    branches = scenario.branches
    mem_state = compile_memory_v3(scenario, policy=policy)
    priors = mem_state.priors

    verification_actions = 0
    verification_nodes = 0.0

    # Absolute contradiction veto guard: if confirmed contradiction, force uniform priors unconditionally
    if mem_state.contradiction_state == "CONFIRMED_CONTRADICTION":
        priors = {b: 1.0 / len(branches) for b in branches}
    # Verification-as-Action policy: if verification is required, expend verification action first
    elif policy == "v2_verification_action" and mem_state.verification_required:
        verification_actions += 1
        verification_nodes += 1.0  # costs 1.0 node
        is_safe = scenario.verify_action(scenario.memory_recommended)
        if is_safe:
            # Verified safe: upgrade to full APPLICABLE influence
            base_p = 1.0 / len(branches)
            w_prior = base_p + (INFLUENCE_BUDGET * 1.0 * mem_state.evidence_strength)
            w_prior = min(w_prior, 0.95)
            rem_prior = (1.0 - w_prior) / max(1, len(branches) - 1)
            priors = {
                b: (w_prior if b == scenario.memory_recommended else rem_prior)
                for b in branches
            }
        else:
            # Flaw detected: veto memory recommendation completely, revert to uniform baseline
            priors = {b: 1.0 / len(branches) for b in branches}

    visits = {b: 0 for b in branches}
    values = {b: 0.0 for b in branches}
    selected: List[str] = []
    fatal_visits = 0

    for step in range(rollouts):
        parent_visits = sum(visits.values())

        # Select candidate with maximum PUCT score; tie-break orthogonally via SHA-256 hash
        def candidate_key(candidate: str):
            q_val = values[candidate] / visits[candidate] if visits[candidate] > 0 else 0.0
            score = puct_score(
                q_val, visits[candidate], parent_visits, priors[candidate], exploration
            )
            tie_jitter = get_position_independent_hash(scenario.scenario_id, candidate, step, seed)
            return (score, tie_jitter)

        best_branch = max(branches, key=candidate_key)
        selected.append(best_branch)
        visits[best_branch] += 1

        is_optimal, reward, is_fatal = scenario.oracle(best_branch)
        values[best_branch] += reward
        if is_fatal:
            fatal_visits += 1

        if is_optimal:
            total_nodes = len(selected) + verification_nodes
            return PlannerTrace(
                selected_branches=tuple(selected),
                node_visits=len(selected),
                fatal_visits=fatal_visits,
                verification_actions=verification_actions,
                verification_nodes=verification_nodes,
                total_effective_nodes=total_nodes,
                success=True,
                terminal_status=TerminalStatus.RESOLVED,
            )

    total_nodes = len(selected) + verification_nodes
    return PlannerTrace(
        selected_branches=tuple(selected),
        node_visits=len(selected),
        fatal_visits=fatal_visits,
        verification_actions=verification_actions,
        verification_nodes=verification_nodes,
        total_effective_nodes=total_nodes,
        success=False,
        terminal_status=TerminalStatus.EXHAUSTED,
    )


def build_scenarios_v3(
    count: int = 200,
    *,
    num_branches: int = 4,
    num_fatals: int = 2,
    accuracy: float = 0.5,
    applicability_mode: str = "calibrated",
    stale: bool = False,
    seed: int = 42,
) -> List[Scenario]:
    """
    Builds a balanced scenario set where the optimal branch position is
    strictly uniform across all index positions 0..K-1.
    """
    rng = np.random.RandomState(seed)
    scenarios: List[Scenario] = []

    branch_names = tuple(f"strategy_{chr(ord('a') + i)}" for i in range(num_branches))
    num_suboptimal = num_branches - 1 - num_fatals
    if num_suboptimal < 0:
        raise ValueError("Too many fatals for given branch count.")

    for idx in range(count):
        scen_id = f"S{idx + 1:04d}"

        # 1. Balanced assignment of optimal branch position across indices 0..K-1
        opt_idx = idx % num_branches
        optimal_branch = branch_names[opt_idx]

        # Remaining branches
        other_branches = [b for b in branch_names if b != optimal_branch]
        # Shuffle remaining branches deterministically
        rng.shuffle(other_branches)

        suboptimal_branches = tuple(other_branches[:num_suboptimal])
        fatal_branches = tuple(other_branches[num_suboptimal:])

        # 2. Randomize branch order presentation in branches_order
        branches_perm = list(branch_names)
        rng.shuffle(branches_perm)
        branches_order = tuple(branches_perm)

        # 3. Memory recommendation according to accuracy level
        if rng.rand() < accuracy:
            recommended = optimal_branch
        else:
            # Choose uniformly among non-optimal branches
            recommended = rng.choice(other_branches)

        # 4. Applicability & Evidence generation
        is_correct = (recommended == optimal_branch)
        if applicability_mode == "calibrated":
            if is_correct:
                if rng.rand() < 0.70:
                    app = "APPLICABLE"
                    ev = rng.uniform(0.70, 1.00)
                else:
                    app = "APPLICABLE_WITH_VERIFICATION"
                    ev = rng.uniform(0.50, 0.80)
            else:
                roll = rng.rand()
                if roll < 0.20:
                    app = "APPLICABLE_WITH_VERIFICATION"
                    ev = rng.uniform(0.30, 0.60)
                elif roll < 0.70:
                    app = "INSUFFICIENTLY_KNOWN"
                    ev = rng.uniform(0.10, 0.30)
                else:
                    app = "NOT_APPLICABLE"
                    ev = 0.00
        else:
            # Uninformative: uniform over all 4 states
            app = rng.choice([
                "APPLICABLE",
                "APPLICABLE_WITH_VERIFICATION",
                "INSUFFICIENTLY_KNOWN",
                "NOT_APPLICABLE",
            ])
            ev = rng.uniform(0.0, 1.0) if app != "NOT_APPLICABLE" else 0.0

        contradiction = "CONFIRMED_CONTRADICTION" if stale else "NONE"

        scenarios.append(Scenario(
            scenario_id=scen_id,
            branches_order=branches_order,
            _optimal_branch=optimal_branch,
            _suboptimal_branches=suboptimal_branches,
            _fatal_branches=fatal_branches,
            memory_recommended=recommended,
            memory_applicability=app,
            memory_evidence_strength=round(float(ev), 4),
            memory_contradiction_state=contradiction,
        ))

    return scenarios
