---
id: "atm-m172-art193-199-medii-stocare"
type: legal_atomic_obligation
lifecycle: REVIEW
verification: verified_source
instruction_trust: NONE
category: technical_obligation_analysis
source_act: "[[Ordinul_M172_2021]]"
legal_article: "Articolele 193–199, Anexa nr. 9 și Anexa nr. 18"
created: 2026-09-06
updated: 2026-09-06
status: requires_legal_review
human_approval_required: true
---

# Notă Derivată Atomică: Evidența Mediilor Fizice de Stocare și Marcarea cu Seria Hardware Unică

> [!IMPORTANT]
> **REGIM DE GUVERNANȚĂ & DISCLAIMER JURIDIC**:
> Această notă derivată atomică este o analiză tehnică preliminară a unui text normativ extern (`instruction_trust: NONE`).
> Nu constituie asistență juridică, conformitate prezumată sau politică activă.
> Statut: `lifecycle: REVIEW` / `verification: verified_source` / `status: requires_legal_review`.
> **Nu poate fi promovată în `ACTIVE` fără validare și aprobare umană explicită.**


## 1. Referință Legală Exactă
- **Act Normativ Sursă**: [[Ordinul_M172_2021]]
- **Articol / Alineat**: **Articolele 193–199, Anexa nr. 9 și Anexa nr. 18**
- **Textul Obligației Legale**:
  > „Fiecare mediu de stocare utilizat este luat în evidență în Registrul din Anexa 9 cu numărul de serie fizic de fabricație, tipul și capacitatea. Pentru mediile cu date CONFIDENȚIAL și superior se întocmește Fișa mediului de stocare (Anexa 18) detaliind fiecare fișier, calea, mărimea în KB și clasificarea. Mediile se marchează fizic conform fracției legale.”

---

## 2. Analiză de Impact Tehnic
Aplicația trebuie să interogheze direct dispozitivele USB/stocare prin API-uri de sistem de nivel scăzut (WMI/Win32 Storage) pentru a prelua Hardware Serial Number, VID, PID și capacitatea brută. Câmpurile fizice sunt Read-Only în UI.

---

## 3. Control Tehnic Propus
Invarianta P16 (Hardware Telemetry Immutability: blocarea oricărei editări manuale a seriei hardware în UI/API), Invarianta P17 (Friendly Name Isolation), Invarianta P18 (Forensics Chain of Custody: fiecare transfer leagă automat seria fizică în logul SHA-256).

---

## 4. Procedură de Testare / Verificare Tehnică
Test unitar de forensic hardware `test_hardware_telemetry_immutability()` ce validează că încercarea de alterare a seriei fizice este respinsă de nucleu.

---

## 5. Evidence Artifact (Dovada de Conformitate)
`Fișa mediului de stocare exportată cu semnătura electronică a gestionarului și amprenta hardware imutabilă.`

---

## 6. Guvernanță și Responsabilitate Operațională
- **Owner Tehnic Propus**: `Hardware_Telemetry_Forensics_Lead`
- **Necesitate Validare Juridică / DPO / Compliance**: **Obligatorie — Necesită validare de către Șeful Structurii de Securitate și Gestionarul CSNR/CDC.**
- **Regim de Promovare**: `REVIEW / requires_legal_review`. **Strict interzisă promovarea în `ACTIVE` fără aprobare umană explicită.**

---

## 🔗 Legături Conexe în Graf
- [[Ordinul_M172_2021]]
- [[04 Security Integrity Map]]
- [[07 Knowledge Domains Map]]
