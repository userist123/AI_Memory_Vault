# Pre-Registration: Polymarket CLOB Corpus v1 Empirical Study

> **Registration Document**: `07_EVALUATION/polymarket/research/PREREGISTRATION.md`  
> **Status**: PRE-REGISTERED (Committed prior to any outcome-linking or hypothesis testing)  
> **Commit Sequence Gate**: Must be committed and pushed to git before running any script linking market prices to settlement outcomes.  
> **Execution Runtime**: Strictly offline reproducible via `python 07_EVALUATION/polymarket/research/run_all.py`.

---

## 1. Executive Summary & Epistemic Contract

This pre-registration defines the formal empirical study evaluating whether pricing across the Polymarket Central Limit Order Book (CLOB) exhibits exploitable structural inefficiencies, miscalibration, or arbitrage opportunities, or conversely whether the market demonstrates weak-form informational efficiency across its primary contract domains.

To eliminate p-hacking, outcome fishing, researcher degrees of freedom, and post-hoc rationalization, all hypotheses, cluster independence units, statistical tests, multiple comparison corrections, confidence intervals, stopping rules, and Minimum Relevant Effect Sizes (MRES) are locked herein.

---

## 2. Hypotheses (H1–H6) Specifications

The study evaluates six primary hypotheses. Any empirical finding failing to clear both the statistical threshold (Holm-Bonferroni adjusted $p \le \alpha$) and the minimum relevant effect size (MRES) is declared **NULL** and presented as such.

```
       Hypotheses Family (Family Size M = 6, Alpha = 0.05)
 ┌─────────────────────────────────────────────────────────────┐
 │ H1: Calibration divergence across contract categories       │
 │ H2: Favorite–longshot pricing bias                          │
 │ H3: Horizon dependence of pricing accuracy (T-24h to T-10m)  │
 │ H4: Crypto threshold grid strike monotonicity violations    │
 │ H5: Single-game lines/spreads/totals monotonicity           │
 │ H6: Liquidity / quote depth vs. calibration error           │
 └─────────────────────────────────────────────────────────────┘
```

---

### H1: Calibration Discrepancy Across Categories

- **Formal Claim**: Calibration accuracy and directional pricing bias differ systematically across contract categories:
  1. Pre-match sports (`sports_pre_match`)
  2. In-play sports (`sports_in_play`)
  3. Crypto price thresholds (`crypto_threshold`)
  4. Event / macro / political / culture markets (`event_market`)
- **Metric & Formula**:
  - For each category $c \in C$ and market/contract $i \in I_c$:
    - Entry price $p_i \in (0, 1)$ taken from the first usable pre-cutoff quote.
    - Binary outcome $y_i \in \{0, 1\}$.
    - Mean Signed Deviation (Bias):
      $$\text{Bias}_c = \frac{1}{|I_c|} \sum_{i \in I_c} (y_i - p_i)$$
    - Weighted Absolute Calibration Error (WCE):
      $$\text{WCE}_c = \sum_{b=1}^{B} \frac{|I_{c,b}|}{|I_c|} \left| \bar{y}_{c,b} - \bar{p}_{c,b} \right|$$
      over 10 uniform probability bins $b \in \{[0.0, 0.1), [0.1, 0.2), \dots, [0.9, 1.0]\}$.
- **Unit of Independence**:
  - `game_key` for sports (match identifier).
  - `underlying_key` (`ASSET|DATE|HOUR`) for crypto.
  - `event_key` (event slug/ID) for event markets.
- **Statistical Test**:
  - Pairwise category difference in absolute bias $|\text{Bias}_{c_1} - \text{Bias}_{c_2}|$ evaluated via clustered bootstrap resampling ($B=2,000$, `seed=42`) at the independence unit level.
- **Minimum Relevant Effect Size (MRES)**:
  - Difference $\ge 0.050$ (5.0 percentage points).
- **Null Definition**:
  - If $95\%$ bootstrap CI covers $[-0.050, +0.050]$ or Holm-adjusted $p > 0.05$, the difference is declared **NULL** (categories share equivalent calibration within bounds).

---

### H2: Favorite–Longshot Bias (FLB)

- **Formal Claim**: Markets systematically misprice extreme probabilities: low-probability contracts ($p < 0.15$) win less frequently than implied ($y < p$, negative bias), while high-probability contracts ($p > 0.85$) win more frequently than implied ($y > p$, positive bias).
- **Metric & Formula**:
  - Low-price basket $L = \{i : p_i \in [0.01, 0.15)\}$:
    $$\text{Bias}_L = \frac{1}{|L|} \sum_{i \in L} (y_i - p_i)$$
  - High-price basket $H = \{i : p_i \in (0.85, 0.99]\}$:
    $$\text{Bias}_H = \frac{1}{|H|} \sum_{i \in H} (y_i - p_i)$$
  - Mid-price reference basket $M = \{i : p_i \in [0.40, 0.60]\}$:
    $$\text{Bias}_M = \frac{1}{|M|} \sum_{i \in M} (y_i - p_i)$$
