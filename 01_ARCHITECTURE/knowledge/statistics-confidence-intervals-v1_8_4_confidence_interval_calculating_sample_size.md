---
id: "229a36c7-2275-52e9-a6a1-3a613db845c0"
type: knowledge
lifecycle: REVIEW
category: mathematical-statistics
tags: ["openstax", "mathematical-statistics", "curriculum", "verified-source"]
created: "2026-09-19"
updated: "2026-09-19"
provenance:
  source_type: ai
  source_ref: "statistics-confidence-intervals-v1-8_4_confidence_interval_calculating_sample_size"
  source_date: "2026-09-19"
  original_path: "06_INBOX/RAW_IMPORTS/openstax_statistics_2e_ch08/8_4_confidence_interval_calculating_sample_size"
  extraction_date: "2026-09-19"
  redaction: none
  provenance_status: complete
confidence: high
verification: unverified
relations: []
---

# 8 4 Confidence Interval Calculating Sample Size (Introductory Statistics 2e)

## 1. Sursă & Proveniență
- **Manual**: *Introductory Statistics 2e*, OpenStax, Rice University.
- **Ediție**: 2nd Edition.
- **Autori**: Barbara Illowsky, Susan Dean.
- **ISBN**: ISBN-13: 978-1-951693-55-8.
- **Tip sursă**: manual.
- **Capitol / Temă**: Chapter 8: Confidence Intervals.
- **Secțiune**: 8 4 Confidence Interval Calculating Sample Size.
- **Licență**: Creative Commons Attribution 4.0 International (CC BY 4.0).
- **URL Licență**: https://creativecommons.org/licenses/by/4.0/.
- **Manifest**: `07_EVALUATION/curriculum/statistics_provenance_manifest.json`.

---

## 2. Concepte Extrase și Validate Verbatim

### 1. Confidence Interval for a Single Population Mean (Known Standard Deviation)
When the population standard deviation is known and the distribution is normal, the confidence interval is calculated using the sample mean and the error bound for the mean (EBM). The EBM is derived from the z-score and the standard error of the mean.

> "The general form for a confidence interval for a single population mean, known standard deviation, normal distribution is given by (lower bound, upper bound) = (point estimate – EBM , point estimate + EBM )"

### 2. Student's t-Distribution
The Student's t-distribution is used for confidence intervals when the population standard deviation is unknown. It utilizes the sample standard deviation and degrees of freedom, defined as n minus 1.

> "df = n - 1; the degrees of freedom for a Student’s t-distribution where n represents the size of the sample"

### 3. Confidence Interval for a Population Proportion
The sample proportion serves as a point estimate for the true population proportion. The confidence interval is constructed using the error bound for a proportion (EBP), which incorporates the z-score and the sample proportion.

> "Confidence interval for a proportion: (lower bound, upper bound) = ( p ′ – E B P , p ′ + E B P )"

### 4. Sample Size Calculation for Population Proportion
The required sample size for estimating a population proportion is calculated using the z-score, the sample proportion, and the desired margin of error. This formula allows researchers to determine the number of participants needed for a specific confidence level.

> "n = z α 2 2 p ′ q ′ E B P 2 provides the number of participants needed to estimate the population proportion with confidence 1 - α and margin of error EBP ."
