---
id: "atm-l190-art4-cnp-garantii"
type: legal_atomic_obligation
lifecycle: REVIEW
verification: verified_source
instruction_trust: NONE
category: technical_obligation_analysis
source_act: "[[Legea_190_2018]]"
legal_article: "Articolul 4"
created: 2026-09-06
updated: 2026-09-06
status: requires_legal_review
human_approval_required: true
---

# Notă Derivată Atomică: Garanții Tehnice la Prelucrarea Numărului de Identificare Național (CNP)

> [!IMPORTANT]
> **REGIM DE GUVERNANȚĂ & DISCLAIMER JURIDIC**:
> Această notă derivată atomică este o analiză tehnică preliminară a unui text normativ extern (`instruction_trust: NONE`).
> Nu constituie asistență juridică, conformitate prezumată sau politică activă.
> Statut: `lifecycle: REVIEW` / `verification: verified_source` / `status: requires_legal_review`.
> **Nu poate fi promovată în `ACTIVE` fără validare și aprobare umană explicită.**


## 1. Referință Legală Exactă
- **Act Normativ Sursă**: [[Legea_190_2018]]
- **Articol / Alineat**: **Articolul 4**
- **Textul Obligației Legale**:
  > „Prelucrarea numărului de identificare național pe baza interesului legitim al operatorului este permisă numai cu implementarea garanțiilor tehnice: minimizare, măsuri tehnice de securitate, numirea obligatorie a unui DPO, termene de stocare proporționale și instruirea personalului.”

---

## 2. Analiză de Impact Tehnic
Identificatorii naționali (CNP, serie CI) nu pot fi stocați sau afișați în clar în rapoarte sau jurnale. Se impune criptare la nivel de coloană sau tokenizare ireversibilă pentru prelucrări de rutină.

---

## 3. Control Tehnic Propus
Modul de tokenizare/pseudonimizare la nivel de persistență; mascare automată în vizualizările UI (`190******1234`).

---

## 4. Procedură de Testare / Verificare Tehnică
Test automat `test_cnp_storage_is_tokenized()` care inspectează datele brute din baza de date și confirmă absența CNP-ului în clar.

---

## 5. Evidence Artifact (Dovada de Conformitate)
`Raport de audit al schemei bazei de date și atestarea instruirii personalului.`

---

## 6. Guvernanță și Responsabilitate Operațională
- **Owner Tehnic Propus**: `Data_Privacy_Engineer`
- **Necesitate Validare Juridică / DPO / Compliance**: **Obligatorie — Necesită avizare din partea DPO.**
- **Regim de Promovare**: `REVIEW / requires_legal_review`. **Strict interzisă promovarea în `ACTIVE` fără aprobare umană explicită.**

---

## 🔗 Legături Conexe în Graf
- [[Legea_190_2018]]
- [[04 Security Integrity Map]]
- [[07 Knowledge Domains Map]]
