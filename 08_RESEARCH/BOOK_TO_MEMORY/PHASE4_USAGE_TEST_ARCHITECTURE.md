# Arhitectura Formală a Usage Test / Task-Based Validation (Faza 4)

**Document Version**: 1.0.0  
**Phase**: Phase 4 — Usage Test / Task-Based Validation  
**Branch**: `research/book-to-memory-phase4-usage-test`  
**Governing Documents**:
- `00_GOVERNANCE/rules/POLICY-LEARNING-QUALITY-02.md` (Secțiunea 6: Test de utilizare & Secțiunea 7: Evaluare cu/fără notă)
- `00_GOVERNANCE/protocols/Confidence_Model.md`
- `03_IMPLEMENTATION/packages/lifecycle/validation/book_to_memory_schema.py`
- `03_IMPLEMENTATION/packages/lifecycle/validation/book_to_memory_lifecycle.py`
- `03_IMPLEMENTATION/packages/lifecycle/validation/book_to_memory_conflict.py`
- `03_IMPLEMENTATION/packages/lifecycle/validation/book_to_memory_usage_test.py`

---

## 1. Principiul Fundamental și Obiectivul Arhitectural

În arhitectura AI Memory Vault, cunoștințele distilate din tratate științifice și cărți nu sunt admise pe baza simplei formulări elegante sau a autorității aparente a textului original.

Conform `POLICY-LEARNING-QUALITY-02`:
> *"Agentul rezolvă un task realist folosind doar nota și dependențele declarate, fără acces la carte.*  
> *Nu se consideră utilă o notă doar pentru că pare utilă. Trebuie demonstrat că poate contribui la rezolvarea unei sarcini reale."*

Faza 4 implementează un cadru determinist și auditabil de **Task-Based Validation** care măsoară capacitatea empirică a unei note de a asista un agent în rezolvarea problemelor reale, în condiții stricte de izolare a sursei.

---

## 2. Granița de Izolare a Sursei (Source Isolation Barrier)

Pentru a garanta că succesul la test se datorează calității sintezei atomice din notă și nu accesului direct la volumul original sau la internet, sistemul impune o barieră impenetrabilă de izolare:

```text
┌────────────────────────────────────────────────────────┐
│                   TASK-BASED VALIDATOR                 │
│                                                        │
│  [TaskSpecification (Imutabil)]                        │
│          +                                             │
│  [Nota Candidată]                                      │
│          +                   ───► [Evaluare Izolată]   │
│  [Dependențe Declarate]                                │
│                                                        │
└────────────────────────────────────────────────────────┘
          ▲
          │ STRICT BLOCAT (SourceIsolationError)
          │
  ┌───────┴──────────────────────────────────────────────┐
  │ ❌ Cartea brută / scanare PDF                        │
  │ ❌ 06_INBOX/Carti/*                                  │
  │ ❌ Căutare Web / Google / ArXiv / Wikipedia          │
  │ ❌ Răspunsuri ascunse din prompt / context exterior  │
  └──────────────────────────────────────────────────────┘
```

Orice încercare detectată în răspuns sau în contextul declarat care referă fișiere PDF originale, directoare inbox sau surse web declanșează automat:
- `SourceIsolationError`;
- Notarea încercării cu scor `0/10` și statut `FAIL`;
- Consemnarea încălcării de securitate în istoricul de audit.

---

## 3. Structura de Date și Imutabilitatea Task-ului

Pentru a preveni fenomenul de "task tailoring" (ajustarea cerințelor după ce modelul a răspuns pentru a forța un scor mare), definirea sarcinii este complet decuplată de evaluare:

### 3.1 TaskSpecification (Frozen Dataclass)
- `task_id`: Identificator unic stabil;
- `title` & `description`: Descrierea riguroasă a problemei (minimum 10 caractere);
- `task_type`: Tipologia de problemă (`application`, `diagnosis`, `alternative_selection`, `transfer`, `error_identification`, `rule_application`);
- `expected_criteria`: Criteriile tehnice de evaluare stabilite *a priori*;
- **Protecție Anti-Memorare**: Sarcinile de memorare mecanică sau copiere ad-literam (`reproduce word-for-word`, `memorize literal text`) sunt respinse la instanțiere cu `UsageTestValidationError`.

