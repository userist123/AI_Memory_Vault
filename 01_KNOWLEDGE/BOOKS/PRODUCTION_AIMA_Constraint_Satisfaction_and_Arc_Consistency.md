---
id: 15f71e84-2458-5a7e-a4da-72a8740e2b06
type: knowledge
lifecycle: REVIEW
category: ai/constraint_satisfaction_problems
tags:
- aima
- russell-norvig
- csp
- arc-consistency
- backtracking
- constraint-propagation
- ac-3
- sudoku
created: '2026-09-04'
updated: '2026-09-04'
provenance:
  source_type: ai
  source_ref: 06_INBOX/RAW_IMPORTS/BOOKS/Russell-Norvig-AIMA4e-Ch6
confidence: high
verification: unverified
relations:
- relation: references
  target: 01_KNOWLEDGE/BOOKS/EXPERT_AIMA_Game_Theory_and_Adversarial_Search.md
- relation: references
  target: 01_KNOWLEDGE/BOOKS/SPECIALIZED_AIMA_Automated_Planning_and_HTN.md
- relation: references
  target: 01_KNOWLEDGE/BOOKS/Caiet_Teme_Aplicatii_Practice_Carti.md
---

# AIMA Production: Satisfacerea Constrângerilor (CSP) și Consistența de Arc

**Sursă**: Stuart Russell & Peter Norvig, *Artificial Intelligence: A Modern Approach* 4th ed. (Capitolul 6: Probleme de Satisfacere a Constrângerilor)
**Domeniu**: Căutare cu Constrângeri, Reducerea Domeniilor, Backtracking Inteligent

---

## 1. Formalizarea CSP

Un CSP este definit de tripletul $(X, D, C)$:
- $X = \{X_1, X_2, \ldots, X_n\}$ — mulțimea variabilelor
- $D = \{D_1, D_2, \ldots, D_n\}$ — domeniile fiecărei variabile
- $C$ — mulțimea constrângerilor (relații între variabile)

### 1.1 Tipuri de Constrângeri

| Tip | Aritate | Exemplu |
| :--- | :--- | :--- |
| **Unare** | 1 variabilă | $X_1 \neq \text{roșu}$ |
| **Binare** | 2 variabile | $X_1 \neq X_2$ (colorare grafuri) |
| **Globale** | $n$ variabile | `AllDifferent(X_1, \ldots, X_n)` (Sudoku) |
| **Soft** | orice | Preferințe cu cost (CSP de optimizare) |

### 1.2 Graful de Constrângeri

Fiecare variabilă este un nod; fiecare constrângere binară este o muchie. Structura grafului determină complexitatea:
- **Graf arbore** → rezolvabil în $O(n \cdot d^2)$ cu propagare direcțională
- **Graf general** → NP-complet, dar tehnicile de propagare reduc dramatic spațiul

---

## 2. Algoritmul AC-3 (Arc Consistency)

### 2.1 Definiție

Un arc $(X_i, X_j)$ este **arc-consistent** dacă pentru fiecare valoare $a \in D_i$ există cel puțin o valoare $b \in D_j$ care satisface constrângerea $C_{ij}$.

### 2.2 Pseudocod

```
function AC-3(csp):
    queue ← toate arcele {(Xi, Xj) | Cij ∈ C}
    while queue ≠ ∅:
        (Xi, Xj) ← DEQUEUE(queue)
        if REVISE(csp, Xi, Xj):
            if |Di| == 0: return false  // inconsistență detectată
            for each Xk in NEIGHBORS(Xi) - {Xj}:
                ENQUEUE(queue, (Xk, Xi))
    return true

function REVISE(csp, Xi, Xj):
    revised ← false
    for each x in Di:
        if no y in Dj satisfies Cij(x, y):
            remove x from Di
            revised ← true
    return revised
```

### 2.3 Complexitate

- **Timp**: $O(e \cdot d^3)$ unde $e$ = numărul de arce, $d$ = dimensiunea maximă a domeniului
- **Spațiu**: $O(e + n \cdot d)$
- **Notă**: AC-3 nu garantează soluția, ci **reduce domeniile**; trebuie combinat cu backtracking

---

## 3. Backtracking cu Propagare

### 3.1 Strategia Completă

```
function BACKTRACK(assignment, csp):
    if assignment is complete: return assignment
    var ← SELECT-UNASSIGNED-VARIABLE(csp)     // MRV
    for each value in ORDER-DOMAIN-VALUES(var, csp):  // LCV
        if value is consistent with assignment:
            assignment[var] ← value
            inferences ← INFERENCE(csp, var, value)  // MAC / FC
            if inferences ≠ failure:
                result ← BACKTRACK(assignment, csp)
                if result ≠ failure: return result
            remove inferences; unassign var
    return failure
```

### 3.2 Euristici Critice

| Euristică | Acronim | Strategie |
| :--- | :--- | :--- |
| **Minimum Remaining Values** | MRV | Alege variabila cu cel mai mic domeniu (fail-first) |
| **Degree Heuristic** | DEG | La egalitate MRV, alege variabila cu cele mai multe constrângeri |
| **Least Constraining Value** | LCV | Alege valoarea care elimină cele mai puține opțiuni pentru vecini |
| **Maintaining Arc Consistency** | MAC | Rulează AC-3 după fiecare atribuire |
| **Forward Checking** | FC | Verifică doar arcele directe ale variabilei atribuite |

### 3.3 Conflict-Directed Backjumping

Backtracking-ul naiv revine la variabila anterioară. **Backjumping** identifică sursa conflictului și sare direct la variabila responsabilă:

```
Dacă X₇ eșuează din cauza constrângerii cu X₃,
backjump direct la X₃ (nu la X₆).
```

Menține un **conflict set** per variabilă care înregistrează care atribuiri anterioare au cauzat eliminarea valorilor.

---

## 4. CSP în Probleme Reale

| Problemă | Variabile | Constrângeri | Scală |
| :--- | :--- | :--- | :--- |
| **Sudoku 9×9** | 81 celule | AllDiff pe rânduri/coloane/blocuri | AC-3 rezolvă ~95% fără backtracking |
| **Colorare Hartă** | Regiuni | Vecini ≠ aceeași culoare | NP-complet general |
| **Scheduling** | Time-slots | Fără suprapunere + preferințe | Soft CSP cu optimizare |
| **Circuit Layout** | Componente | Non-overlap + distanță minimă | Mii de variabile |

---

## 5. CSP și Planificarea Agenților AI

### 5.1 Task Assignment ca CSP

Un agent care trebuie să aloce sarcini sub-agenților poate formula asta ca CSP:
- **Variabile**: Sarcinile de executat
- **Domenii**: Agenții disponibili
- **Constrângeri**: Capacitate maximă, competențe necesare, deadline-uri

### 5.2 Prompt Validation ca CSP

Validarea unui prompt complex poate fi formulată ca CSP:
- **Variabile**: Secțiunile promptului (system, context, instruction)
- **Constrângeri**: Token budget, no-overlap de instrucțiuni, XML demarcation obligatorie

---

## Referințe Obsidian

- [[EXPERT_AIMA_Game_Theory_and_Adversarial_Search]]
- [[SPECIALIZED_AIMA_Automated_Planning_and_HTN]]
- [[MASTERY_AIMA_HMM_Kalman_and_Particle_Filtering]]
- [[Caiet_Teme_Aplicatii_Practice_Carti]]
