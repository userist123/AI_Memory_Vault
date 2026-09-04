---
id: bc82e557-c5c4-57e9-aa98-697a5415db31
type: knowledge
lifecycle: REVIEW
category: architecture/mlops
tags:
- mlops
- huyen
- feature-store
- data-leakage
- point-in-time-join
- continual-learning
- shadow-deployment
- canary-analysis
created: '2026-09-04'
updated: '2026-09-04'
provenance:
  source_type: ai
  source_ref: "06_INBOX/RAW_IMPORTS/BOOKS/Chip-Huyen-Designing-ML-Systems-Ch3-9"
confidence: high
verification: unverified
relations:
- relation: references
  target: 01_KNOWLEDGE/BOOKS/Production_ML_Systems_and_Continual_Learning.md
- relation: references
  target: 01_KNOWLEDGE/BOOKS/Caiet_Teme_Aplicatii_Practice_Carti.md
- relation: references
  target: 00_CORE/System_Architecture.md
---

# MLOps Avansat: Feature Stores, Prevenirea Scurgerii de Date & Învățare Continuă

**Sursă**: Chip Huyen, *Designing Machine Learning Systems* (Capitolele 3–9)  
**Domeniu**: Arhitectură MLOps, Integritatea Datelor & Desfășurare Continuă

---

## 1. Feature Stores & Jointura la Punct Fix în Timp (Point-in-Time Join)

Una dintre cele mai periculoase erori din sistemele ML este **Data Leakage** (scurgerea de date din viitor în setul de antrenament):
- **Cauză tipică**: Calcularea agregărilor de caracteristici (ex: numărul de interogări ale unui utilizator în ultimele 7 zile) folosind starea curentă a bazei de date în loc de starea existentă exact la momentul producerii evenimentului țintă ($t_{\text{event}}$).
- **Point-in-Time Correctness**: Asigură că pentru fiecare etichetă generată la $t_{\text{event}}$, toate caracteristicile asociate sunt calculate strict pe intervalul $(-\infty, t_{\text{event}}]$.
- **Rolul Feature Store-ului (ex: Feast, Hopsworks)**:
  - *Depozit Offline (Parquet, BigQuery)*: Oferă interogări pe bază de intervale temporale pentru antrenare fără scurgere de date.
  - *Depozit Online (Redis, Cassandra)*: Oferă citiri de latență ultra-joasă (sub 10ms) pentru inferență în timp real.

---

## 2. Strategii de Desfășurare în Producție & Validare Fără Risc

Huyen clasifică tranziția de la laborator la producție în 4 niveluri de siguranță:
1. **Desfășurare din Umbră (Shadow Deployment)**: Noul model primește traficul real în paralel cu modelul de bază, dar predicțiile sale sunt doar înregistrate în jurnal, fără a afecta utilizatorul. Permite măsurarea latenței reale și a stabilității memoriei la sarcină mare.
2. **Canary Release**: Noul model preia inițial un procent infim de trafic (ex: 2%), crescut incremental la 10%, 50%, 100% doar dacă ratele de eroare și scorurile de calitate rămân în parametri prestabiliți.
3. **Testare A/B cu Plafon de Semnificație Statistică**: Măsurarea directă a impactului asupra metricilor de afaceri/utilizator prin rutarea utilizatorilor unici către variante distincte.
4. **Algoritmi Bandits (Multi-Armed Bandits / Thompson Sampling)**: Alocare dinamică și continuă a traficului către modelul cel mai performant în timp real, minimizând costul explorării (*regret minimization*).

---

## 3. Învățare Continuă: Re-antrenare Fără Stare vs. Ajustare cu Stare

- **Re-antrenare Fără Stare (Stateless Retraining)**: Modelul este antrenat de la zero pe o fereastră glisantă de date recente (ex: ultimele 90 de zile). Garantează că modelul nu acumulează artefacte corupte, dar este costisitor computațional.
- **Ajustare Fină Continuă (Stateful Fine-Tuning / Online Learning)**: Modelul existent își actualizează ponderile incremental pe loturi mici de date proaspăt colectate. Necesită un mecanism strict de regularizare (ex: Weight Decay sau mixare cu date vechi) pentru a preveni uitarea catastrofică (*Catastrophic Forgetting*).

---

## 4. Playbook Operațional: Ce fac când antrenez sau actualizez un model?

1. **Verific schema temporală**: Mă asigur că niciun câmp creat la un timestamp ulterior etichetei nu intră în matricea de caracteristici.
2. **Implementez jurnalizarea asincronă a predicțiilor**: Salvez perechile `(input_id, prediction, ground_truth, latency)` cu hash SHA-256 pentru trasabilitate completă.
3. **Setez praguri automate de alertare pe drift**: Dacă testul Kolmogorov-Smirnov arată $p < 0.05$ pe distribuția scorurilor, declanșez re-validarea setului de testare.