- **Unit of Independence**:
  - Cluster key (`game_key`, `underlying_key`, `event_key`).
- **Statistical Test**:
  - Clustered bootstrap test of $\text{Bias}_L < 0$ and $\text{Bias}_H > 0$ with 2,000 resamples.
- **Minimum Relevant Effect Size (MRES)**:
  - $|\text{Bias}_L| \ge 0.030$ (3.0 percentage points) AND $|\text{Bias}_H| \ge 0.030$.
- **Null Definition**:
  - If bootstrap CI of $\text{Bias}_L$ contains 0 or upper bound $> -0.030$, or if Holm-adjusted $p > 0.05$, FLB is declared **NULL** (Polymarket pricing does not exhibit exploitable favorite-longshot bias).

---

### H3: Horizon Dependence of Calibration Error

- **Formal Claim**: Price forecast accuracy improves monotonically as time-to-close decreases across standard horizons:
  - $h_1 = T - 24\text{ hours}$
  - $h_2 = T - 6\text{ hours}$
  - $h_3 = T - 1\text{ hour}$
  - $h_4 = T - 10\text{ minutes}$
- **Metric & Formula**:
  - Brier Score at horizon $h$:
    $$\text{BS}(h) = \frac{1}{N_h} \sum_{i=1}^{N_h} (p_i(h) - y_i)^2$$
  - Horizon Delta:
    $$\Delta\text{BS}(h_a, h_b) = \text{BS}(h_a) - \text{BS}(h_b)$$
- **Unit of Independence**:
  - Cluster key (`game_key`, `underlying_key`, `event_key`).
- **Statistical Test**:
  - Clustered bootstrap CI on $\Delta\text{BS}(h_a, h_{a+1})$.
- **Minimum Relevant Effect Size (MRES)**:
  - Brier score improvement $\ge 0.020$ (2.0 Brier points) between consecutive horizons.
- **Null Definition**:
  - If $\Delta\text{BS} < 0.020$ or monotonic ordering is violated, the hypothesis of monotonic horizon resolution is declared **NULL** (prices remain static or early noise matches late noise).

---

### H4: Crypto Threshold Grid Monotonicity Violations

- **Formal Claim**: At any identical observation timestamp $t$, for an underlying crypto asset with threshold strike contracts $K_1 < K_2$ expiring at time $T$, the law of one price requires:
  $$p(S_T > K_2, t) \le p(S_T > K_1, t)$$
- **Metric & Formula**:
  - Instantaneous Inversion:
    $$\text{Violation}_{\text{gross}}(K_1, K_2, t) = \max\left(0, p(K_2, t) - p(K_1, t)\right)$$
  - Net Arbitrage Profitability (accounting for transaction frictions):
    $$\text{Friction}(K_1, K_2, t) = \frac{\text{Spread}(K_1, t) + \text{Spread}(K_2, t)}{2} + \text{MinTick}$$
    $$\text{Violation}_{\text{net}}(K_1, K_2, t) = \max\left(0, \text{Violation}_{\text{gross}}(K_1, K_2, t) - \text{Friction}(K_1, K_2, t)\right)$$
  - Inversion Frequency: fraction of synchronous time slices exhibiting $\text{Violation}_{\text{gross}} > 0$ and $\text{Violation}_{\text{net}} > 0$.
- **Unit of Independence**:
  - `underlying_key` (`ASSET|DATE|HOUR`).
- **Statistical Test**:
  - Cluster bootstrap on mean net arbitrage profit per hourly expiration window.
- **Minimum Relevant Effect Size (MRES)**:
  - Net executable violation rate $\ge 0.5\%$ of timestamps with mean net profit $\ge \$0.010$ (1.0 cent/share).
- **Null Definition**:
  - If net violations occur in $< 0.5\%$ of quotes or net profit after spread and tick is $\le 0$, the crypto grid is declared **ARBITRAGE-FREE / STRUCTURALLY MONOTONIC** (inversions are sub-tick or friction-dominated noise).

---

### H5: Single-Game Line Monotonicity Violations

- **Formal Claim**: Over/Under totals and point spreads for the same athletic contest at identical timestamp $t$ are monotone in line thresholds:
  $$L_1 < L_2 \implies p(\text{Total} > L_2, t) \le p(\text{Total} > L_1, t)$$
- **Metric & Formula**:
  - Gross line inversion:
    $$\text{LineViolation}_{\text{gross}}(L_1, L_2, t) = \max\left(0, p(L_2, t) - p(L_1, t)\right)$$
  - Net line inversion:
    $$\text{LineViolation}_{\text{net}}(L_1, L_2, t) = \max\left(0, \text{LineViolation}_{\text{gross}} - \text{Friction}\right)$$
- **Unit of Independence**:
  - `game_key`.
- **Statistical Test**:
  - Clustered bootstrap on net line violations clustered by match.
