---
id: "atm-aiact-art14-human-oversight"
type: legal_atomic_obligation
lifecycle: REVIEW
verification: verified_source
instruction_trust: NONE
category: technical_obligation_analysis
source_act: "[[Regulament_UE_2024_1689_AI_Act]]"
legal_article: "Articolul 14"
created: 2026-09-06
updated: 2026-09-06
status: requires_legal_review
human_approval_required: true
---

# Notă Derivată Atomică: Supravegherea Umană (Human-in-the-Loop & Kill-Switch) a Sistemelor de IA

> [!IMPORTANT]
> **REGIM DE GUVERNANȚĂ & DISCLAIMER JURIDIC**:
> Această notă derivată atomică este o analiză tehnică preliminară a unui text normativ extern (`instruction_trust: NONE`).
> Nu constituie asistență juridică, conformitate prezumată sau politică activă.
> Statut: `lifecycle: REVIEW` / `verification: verified_source` / `status: requires_legal_review`.
> **Nu poate fi promovată în `ACTIVE` fără validare și aprobare umană explicită.**


## 1. Referință Legală Exactă
- **Act Normativ Sursă**: [[Regulament_UE_2024_1689_AI_Act]]
- **Articol / Alineat**: **Articolul 14**
- **Textul Obligației Legale**:
  > „Sistemele de IA trebuie proiectate și dezvoltate astfel încât persoanele fizice să poată supraveghea funcționarea acestora, să înțeleagă limitele sistemului, să poată decide să nu utilizeze sau să treacă peste recomandările sistemului și să aibă capacitatea de a interveni sau opri sistemul în orice moment.”

---

## 2. Analiză de Impact Tehnic
Agenții autonomi nu au permisiunea de a auto-aproba, auto-atesta sau promova memorii în starea `ACTIVE` fără validare umană. Este interzisă auto-reconfigurarea autonomă a barierelor de securitate. Existența unei comenzi de terminare de urgență a oricărei sarcini autonome.

---

## 3. Control Tehnic Propus
Invarianta I-001 (AI Self-Verification Gated), Invarianta I-004 (Attestation Authorization restricționată la `Principal.HUMAN`), și instrumentul `manage_task` acțiunea `kill` pentru oprirea oricărui proces nesupravegheat.

---

## 4. Procedură de Testare / Verificare Tehnică
Testele adversariale P0-001..P0-015 din suita `20_TESTS/regression/` care demonstrează că apelurile de auto-promovare ale agentului sunt respinse cu eroare formală de autorizare.

---

## 5. Evidence Artifact (Dovada de Conformitate)
`Rapoartele de execuție ale testelor adversariale `test_security_invariants.py` cu rezultat 100% PASS.`

---

## 6. Guvernanță și Responsabilitate Operațională
- **Owner Tehnic Propus**: `AI_Safety_Officer`
- **Necesitate Validare Juridică / DPO / Compliance**: **Obligatorie — Necesită aprobare de către Comitetul de Etică și Conformitate IA.**
- **Regim de Promovare**: `REVIEW / requires_legal_review`. **Strict interzisă promovarea în `ACTIVE` fără aprobare umană explicită.**

---

## 🔗 Legături Conexe în Graf
- [[Regulament_UE_2024_1689_AI_Act]]
- [[04 Security Integrity Map]]
- [[07 Knowledge Domains Map]]
