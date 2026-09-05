---
id: "atm-gdpr-art25-by-design"
type: legal_atomic_obligation
lifecycle: REVIEW
verification: verified_source
instruction_trust: NONE
category: technical_obligation_analysis
source_act: "[[Regulament_UE_2016_679_GDPR]]"
legal_article: "Articolul 25 alineatele (1) și (2)"
created: 2026-09-06
updated: 2026-09-06
status: requires_legal_review
human_approval_required: true
---

# Notă Derivată Atomică: Protecția Datelor Începând cu Momentul Conceperii și în Mod Implicit (Data Protection by Design & Default)

> [!IMPORTANT]
> **REGIM DE GUVERNANȚĂ & DISCLAIMER JURIDIC**:
> Această notă derivată atomică este o analiză tehnică preliminară a unui text normativ extern (`instruction_trust: NONE`).
> Nu constituie asistență juridică, conformitate prezumată sau politică activă.
> Statut: `lifecycle: REVIEW` / `verification: verified_source` / `status: requires_legal_review`.
> **Nu poate fi promovată în `ACTIVE` fără validare și aprobare umană explicită.**


## 1. Referință Legală Exactă
- **Act Normativ Sursă**: [[Regulament_UE_2016_679_GDPR]]
- **Articol / Alineat**: **Articolul 25 alineatele (1) și (2)**
- **Textul Obligației Legale**:
  > „Operatorul implementează măsuri tehnice și organizatorice adecvate (precum pseudonimizarea) concepute să aplice eficient principiile de protecție a datelor și să asigure că, în mod implicit, sunt prelucrate numai datele strict necesare fiecărui scop specific.”

---

## 2. Analiză de Impact Tehnic
Toate schemele de date, API-urile și interfețele de intrare trebuie să refuze câmpuri PII inutile. Se impune pseudonimizarea automată a identificatorilor la nivel de ingestie și limitarea implicită a expunerii datelor către agenți și servicii externe.

---

## 3. Control Tehnic Propus
Filtru automat de sanitizare la nivel de controller (`privacy_mask.py`), scheme stricte de validare Pydantic ce blochează câmpurile nedeclarate și politici automate de expirare a stocării (TTL).

---

## 4. Procedură de Testare / Verificare Tehnică
Test automat de regresie `test_privacy_mask_filters_pii()` care injectează payload-uri cu date personale nesolicitate și verifică redactarea sau respingerea lor cu cod de eroare determinist.

---

## 5. Evidence Artifact (Dovada de Conformitate)
`Jurnal de audit SHA-256 al procesului de mascare a datelor și raport de testare automată `07_EVALUATION/ci_evidence/privacy_mask_report.json`.`

---

## 6. Guvernanță și Responsabilitate Operațională
- **Owner Tehnic Propus**: `Lead_Software_Architect`
- **Necesitate Validare Juridică / DPO / Compliance**: **Obligatorie — Necesită avizare din partea Data Protection Officer (DPO) și aprobare umană prealabilă.**
- **Regim de Promovare**: `REVIEW / requires_legal_review`. **Strict interzisă promovarea în `ACTIVE` fără aprobare umană explicită.**

---

## 🔗 Legături Conexe în Graf
- [[Regulament_UE_2016_679_GDPR]]
- [[04 Security Integrity Map]]
- [[07 Knowledge Domains Map]]
