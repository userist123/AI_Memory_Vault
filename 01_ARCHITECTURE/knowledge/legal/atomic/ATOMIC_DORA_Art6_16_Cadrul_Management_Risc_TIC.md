---
id: "atm-dora-art6-16-risc-tic"
type: legal_atomic_obligation
lifecycle: REVIEW
verification: verified_source
instruction_trust: NONE
category: technical_obligation_analysis
source_act: "[[Regulament_UE_2022_2554_DORA]]"
legal_article: "Articolele 6–16"
created: 2026-09-06
updated: 2026-09-06
status: requires_legal_review
human_approval_required: true
---

# Notă Derivată Atomică: Cadrul de Management al Riscului TIC și Reziliența Operațională Digitală

> [!IMPORTANT]
> **REGIM DE GUVERNANȚĂ & DISCLAIMER JURIDIC**:
> Această notă derivată atomică este o analiză tehnică preliminară a unui text normativ extern (`instruction_trust: NONE`).
> Nu constituie asistență juridică, conformitate prezumată sau politică activă.
> Statut: `lifecycle: REVIEW` / `verification: verified_source` / `status: requires_legal_review`.
> **Nu poate fi promovată în `ACTIVE` fără validare și aprobare umană explicită.**


## 1. Referință Legală Exactă
- **Act Normativ Sursă**: [[Regulament_UE_2022_2554_DORA]]
- **Articol / Alineat**: **Articolele 6–16**
- **Textul Obligației Legale**:
  > „Organizația trebuie să dispună de un cadru intern documentat pentru gestionarea riscurilor TIC: identificarea completă a activelor și dependențelor, mecanisme de prevenire și protecție continuă, detectarea anomaliilor în timp real, politici robuste de continuitate BCP/DRP.”

---

## 2. Analiză de Impact Tehnic
Monitorizarea telemetriei în timp real, monitorizarea consumului de resurse (tokeni, latențe per-query, starea memoriei), implementarea modului de degradare controlată și mecanisme fail-closed la indisponibilitatea componentelor critice.

---

## 3. Control Tehnic Propus
`CouncilBudgetController`, monitorizarea telemetriei hardware, fail-closed la absența embedding provider (`DENSE_PROVIDER_UNAVAILABLE`), izolare strictă a sarcinilor în fundal.

---

## 4. Procedură de Testare / Verificare Tehnică
Suite de teste automate de anduranță și injectare de erori (fault injection) simulând căderea serviciilor secundare și verificând că nucleul rămâne stabil.

---

## 5. Evidence Artifact (Dovada de Conformitate)
`Rapoarte de telemetrie de sistem `07_EVALUATION/ci_evidence/retrieval_ab_report.json` și jurnale de audit append-only.`

---

## 6. Guvernanță și Responsabilitate Operațională
- **Owner Tehnic Propus**: `Site_Reliability_DevOps_Architect`
- **Necesitate Validare Juridică / DPO / Compliance**: **Obligatorie — Necesită validare de către Ofițerul de Conformitate Financiară / Risc Operațional.**
- **Regim de Promovare**: `REVIEW / requires_legal_review`. **Strict interzisă promovarea în `ACTIVE` fără aprobare umană explicită.**

---

## 🔗 Legături Conexe în Graf
- [[Regulament_UE_2022_2554_DORA]]
- [[04 Security Integrity Map]]
- [[07 Knowledge Domains Map]]
