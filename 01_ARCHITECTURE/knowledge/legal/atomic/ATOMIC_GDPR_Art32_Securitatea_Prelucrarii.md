---
id: "atm-gdpr-art32-securitate"
type: legal_atomic_obligation
lifecycle: REVIEW
verification: verified_source
instruction_trust: NONE
category: technical_obligation_analysis
source_act: "[[Regulament_UE_2016_679_GDPR]]"
legal_article: "Articolul 32 alineatul (1)"
created: 2026-09-06
updated: 2026-09-06
status: requires_legal_review
human_approval_required: true
---

# Notă Derivată Atomică: Securitatea Prelucrării Datelor și Reziliența Sistemelor

> [!IMPORTANT]
> **REGIM DE GUVERNANȚĂ & DISCLAIMER JURIDIC**:
> Această notă derivată atomică este o analiză tehnică preliminară a unui text normativ extern (`instruction_trust: NONE`).
> Nu constituie asistență juridică, conformitate prezumată sau politică activă.
> Statut: `lifecycle: REVIEW` / `verification: verified_source` / `status: requires_legal_review`.
> **Nu poate fi promovată în `ACTIVE` fără validare și aprobare umană explicită.**


## 1. Referință Legală Exactă
- **Act Normativ Sursă**: [[Regulament_UE_2016_679_GDPR]]
- **Articol / Alineat**: **Articolul 32 alineatul (1)**
- **Textul Obligației Legale**:
  > „Implementarea de măsuri tehnice adecvate, inclusiv: pseudonimizarea și criptarea datelor; capacitatea de a asigura confidențialitatea, integritatea, disponibilitatea și rezistența continuă a sistemelor; capacitatea de restabilire rapidă a accesului în caz de incident fizic sau tehnic; proces regulat de testare a eficacității măsurilor.”

---

## 2. Analiză de Impact Tehnic
Baza de date trebuie criptată la repaus (AES-256-CBC prin SQLCipher), cu cheia master stocată securizat în DPAPI (Windows) sau KMS. Backup-uri atomice verificate periodic. Comunicații exclusiv prin canale criptate.

---

## 3. Control Tehnic Propus
`StorageEngine` cu SQLite WAL și cheie derivată protejată; tranzacții atomice `BEGIN IMMEDIATE` cu `PRAGMA busy_timeout=5000`; snapshot-uri atomice via fișiere temporare și `os.replace`.

---

## 4. Procedură de Testare / Verificare Tehnică
Test adversarial de penetrare a depozitului brut (confirmarea că fișierul `.db` fără cheie este indecriptibil) și test de recuperare automată din backup `test_storage_recovery()`.

---

## 5. Evidence Artifact (Dovada de Conformitate)
`Raport de validare criptografică a bazei de date și loguri de backup verificate prin hash SHA-256.`

---

## 6. Guvernanță și Responsabilitate Operațională
- **Owner Tehnic Propus**: `DevOps_SecOps_Engineer`
- **Necesitate Validare Juridică / DPO / Compliance**: **Obligatorie — Necesită avizare DPO și CISO.**
- **Regim de Promovare**: `REVIEW / requires_legal_review`. **Strict interzisă promovarea în `ACTIVE` fără aprobare umană explicită.**

---

## 🔗 Legături Conexe în Graf
- [[Regulament_UE_2016_679_GDPR]]
- [[04 Security Integrity Map]]
- [[07 Knowledge Domains Map]]