### 3.2 UsageTestRecord și Semnătura Tamper-Evident
Fiecare încercare generează un record semnat criptografic cu SHA-256:
```python
canonical_payload = {
    "test_id": record.test_id,
    "note_id": record.note_id,
    "task_id": record.task_id,
    "model_or_agent": record.model_or_agent,
    "attempt": record.attempt,
    "score": record.score,
    "max_score": record.max_score,
    "rubric_scores": record.rubric_scores,
    "status": record.status,
    "timestamp": record.timestamp,
    "evaluator_id": record.evaluator_id,
}
signature = hashlib.sha256(json.dumps(canonical_payload, sort_keys=True).encode()).hexdigest()
```
Orice tentativă de alterare a scorului sau statutului invalidează verificarea semnăturii (`verify_signature() == False`).

---

## 4. Grila de Notare Canonică (Rubric Policy-02)

Notarea se bazează pe 5 dimensiuni standardizate, fiecare evaluată strict între `0` și `2` puncte (Scor maxim: 10 puncte):

| Dimensiune | Punctaj | Condiție de Conformitate |
| :--- | :---: | :--- |
| **Corectitudine** | 0 – 2 | Răspunsul este tehnic valid; **obligatoriu >= 1 pentru PASS**. |
| **Completitudine** | 0 – 2 | Acoperă toate cerințele esențiale din specificația sarcinii. |
| **Fără ghicit** | 0 – 2 | Fiecare concluzie derivă din notă, fără speculații nefundamentate. |
| **Fără surse externe** | 0 – 2 | Zero referințe la materiale exterioare notei și contextului permis. |
| **Reproductibilitate** | 0 – 2 | Procedura poate fi replicată determinist în condiții identice. |

### Matricea Decizională a Scorului:
- **`>= 8/10` și Corectitudine `>= 1`**: `PASS` (Nota devine eligibilă pentru promovarea la `VERIFIED`);
- **`8/10` dar Corectitudine `= 0`**: `FAIL` (Imposibil de promovat un răspuns incorect din punct de vedere faptic);
- **`5 – 7/10`**: `RETRY` (Nota necesită rafinare, clarificare conceptuală și re-testare);
- **`< 5/10`**: `FAIL` (Respingere sau rescriere structurală completă).

---

## 5. Separarea Epistemică (Ipoteză vs. Mecanism de Producție)

Usage Test respectă ierarhia epistemică strictă definită în schemă:
$$\text{BIOLOGICAL FACT} \longrightarrow \text{ENGINEERING HYPOTHESIS} \longrightarrow \text{EXPERIMENT} \longrightarrow \text{VAULT MECHANISM}$$

- Dacă nota testată are tipul `epistemic_type = HYPOTHESIS`, un scor de `10/10` la Usage Test demonstrează **utilitatea ipotezei ca model de lucru**, dar **NU o transformă automat într-un mecanism de producție**.
- Nota devine `VERIFIED HYPOTHESIS`, păstrând cerința formală de experimentare înainte de integrarea în nucleul cognitiv.

---

## 6. Independența și Prioritatea Conflictelor

1. Un scor perfect la Usage Test (`10/10`) **NU rezolvă și nu anulează un conflict**;
2. Dacă o notă este marcată cu un conflict deschis (`status == "open"`) de severitate `high` sau `critical`, nota **rămâne strict blocată** de la promovarea în `ACTIVE`, indiferent de rezultatul testului de utilizare (`check_lifecycle_eligibility` returnează `False`).

---

## 7. Validare Multi-Agent fără Contaminare

Pentru note importante:
1. Sarcina este executată de **minimum doi agenți/modele distincți** (`agent_alpha`, `agent_beta`);
2. Evaluarea se face în condiții de **izolare temporală și informațională**: Agentul B nu are acces la răspunsul, dovezile sau scorul Agentului A;
3. Sistemul detectează contaminarea evaluatorului (`ContaminatedEvaluatorError`) dacă răspunsul agentului secundar referă rezultatele agentului anterior;
4. **Cerință de promovare**: Ambii agenți trebuie să obțină `PASS` (`score >= 8/10`). Dacă unul dintre agenți eșuează, nota este trimisă la rafinare (`RETRY`).

---

## 8. Granițe de Autoritate și Protecție împotriva Autopromovării

- `Principal.AI_AGENT` nu are autoritatea de a arbitra, acorda punctaje sau semna înregistrările de Usage Test (`EvaluationTamperError`).
- **Nicio Autopromovare**: Reușita la Usage Test conferă exclusiv statutul de eligibilitate. Tranziția către `ACTIVE` necesită în mod obligatoriu parcurgerea porților `GATE-06`, `GATE-07` și emiterea unui `OwnerApprovalToken` valid semnat HMAC de către `Principal.HUMAN`.
