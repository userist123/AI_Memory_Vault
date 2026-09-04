---
id: 49e4644c-1868-53fd-a3c1-0b5b76bc508f
type: knowledge
lifecycle: REVIEW
category: mlops/distribution_shift_monitoring
tags:
- ml-systems
- huyen
- distribution-shift
- ks-test
- psi
- concept-drift
- data-monitoring
- covariate-shift
created: '2026-09-04'
updated: '2026-09-04'
provenance:
  source_type: ai
  source_ref: 06_INBOX/RAW_IMPORTS/BOOKS/Huyen-Designing-ML-Systems-Ch8
confidence: high
verification: unverified
relations:
- relation: references
  target: 01_KNOWLEDGE/BOOKS/HARDENING_MLOps_Feedback_Loops_and_Concept_Drift.md
- relation: references
  target: 01_KNOWLEDGE/BOOKS/PRODUCTION_MLOps_AB_Testing_and_Statistical_Significance.md
- relation: references
  target: 01_KNOWLEDGE/BOOKS/Caiet_Teme_Aplicatii_Practice_Carti.md
---

# MLOps Scaling: Detectarea Deplasărilor de Distribuție și Monitorizare

**Sursă**: Chip Huyen, *Designing Machine Learning Systems* (Capitolul 8: Data Distribution Shifts)
**Domeniu**: Covariate Shift, Label Shift, Concept Drift, Teste Statistice de Monitorizare

---

## 1. Taxonomia Deplasărilor

### 1.1 Tipuri de Shift

Fie $P_{\text{train}}(X, Y)$ distribuția de antrenament și $P_{\text{prod}}(X, Y)$ distribuția de producție.

| Tip | Ce se schimbă | Ce rămâne | Exemplu |
| :--- | :--- | :--- | :--- |
| **Covariate Shift** | $P(X)$ | $P(Y \mid X)$ | Fotografii de zi → noapte |
| **Label Shift** | $P(Y)$ | $P(X \mid Y)$ | Proporția de spam crește |
| **Concept Drift** | $P(Y \mid X)$ | $P(X)$ | Aceeași acțiune, alt preț just |
| **Feature Drift** | $P(X_i)$ | Celelalte features | Un senzor de-calibrat |

### 1.2 Gradual vs Sudden vs Recurring

```
Gradual:    ──────╱────────   (sezonier lent)
Sudden:     ──────┐────────   (eveniment: COVID, crash)
Recurring:  ──╱──╲──╱──╲──   (zilnic: trafic web zi/noapte)
Incremental: ──╱╱╱╱╱╱─────   (degradare graduală continuă)
```

---

## 2. Teste Statistice pentru Detectare

### 2.1 Kolmogorov-Smirnov Test (KS)

Compară **CDFurile empirice** ale două distribuții:

$$D_{KS} = \sup_x |F_{\text{ref}}(x) - F_{\text{prod}}(x)|$$

| Proprietate | Detaliu |
| :--- | :--- |
| **Tip** | Non-parametric, nu presupune normalitate |
| **Sensibilitate** | Bună pe distribuții continue |
| **Limitare** | Univariat (per feature individual) |
| **Prag** | $p < 0.05$ → shift detectat |

### 2.2 Population Stability Index (PSI)

$$\text{PSI} = \sum_{i=1}^{B} (p_i^{\text{actual}} - p_i^{\text{expected}}) \cdot \ln\left(\frac{p_i^{\text{actual}}}{p_i^{\text{expected}}}\right)$$

Unde $B$ = numărul de bin-uri (de obicei 10-20).

| PSI | Interpretare |
| :--- | :--- |
| < 0.10 | ✅ Fără shift semnificativ |
| 0.10 – 0.25 | ⚠️ Shift moderat, investigare |
| > 0.25 | 🚨 Shift major, model posibil invalid |

### 2.3 Comparație de Teste

| Test | Tip | Dimensiuni | Sensibilitate | Interpretare |
| :--- | :--- | :--- | :--- | :--- |
| **KS** | Non-parametric | Univariat | Medie | p-value |
| **PSI** | Binned | Univariat | Bună | Scor cu praguri fixe |
| **Chi-squared** | Parametric | Univariat | Bună pe categorice | p-value |
| **MMD** | Kernel-based | Multivariat | Foarte bună | p-value/scor |
| **Wasserstein** | Optimal transport | Univariat | Bună pe shift-uri mici | Distanța |

---

## 3. Monitorizare în Producție

### 3.1 Dashboard de Monitorizare

```
┌─────────────────────────────────────────────┐
│  Model Health Dashboard                      │
├──────────────┬──────────────┬───────────────┤
│  Feature PSI │  Prediction  │  Performance  │
│  per coloană │  Distribution│  Metrics      │
│              │              │               │
│  age: 0.03 ✅│  P(Y=1):     │  AUC: 0.91    │
│  income: 0.18⚠│ train: 0.12 │  F1:  0.87    │
│  zip: 0.42 🚨│  prod:  0.19 │  Latency: 45ms│
└──────────────┴──────────────┴───────────────┘
```

### 3.2 Strategia de Alertare Multi-Nivel

| Nivel | Condiție | Acțiune |
| :--- | :--- | :--- |
| **Info** | PSI 0.05-0.10 pe o feature | Log, monitorizare continuă |
| **Warning** | PSI 0.10-0.25 sau KS p<0.05 pe 3+ features | Alertă echipă, investigare |
| **Critical** | PSI > 0.25 pe feature critică sau performance drop >5% | Rollback automat la model anterior |

### 3.3 Ferestre de Referință

| Strategie | Referință | Avantaj |
| :--- | :--- | :--- |
| **Training set** | Datele de antrenament originale | Detectează orice drift de la baseline |
| **Sliding window** | Ultimele N zile | Detectează shift-uri recente |
| **Seasonal** | Aceeași perioadă anul trecut | Evită alarme false sezoniere |

---

## 4. Reantrenare Adaptivă

### 4.1 Când Să Reantrenezi

| Trigger | Descriere |
| :--- | :--- |
| **Periodic** | La fiecare K zile/săptămâni (simplu, previzibil) |
| **Performance-based** | Când metricile scad sub prag |
| **Shift-based** | Când PSI/KS detectează shift semnificativ |
| **Hybrid** | Periodic + trigger pe alertă critică |

### 4.2 Online Learning vs Reantrenare Completă

| Aspect | Online Learning | Reantrenare |
| :--- | :--- | :--- |
| **Latență** | Secunde | Ore-zile |
| **Date necesare** | Ultimul batch | Tot setul |
| **Risc** | Catastrophic forgetting | Pierdere date recente |
| **Implementare** | Complexă | Standard |

---

## 5. Aplicabilitate în Memory Vault

- **Embedding Drift**: Monitorizarea distribuției vectorilor de embedding pe notele noi vs cele vechi
- **Query Distribution**: PSI pe tipurile de interogări (search patterns) pentru detectarea schimbărilor de utilizare
- **Confidence Calibration**: Verificarea că scorurile de confidence rămân bine calibrate în timp

---

## Referințe Obsidian

- [[HARDENING_MLOps_Feedback_Loops_and_Concept_Drift]]
- [[PRODUCTION_MLOps_AB_Testing_and_Statistical_Significance]]
- [[CAPSTONE_MLOps_Shadow_Deployments_and_Bandit_Routing]]
- [[Caiet_Teme_Aplicatii_Practice_Carti]]
