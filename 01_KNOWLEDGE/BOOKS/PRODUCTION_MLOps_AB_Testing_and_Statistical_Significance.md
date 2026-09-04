---
id: c7c7a886-b495-5c12-be9a-8a8c14998d60
type: knowledge
lifecycle: REVIEW
category: mlops/ab_testing_statistical_significance
tags:
- ml-systems
- huyen
- ab-testing
- statistical-significance
- sequential-testing
- bonferroni
- experiment-design
- hypothesis-testing
created: '2026-09-04'
updated: '2026-09-04'
provenance:
  source_type: ai
  source_ref: 06_INBOX/RAW_IMPORTS/BOOKS/Huyen-Designing-ML-Systems-Ch9-Ch10
confidence: high
verification: unverified
relations:
- relation: references
  target: 01_KNOWLEDGE/BOOKS/EXPERT_MLOps_RealTime_Streaming_Features_and_Windows.md
- relation: references
  target: 01_KNOWLEDGE/BOOKS/CAPSTONE_MLOps_Shadow_Deployments_and_Bandit_Routing.md
- relation: references
  target: 01_KNOWLEDGE/BOOKS/Caiet_Teme_Aplicatii_Practice_Carti.md
---

# MLOps Production: Testare A/B și Semnificanță Statistică

**Sursă**: Chip Huyen, *Designing Machine Learning Systems* (Capitolele 9-10: Deployment & Experimentation)
**Domeniu**: Evaluare de Modele în Producție, Design Experimental, Testare Secvențială

---

## 1. Testarea A/B pentru Modele ML

### 1.1 De Ce Este Diferită de Testarea Software Clasică

| Aspect | Software Clasic | Modele ML |
| :--- | :--- | :--- |
| **Metrică** | Deterministă (latență, erori) | Stochastică (CTR, revenue) |
| **Variabilitate** | Mică | Mare (depinde de distribuția utilizatorilor) |
| **Durată** | Ore-zile | Zile-săptămâni |
| **Risc** | Bug vizibil | Degradare subtilă, greu detectabilă |

### 1.2 Setup-ul Experimental

```
Grup Control (A): Model vechi (baseline)
Grup Treatment (B): Model nou (challenger)

Randomizare: Hash(user_id) % 100
  → [0, 49]  = Grup A
  → [50, 99] = Grup B
```

**Cerințe**:
- **SUTVA** (Stable Unit Treatment Value Assumption): Tratamentul unui utilizator NU afectează rezultatul altui utilizator
- **Sample Size**: Calculat a priori pe baza MDE (Minimum Detectable Effect)
- **Duration**: Minim 1-2 cicluri de business complet (ex: 2 săptămâni include weekend)

---

## 2. Testarea Ipotezelor

### 2.1 Formularea

$$H_0: \mu_B - \mu_A = 0 \quad \text{(nicio diferență)}$$
$$H_1: \mu_B - \mu_A \neq 0 \quad \text{(diferență semnificativă)}$$

### 2.2 Erorile Fundamentale

| Tip | Probabilitate | Consecință |
| :--- | :--- | :--- |
| **Tip I (α)** — Fals Pozitiv | De obicei α = 0.05 | Adoptăm model inferior |
| **Tip II (β)** — Fals Negativ | De obicei β = 0.20 | Respingem model superior |
| **Putere (1-β)** | 0.80 (standard) | Șansa de a detecta un efect real |

### 2.3 Calculul Sample Size

$$n = \frac{(Z_{\alpha/2} + Z_\beta)^2 \cdot 2\sigma^2}{\delta^2}$$

Unde:
- $Z_{\alpha/2} \approx 1.96$ pentru α = 0.05
- $Z_\beta \approx 0.84$ pentru putere 80%
- $\sigma^2$ = varianța metricii
- $\delta$ = MDE (Minimum Detectable Effect)

---

## 3. Problema Peeking-ului și Testarea Secvențială

### 3.1 Peeking Problem

**Anti-pattern critic**: Verificarea p-value-ului zilnic și oprirea experimentului când p < 0.05.

