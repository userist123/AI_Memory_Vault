---
id: "atm-m172-art51-evidenta-electronica"
type: legal_atomic_obligation
lifecycle: REVIEW
verification: verified_source
instruction_trust: NONE
category: technical_obligation_analysis
source_act: "[[Ordinul_M172_2021]]"
legal_article: "Articolul 51"
created: 2026-09-06
updated: 2026-09-06
status: requires_legal_review
human_approval_required: true
---

# Notă Derivată Atomică: Ținerea Registrelor de Evidență în Format Electronic Omologat

> [!IMPORTANT]
> **REGIM DE GUVERNANȚĂ & DISCLAIMER JURIDIC**:
> Această notă derivată atomică este o analiză tehnică preliminară a unui text normativ extern (`instruction_trust: NONE`).
> Nu constituie asistență juridică, conformitate prezumată sau politică activă.
> Statut: `lifecycle: REVIEW` / `verification: verified_source` / `status: requires_legal_review`.
> **Nu poate fi promovată în `ACTIVE` fără validare și aprobare umană explicită.**


## 1. Referință Legală Exactă
- **Act Normativ Sursă**: [[Ordinul_M172_2021]]
- **Articol / Alineat**: **Articolul 51**
- **Textul Obligației Legale**:
  > „Registrele de evidență a informațiilor clasificate și a autorizațiilor se pot elabora în formă electronică, folosind o aplicație informatică pentru fiecare tip de evidență la nivelul MApN, omologată de DCiSM, cu obligativitatea tipăririi registrelor la sfârșitul anului calendaristic și înregistrarea în Registrul unic.”

---

## 2. Analiză de Impact Tehnic
Aplicațiile de gestiune a documentelor (ex: `Registru-de-transferuri` C#/WPF) trebuie să funcționeze 100% offline (air-gapped), să utilizeze criptare locală robustă și să dispună de modul nativ de export/tipărire conform machetelor oficiale la data de 31 decembrie.

---

## 3. Control Tehnic Propus
Arhitectură decuplată de rețea, baze de date criptate SQLCipher AES-256, autentificare locală bazată pe hash PBKDF2/Argon2 cu salt, generatoare de rapoarte PDF/A conforme cu machetele din Anexele 9 și 18.

---

## 4. Procedură de Testare / Verificare Tehnică
Test de funcționare în izolare totală de rețea (cu adaptorul de rețea dezactivat) și test de generare a raportului anual identic cu registrul tipizat.

---

## 5. Evidence Artifact (Dovada de Conformitate)
`Dosarul tehnic de omologare DCiSM și rapoartele de testare în laborator izolat.`

---

## 6. Guvernanță și Responsabilitate Operațională
- **Owner Tehnic Propus**: `Desktop_Application_Architect`
- **Necesitate Validare Juridică / DPO / Compliance**: **Obligatorie — Necesită omologare tehnică din partea DCiSM.**
- **Regim de Promovare**: `REVIEW / requires_legal_review`. **Strict interzisă promovarea în `ACTIVE` fără aprobare umană explicită.**

---

## 🔗 Legături Conexe în Graf
- [[Ordinul_M172_2021]]
- [[04 Security Integrity Map]]
- [[07 Knowledge Domains Map]]
