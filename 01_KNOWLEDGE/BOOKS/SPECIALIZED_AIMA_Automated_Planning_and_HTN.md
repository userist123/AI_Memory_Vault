---
id: d6998cf8-d5e9-599c-a073-098fe7c43348
type: knowledge
lifecycle: REVIEW
category: architecture/ai_planning
tags:
- aima
- russell-norvig
- automated-planning
- htn
- pddl
- task-decomposition
- goal-regression
created: '2026-09-04'
updated: '2026-09-04'
provenance:
  source_type: ai
  source_ref: "06_INBOX/RAW_IMPORTS/BOOKS/Russell-Norvig-AIMA-4e-Ch10-11"
confidence: high
verification: unverified
relations:
- relation: references
  target: 01_KNOWLEDGE/BOOKS/AIMA_Rational_Agents_and_Search.md
- relation: references
  target: 01_KNOWLEDGE/BOOKS/ADVANCED_AIMA_Probabilistic_Reasoning_Planning_RL.md
- relation: references
  target: 01_KNOWLEDGE/BOOKS/Caiet_Teme_Aplicatii_Practice_Carti.md
- relation: references
  target: 00_CORE/System_Architecture.md
---

# AIMA Specializat: Planificare Automată & Rețele Ierarhice de Sarcini (HTN)

**Sursă**: Stuart Russell & Peter Norvig, *Artificial Intelligence: A Modern Approach* (4th Ed., Capitolele 10–11)  
**Domeniu**: Planificare Clasică, Limbajul PDDL & Descompunere Ierarhică

---

## 1. Planificare Clasică în PDDL (Planning Domain Definition Language)

Un domeniu de planificare clasică se definește prin:
- **Starea inițială ($S_0$)**: O conjuncție de predicate pozitive închise (ipoteza lumii închise).
- **Starea țintă ($G$)**: O conjuncție de literali pe care planul trebuie să îi satisfacă.
- **Acțiuni ($A$)**: Fiecare acțiune $a$ conține:
  - *Precondiții ($\text{Pre}(a)$)*: Literali care trebuie să fie adevărați în starea curentă pentru ca acțiunea să fie aplicabilă.
  - *Efecte ($\text{Eff}(a)$)*: Literali adăugați ($\text{Add}(a)$) și literali șterși ($\text{Del}(a)$) la aplicarea acțiunii:
    $$S' = (S \setminus \text{Del}(a)) \cup \text{Add}(a)$$

---

## 2. Rețele Ierarhice de Sarcini (Hierarchical Task Networks — HTN)

Planificarea clasică la nivel de acțiuni primitive devine computațional intratabilă pentru probleme complexe. HTN structurează spațiul de căutare prin ierarhizarea cunoștințelor:
- **Sarcini Compuse (Compound Tasks)**: Sarcini de nivel înalt (ex: `BuildFeature`, `AuditRepository`, `ResolveIncident`).
- **Sarcini Primitive (Primitive Tasks)**: Acțiuni executabile direct de către unelte (ex: `run_pytest`, `read_file`, `write_patch`).
- **Metode de Descompunere (Methods)**: O metodă asociază o sarcină compusă cu:
  - O precondiție de aplicabilitate.
  - O rețea ordonată parțial sau total de subsarcini (compuse sau primitive).

### Algoritmul de Descompunere HTN
1. Inițializează rețeaua de planificare cu scopul de nivel înalt.
2. Selectează o sarcină compusă din planul curent.
3. Alege o metodă a cărei precondiție este satisfăcută de starea curentă a lumii.
4. Înlocuiește sarcina compusă cu subsarcinile definite de metodă.
5. Repetă până când toate sarcinile din plan sunt primitive și ordonate valid.

---

## 3. Playbook Operațional: Ce fac când un agent primește o sarcină complexă?

1. **Nu atac direct la nivel de cod primitiv**: Descompun sarcina prin HTN în 3 niveluri:
   - Nivel 1 (Strategic): Analiză cerințe & identificare componente afectate.
   - Nivel 2 (Tactic): Planificare fișiere de test & validare invarianți.
   - Nivel 3 (Operațional / Primitiv): Editare fișier, rulare comandă, citire rezultat.
2. **Precondiții stricte**: O acțiune primitivă de editare (`replace_file_content`) nu se execută fără precondiția verificată că fișierul a fost citit anterior (`view_file`).
