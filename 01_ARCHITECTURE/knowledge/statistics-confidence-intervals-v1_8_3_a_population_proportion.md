---
id: "7834b61e-a595-5960-852f-6c4ba439e743"
type: knowledge
lifecycle: REVIEW
category: mathematical-statistics
tags: ["openstax", "mathematical-statistics", "curriculum", "verified-source"]
created: "2026-09-19"
updated: "2026-09-19"
provenance:
  source_type: ai
  source_ref: "statistics-confidence-intervals-v1-8_3_a_population_proportion"
  source_date: "2026-09-19"
  original_path: "06_INBOX/RAW_IMPORTS/openstax_statistics_2e_ch08/8_3_a_population_proportion"
  extraction_date: "2026-09-19"
  redaction: none
  provenance_status: complete
confidence: high
verification: unverified
relations: []
---

# 8 3 A Population Proportion (Introductory Statistics 2e)

## 1. Sursă & Proveniență
- **Manual**: *Introductory Statistics 2e*, OpenStax, Rice University.
- **Ediție**: 2nd Edition.
- **Autori**: Barbara Illowsky, Susan Dean.
- **ISBN**: ISBN-13: 978-1-951693-55-8.
- **Tip sursă**: manual.
- **Capitol / Temă**: Chapter 8: Confidence Intervals.
- **Secțiune**: 8 3 A Population Proportion.
- **Licență**: Creative Commons Attribution 4.0 International (CC BY 4.0).
- **URL Licență**: https://creativecommons.org/licenses/by/4.0/.
- **Manifest**: `07_EVALUATION/curriculum/statistics_provenance_manifest.json`.

---

## 2. Concepte Extrase și Validate Verbatim

### 1. Population Proportion
A population proportion represents the true percentage or fraction of a population that possesses a specific characteristic. It is estimated using a sample proportion, denoted as p' or p-hat, calculated by dividing the number of successes by the total sample size.

> "The random variable p ′ p ′ (read "P prime") is that proportion, P ′ = X n P ′ = X n (Sometimes the random variable is denoted as P ^ P ^ , read "P hat".)"

### 2. Confidence Interval for a Proportion
A confidence interval provides a range of values that is likely to contain the true population proportion with a specified level of confidence. It is constructed using the point estimate (sample proportion) and the error bound for the proportion (EBP).

> "The confidence interval has the form ( p ′ p ′ – EBP , p ′ p ′ + EBP ). EBP is error bound for the proportion."

### 3. Error Bound for a Proportion (EBP)
The error bound for a proportion (EBP) defines the margin of error for the estimate. It is calculated using the critical z-score corresponding to the confidence level and the standard deviation of the sample proportion.

> "The error bound for a proportion is E B P = ( z α 2 ) ( p ′ q ′ n ) E B P = ( z α 2 ) ( p ′ q ′ n ) where q ′ q ′ = 1 – p ′ p ′"

### 4. Plus-Four Confidence Interval
The plus-four method is an adjustment used to improve the accuracy of confidence intervals for proportions when the sample size is small. It involves adding two successes and two failures to the observed data, resulting in a new sample size of n + 4.

> "We simply pretend that we have four additional observations. Two of these observations are successes and two are failures. The new sample size, then, is n + 4, and the new count of successes is x + 2."

### 5. Sample Size Calculation
Researchers can determine the required sample size for a study by rearranging the error bound formula. When the estimated proportion is unknown, using 0.5 for both p' and q' ensures the largest possible sample size, providing a conservative estimate.

> "n = ( z α 2 ) 2 ( p ′ q ′ ) E B P 2 n = ( z α 2 ) 2 ( p ′ q ′ ) E B P 2"
