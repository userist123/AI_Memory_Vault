---
id: "ed784073-5394-5c11-ab3b-64039db99692"
type: knowledge
lifecycle: REVIEW
category: mathematical-statistics
tags: ["openstax", "mathematical-statistics", "curriculum", "verified-source"]
created: "2026-09-19"
updated: "2026-09-19"
provenance:
  source_type: ai
  source_ref: "statistics-confidence-intervals-v1-8_2_a_single_population_mean_using_the_student_t_distribution"
  source_date: "2026-09-19"
  original_path: "06_INBOX/RAW_IMPORTS/openstax_statistics_2e_ch08/8_2_a_single_population_mean_using_the_student_t_distribution"
  extraction_date: "2026-09-19"
  redaction: none
  provenance_status: complete
confidence: high
verification: unverified
relations: []
---

# 8 2 A Single Population Mean Using The Student T Distribution (Introductory Statistics 2e)

## 1. Sursă & Proveniență
- **Manual**: *Introductory Statistics 2e*, OpenStax, Rice University.
- **Ediție**: 2nd Edition.
- **Autori**: Barbara Illowsky, Susan Dean.
- **ISBN**: ISBN-13: 978-1-951693-55-8.
- **Tip sursă**: manual.
- **Capitol / Temă**: Chapter 8: Confidence Intervals.
- **Secțiune**: 8 2 A Single Population Mean Using The Student T Distribution.
- **Licență**: Creative Commons Attribution 4.0 International (CC BY 4.0).
- **URL Licență**: https://creativecommons.org/licenses/by/4.0/.
- **Manifest**: `07_EVALUATION/curriculum/statistics_provenance_manifest.json`.

---

## 2. Concepte Extrase și Validate Verbatim

### 1. Student's t-distribution
The Student's t-distribution is a probability distribution used for statistical inference when the population standard deviation is unknown. It is characterized by its degrees of freedom, and its shape is symmetric about zero, with thicker tails and a shorter center than the standard normal distribution. As the degrees of freedom increase, the t-distribution approaches the standard normal distribution.

> "The graph for the Student's t-distribution is similar to the standard normal curve. The mean for the Student's t-distribution is zero and the distribution is symmetric about zero. The Student's t-distribution has more probability in its tails than the standard normal distribution because the spread of the t-distribution is greater than the spread of the standard normal."

### 2. Degrees of Freedom
Degrees of freedom (df) represent the number of values in a calculation that are free to vary. For a single population mean using the t-distribution, the degrees of freedom are calculated as n - 1, where n is the sample size. This value is essential for determining the specific shape of the t-distribution used for confidence intervals.

> "We call the number n – 1 the degrees of freedom (df)."

### 3. Error Bound for a Population Mean (EBM)
The Error Bound for a Population Mean (EBM) is the margin of error used to construct a confidence interval when the population standard deviation is unknown. It is calculated by multiplying the t-score (corresponding to the desired confidence level) by the standard error of the mean. The resulting interval provides an estimate of the true population mean.

> "If the population standard deviation is not known , the error bound for a population mean is: E B M = ( t α 2 ) ( s n )"

### 4. T-score
A t-score is a standardized value that measures how far a sample mean is from the population mean in terms of the estimated standard error. It serves the same interpretive purpose as a z-score but is used specifically when the population standard deviation is unknown and must be estimated using the sample standard deviation.

> "The t -score has the same interpretation as the z -score . It measures how far x ¯ x ¯ is from its mean μ ."
