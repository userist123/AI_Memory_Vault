---
id: aaed3882-4a29-51e5-b7d0-9bb95ac9180a
type: knowledge
lifecycle: REVIEW
category: ai_theory/agents
tags:
- aima
- russell-norvig
- rational-agents
- peas
- heuristic-search
- a-star
- planning
- adversarial-search
- belief-states
created: '2026-09-04'
updated: '2026-09-04'
provenance:
  source_type: ai
  source_ref: 06_INBOX/RAW_IMPORTS/BOOKS/efdd4d1d4c2087fe1cbe03d9ced67f34.pdf
confidence: high
verification: unverified
relations:
- relation: references
  target: 00_CORE/System_Architecture.md
- relation: references
  target: 01_KNOWLEDGE/BOOKS/Agent_Architecture_and_Tool_Orchestration.md
---

# Artificial Intelligence: A Modern Approach — Rational Agents & Search Foundations

**Authors**: Stuart Russell and Peter Norvig (4th Edition)  
**Synthesis Role**: Theoretical Framing of Agent Rationality, Heuristic Search, and Planning  

---

## 1. The Rational Agent & PEAS Framework

A **rational agent** is one that selects an action expected to maximize its performance measure, given the evidence provided by its percept sequence and its built-in knowledge. Rationality is distinct from omniscience (rationality maximizes *expected* performance, not actual post-hoc outcome).

### PEAS Specification
To design an intelligent agent, the task environment must be formally specified via PEAS:
- **P**erformance measure: The metric used to evaluate success (e.g., test accuracy, code validity, latency, context token efficiency).
- **E**nvironment: Fully vs. partially observable, deterministic vs. stochastic, episodic vs. sequential, static vs. dynamic, discrete vs. continuous, single vs. multi-agent.
- **A**ctuators: The interface of actions the agent can perform (e.g. file writing, CLI execution, git commands).
- **S**ensors: The percept stream available to the agent (e.g. user prompt, retrieved memory context, execution error output).

---

## 2. Problem Formulation & Heuristic Search

Search methods systematically explore a state space to find a path from an initial state to a goal state:
- **Completeness**: Guaranteed to find a solution if one exists.
- **Optimality**: Finds the lowest path-cost solution.
- **Time Complexity & Space Complexity**: Critical constraints when branching factor $b$ is high.

### $A^*$ Search & Heuristic Admissibility
$A^*$ evaluates nodes via the evaluation function:
$$f(n) = g(n) + h(n)$$
where $g(n)$ is the exact cost to reach node $n$, and $h(n)$ is the estimated cost from $n$ to the goal.
- **Admissibility**: A heuristic $h(n)$ is admissible if it never overestimates the true cost to reach the goal ($h(n) \le h^*(n)$). Admissibility guarantees optimality in tree search.
- **Consistency (Monotonicity)**: $h(n) \le c(n, a, n') + h(n')$. Consistency guarantees that $f(n)$ is non-decreasing along any path, ensuring that the first time a state is expanded in graph search, its path cost is optimal.

### Bounded Expansion Invariant
In associative memory graphs (such as spreading activation), unconstrained graph traversal induces exponential search space explosion ($O(b^d)$). Bounding search horizons to $d \le 2$ hops with decay attenuation ($decay^h$) prevents runtime runaway and context budget overflow.

---

## 3. Adversarial Search & Multi-Agent Environments

In environments where multiple agents operate competitively or with independent objectives:
- **Minimax Principle**: Optimal decisions under the assumption of an adversarial opponent who minimizes the evaluator's utility.
- **Alpha-Beta Pruning**: Prunes subtrees that are mathematically guaranteed to have no impact on the final decision, reducing effective branching factor from $b$ to $\sqrt{b}$.
- **Belief States**: Under partial observability, the agent maintains a probability distribution over possible world states, updating its belief state upon each new observation.
