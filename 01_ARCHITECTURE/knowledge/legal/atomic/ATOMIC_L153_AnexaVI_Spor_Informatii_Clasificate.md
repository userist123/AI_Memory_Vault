---
id: "atm-l153-anexavi-spor-clasificate"
type: legal_atomic_obligation
lifecycle: REVIEW
verification: verified_source
instruction_trust: NONE
category: technical_obligation_analysis
source_act: "[[Legea_Cadru_153_2017]]"
legal_article: "Anexa VIII, Tabelul 108Lex, Nota 2 și Anexa VI"
created: 2026-09-06
updated: 2026-09-06
status: requires_legal_review
human_approval_required: true
---

# Notă Derivată Atomică: Drepturi Salariale și Compensarea Gestionării Informațiilor Clasificate

> [!IMPORTANT]
> **REGIM DE GUVERNANȚĂ & DISCLAIMER JURIDIC**:
> Această notă derivată atomică este o analiză tehnică preliminară a unui text normativ extern (`instruction_trust: NONE`).
> Nu constituie asistență juridică, conformitate prezumată sau politică activă.
> Statut: `lifecycle: REVIEW` / `verification: verified_source` / `status: requires_legal_review`.
> **Nu poate fi promovată în `ACTIVE` fără validare și aprobare umană explicită.**


## 1. Referință Legală Exactă
- **Act Normativ Sursă**: [[Legea_Cadru_153_2017]]
- **Articol / Alineat**: **Anexa VIII, Tabelul 108Lex, Nota 2 și Anexa VI**
- **Textul Obligației Legale**:
  > „Personalul care gestionează date și informații clasificate beneficiază de un spor salarial specific (până la 25% la ORNISS, până la 15% în administrație, sporuri de risc și pericol deosebit în sectorul de apărare) în funcție de certificatul de securitate deținut și timpul efectiv lucrat.”

---

## 2. Analiză de Impact Tehnic
Sistemele de gestiune a resurselor umane și pontaj trebuie să asocieze automat drepturile salariale de valabilitatea certificatului de securitate, alertând cu privire la expirarea avizului pentru a preveni plăți necuvenite sau prelucrări neautorizate.

---

## 3. Control Tehnic Propus
Algoritm de verificare a stării certificatului de securitate; generare de notificări automate cu 60 și 30 de zile înainte de expirarea valabilității autorizației.

---

## 4. Procedură de Testare / Verificare Tehnică
Test unitar de calcul salarial și test de expirare certificat `test_security_clearance_expiry_triggers_alert()`.

---

## 5. Evidence Artifact (Dovada de Conformitate)
`Jurnalul de verificare a certificatelor de securitate și deciziile interne de acordare a sporurilor.`

---

## 6. Guvernanță și Responsabilitate Operațională
- **Owner Tehnic Propus**: `HR_Financial_Systems_Engineer`
- **Necesitate Validare Juridică / DPO / Compliance**: **Obligatorie — Necesită validare de către Direcția Juridică și Direcția Financiar-Contabilă.**
- **Regim de Promovare**: `REVIEW / requires_legal_review`. **Strict interzisă promovarea în `ACTIVE` fără aprobare umană explicită.**

---

## 🔗 Legături Conexe în Graf
- [[Legea_Cadru_153_2017]]
- [[04 Security Integrity Map]]
- [[07 Knowledge Domains Map]]