- **Minimum Relevant Effect Size (MRES)**:
  - Net violation rate $\ge 0.5\%$ of quotes with net executable edge $\ge \$0.010$.
- **Null Definition**:
  - If net executable violations $< 0.5\%$ or net edge $\le 0$, single-game lines are declared **MONOTONE / EFFICIENT**.

---

### H6: Liquidity vs. Calibration Error

- **Formal Claim**: Calibration error is inversely related to contract market liquidity (measured by total volume and order book quote frequency). High-liquidity contracts exhibit smaller calibration errors than low-liquidity contracts.
- **Metric & Formula**:
  - Absolute pricing error per contract: $e_i = |p_i - y_i|$.
  - Predictors: $\log_{10}(\text{Volume USD}_i + 1)$, $\log_{10}(\text{Quote Count}_i + 1)$.
  - Spearman Rank Correlation:
    $$\rho_v = \text{CorrRank}(\log_{10}(\text{Volume}_i), e_i)$$
    $$\rho_q = \text{CorrRank}(\log_{10}(\text{Quote Count}_i), e_i)$$
- **Unit of Independence**:
  - Cluster key (`game_key`, `underlying_key`, `event_key`).
- **Statistical Test**:
  - Cluster bootstrap CI on Spearman rank correlation $\rho_v$ and $\rho_q$.
- **Minimum Relevant Effect Size (MRES)**:
  - Spearman $|\rho| \ge 0.150$ with $p \le 0.05$.
- **Null Definition**:
  - If $|\rho| < 0.150$ or CI spans 0, liquidity is declared **UNINFORMATIVE OF CALIBRATION ERROR** (thin contracts calibrate as well or as poorly as deep contracts).

---

## 3. Multiple Comparison Correction

- **Method**: Holm-Bonferroni step-down procedure.
- **Implementation**: Utilizes `packages.polymarket.ablation.run_ablations` / step-down logic directly.
- **Family**: The full family $\{H1, H2, H3, H4, H5, H6\}$ ($M = 6$).
- **Significance Level**: Family-wise error rate $\alpha = 0.05$.
- **Procedure**:
  1. Obtain raw cluster bootstrap p-values: $p_{(1)} \le p_{(2)} \le \dots \le p_{(6)}$.
  2. Sequential rejection threshold for rank $k \in \{1, \dots, 6\}$:
     $$\alpha_k = \frac{\alpha}{M - k + 1} = \frac{0.05}{7 - k}$$
  3. Step down: If $p_{(k)} \le \alpha_k$, reject $H_{(k)}$; stop at the first failure to reject. All remaining hypotheses receive corrected verdict `NO_DIFFERENCE` / `NULL`.

---

## 4. Clustered Bootstrap & Sampling Plan

- **Bootstrap Iterations**: $B = 2,000$.
- **Random Seed**: Fixed at `seed = 42`.
- **Resampling Procedure**:
  - Resample clusters (units of independence) with replacement.
  - Include all markets, contracts, and quotes belonging to selected clusters.
  - Compute replication metrics $\hat{\theta}^{(b)}$ for $b \in \{1, \dots, B\}$.
  - 95% Confidence Intervals calculated via the empirical percentile method:
    $$\text{CI}_{95\%} = \left[ \hat{\theta}_{(0.025 \cdot B)}, \hat{\theta}_{(0.975 \cdot B)} \right]$$

---

## 5. Independence Key Generation Rules

To prevent artificial degrees of freedom, the mapping from market records to independence keys is governed by deterministic rules:

1. **Sports (`sports_pre_match`, `sports_in_play`)**:
   - `game_key = sport + "_" + date + "_" + teamA + "_vs_" + teamB`
   - Extracted from Gamma market question/slug via regex `(vs\.?|@|-v-)`.
2. **Crypto Thresholds (`crypto_threshold`)**:
   - `underlying_key = asset + "_" + resolution_date + "_" + resolution_hour`
   - Example: `BTC_2026-09-14_20` (all strikes expiring in the same hour share 1 cluster).
3. **Event Markets (`event_market`)**:
   - `event_key = event_id` (or canonical `event_slug` from Polymarket Gamma `/events`).

**Rule Agreement Verification**:
- Prior to analysis, 100 randomly sampled markets will be manually clustered into independent real-world outcomes and compared against the programmatic cluster generator. The agreement rate will be measured and reported.

---

## 6. Stopping Rule

The corpus size is determined strictly by the census of available, verified resolved CLOB markets up to the historical cutoff:
- All qualifying markets captured in Corpus v1 are analyzed in their entirety.
- No data collection will continue after analysis commences.
- No p-value-driven sample expansion is permitted.

---

## 7. Sign-off & Lock

This document represents the immutable pre-registration contract for the Polymarket CLOB Corpus v1 Research Study. Any subsequent departure or adaptation necessitated by empirical data realities must be explicitly disclosed under `DEVIATIONS` in `RESEARCH_REPORT_V1.md`.