```
Zi 1: p = 0.23 → continuă
Zi 2: p = 0.11 → continuă
Zi 3: p = 0.04 → OPRESC! "Semnificativ!"  ← FALS
```

Fiecare verificare crește rata de Tip I efectivă. Cu 10 verificări, α efectiv ≈ 0.19 (nu 0.05!).

### 3.2 Soluția: Sequential Testing (SPRT)

```
Sequential Probability Ratio Test:
  La fiecare observație nouă, calculează:
    Λ_n = P(date | H_1) / P(date | H_0)
  
  Decizia:
    Λ_n ≥ B = ln(1/α) → Acceptă H_1 (semnificativ)
    Λ_n ≤ A = ln(β)   → Acceptă H_0 (nu e semnificativ)
    A < Λ_n < B        → Continuă colectarea
```

**Avantaj**: Permite oprirea timpurie legitimă cu control riguros al erorilor.

### 3.3 Always-Valid P-Values

Alternativă modernă: *e-values* și *anytime-valid confidence sequences* care permit verificarea la orice moment fără inflație de α.

---

## 4. Testare Multiplă și Corecția Bonferroni

### 4.1 Problema

Dacă testezi 20 de metrici simultan la α = 0.05, probabilitatea de cel puțin un fals pozitiv:

$$P(\text{≥1 fals pozitiv}) = 1 - (1 - 0.05)^{20} \approx 0.64$$

### 4.2 Corecția Bonferroni

$$\alpha_{\text{corectat}} = \frac{\alpha}{k}$$

Pentru 20 metrici: $\alpha_{\text{corectat}} = 0.05/20 = 0.0025$

**Dezavantaj**: Foarte conservatoare. Alternative mai puțin conservatoare:
- **Holm-Bonferroni**: Step-down procedure
- **Benjamini-Hochberg**: Controlează FDR (False Discovery Rate) în loc de FWER

### 4.3 Tabel Comparativ

| Metodă | Controlează | Conservatism | Putere |
| :--- | :--- | :--- | :--- |
| **Bonferroni** | FWER | Foarte conservator | Scăzută |
| **Holm-Bonferroni** | FWER | Moderat | Moderată |
| **Benjamini-Hochberg** | FDR | Puțin conservator | Ridicată |
| **Fără corecție** | Nimic | — | Maximă (dar nesigură) |

---

## 5. Metrici de Evaluare în Producție

### 5.1 Guardrail Metrics vs Primary Metrics

| Tip | Exemple | Rol |
| :--- | :--- | :--- |
| **Primary** | CTR, Revenue per user | Metrică de decizie |
| **Guardrail** | Latency p99, Error rate, Crash rate | NU trebuie să se degradeze |

**Regulă**: Experimentul este un succes doar dacă primary metric se îmbunătățește AND guardrails rămân stabile.

### 5.2 Metrici Proxy vs Metrici de Business

```
User click → Page view → Add to cart → Purchase → Return rate
  (proxy)     (proxy)     (proxy)      (business)  (long-term)
```

Alegerea metricii trebuie să balanseze:
- **Sensibilitate**: Metricile proxy detectează diferențe mai repede
- **Relevanță**: Metricile de business reflectă impactul real
- **Timp**: Metricile long-term necesită experimente lungi

---

## 6. Aplicabilitate în Memory Vault

- **Shadow Deployment Testing** (din Tier 6): Compară modelul nou cu baseline-ul pe query-uri reale
- **Retrieval Quality A/B**: Testează noi strategii de embedding/reranking pe search-uri vault
- **Guardrail**: Latența search-ului (p99 < 200ms) ca guardrail metric

---

## Referințe Obsidian

- [[EXPERT_MLOps_RealTime_Streaming_Features_and_Windows]]
- [[CAPSTONE_MLOps_Shadow_Deployments_and_Bandit_Routing]]
- [[HARDENING_MLOps_Feedback_Loops_and_Concept_Drift]]
- [[Caiet_Teme_Aplicatii_Practice_Carti]]
