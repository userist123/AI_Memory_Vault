# Arhitectura Formală a Conflict Registry-ului pentru Book-to-Memory

**Document Version**: 1.0.0  
**Phase**: Phase 3 — Conflict Management  
**Branch**: `research/book-to-memory-phase3-conflicts`  
**Governing Policies**:
- `00_GOVERNANCE/rules/POLICY-LEARNING-QUALITY-02.md` (Secțiunea 5: Contradicții)
- `00_GOVERNANCE/protocols/Confidence_Model.md` (Forensic Evidence & Provenance)
- `03_IMPLEMENTATION/packages/lifecycle/validation/book_to_memory_schema.py`
- `03_IMPLEMENTATION/packages/lifecycle/validation/book_to_memory_lifecycle.py`
- `03_IMPLEMENTATION/packages/lifecycle/validation/book_to_memory_conflict.py`

---

## 1. Context și Misiune Arhitecturală

În procesul de distilare a cunoștințelor din cărți și tratate științifice către memoria AI Vault, contradicțiile între surse (autor vs. autor, ediție veche vs. ediție nouă, paradigmă biologică vs. implementare inginerească) sunt inevitabile.

Arhitectura clasică a sistemelor RAG / LLM tinde să rezolve contradicțiile prin:
1. *Subsumption*: ultima sursă citită suprascrie poziția anterioară;
2. *Hallucinated Consensus*: modelul produce o sinteză hibridă falsă ("pe de o parte... pe de altă parte...") fără ancorare riguroasă în surse;
3. *Arbitrary Deletion*: eliminarea uneia dintre note pentru a păstra coerența aparentă a grafului.

Toate aceste trei moduri sunt **interzise categoric** în AI Memory Vault prin `POLICY-LEARNING-QUALITY-02` (Secțiunea 5):
> *"Când două surse se contrazic, ambele poziții sunt păstrate.*  
> *Conflictul open nu se elimină prin rescrierea uneia dintre note.*  
> *O notă cu conflict open de severitate high nu poate deveni active."*

Phase 3 materializează această cerință printr-un **Conflict Registry** deterministic, cu păstrare duală imutabilă, porți stricte de proveniență și granițe ferme între actorii AI și Uman.

---

## 2. Identitate Deterministă și Invarianță la Ordine

### 2.1 Formatul Identificatorului
Fiecare conflict este înregistrat sub o cheie unică canonică:
```text
CONFLICT-<domain>-<slug>
```
Unde:
- `<domain>` este domeniul științific sau tehnic (ex. `neuroscience`, `distributed_systems`, `algorithms`), validat strict regex `^[a-zA-Z0-9_\-]+$` pentru prevenirea atacurilor de path traversal (`../`, `/`, `\`);
- `<slug>` este format din primele cuvinte ale primei afirmații ordonate lexicografic, concatenate cu un hash SHA-256 de 16 caractere hexazecimale calculat peste perechea normalizată de afirmații.

### 2.2 Algoritmul Invarianței la Ordine (Order Invariance)
Dacă Agentul A detectează contradicția `(Claim1 vs Claim2)` iar Agentul B detectează `(Claim2 vs Claim1)`, ambele apeluri trebuie să producă **același identificator** și să nu creeze duplicate în registru:
```python
sorted_claims = sorted([claim_a.strip(), claim_b.strip()])
digest = hashlib.sha256(f"{domain}:{sorted_claims[0]}::{sorted_claims[1]}".encode('utf-8')).hexdigest()[:16]
conflict_id = f"CONFLICT-{domain}-{slug}-{digest}"
```

---

## 3. Schema Structurii de Date și Păstrarea Pozițiilor Duale

Înregistrarea `ConflictRecord` reține simetric și complet ambele poziții divergente:

```json
{
  "conflict_id": "CONFLICT-neuroscience-synaptic-consolidation-a1f9e8d2c3b40125",
  "domain": "neuroscience",
  "claim_a": "Consolidarea sinaptică este finalizată complet în câteva ore fără reorganizare corticală.",
  "source_a": {
    "source_title": "Cellular Basis of Memory",
    "chapter": "Chapter 2: Synaptic Plasticity",
    "page_range": "45-60"
  },
  "evidence_a": "LTP decays in slices after 4 hours without transcription factor activation.",
  "claim_b": "Consolidarea sinaptică necesită reorganizare de sisteme pe durate de săptămâni/luni.",
  "source_b": {
    "source_title": "Systems Memory and Hippocampus",
    "chapter": "Chapter 5: Neocortical Dialogue",
    "page_range": "112-135"
  },
  "evidence_b": "Retrograde amnesia gradients demonstrate prolonged dependency on hippocampal traces.",
  "severity": "high",
  "status": "open",
  "possible_cause": "Niveluri diferite de analiză (celular vs. sisteme neuroanatomice globale).",
  "resolving_experiment": "Experiment comportamental cu blocanți sinaptici locali vs. decorticare temporală.",
  "agent_interim_behavior": "Tratează ambele modele ca fiind condiționate de nivelul de abstractizare; nu presupune o durată universală de consolidare.",
  "resolution": null,
  "history": []
}
```

### Invariantul Non-Mutației Poziționale
- `claim_a` și `claim_b` nu pot fi modificate retroactiv pentru a ascunde contradicția;
- `evidence_a` și `evidence_b` sunt păstrate permanent ca dovezi textuale pasive;
- Dacă o nouă dovadă aduce o perspectivă diferită, se atașează prin jurnalul de audit (`history`), fără alterarea pozițiilor inițiale.

---

## 4. Strict Provenance Gate și Cerința `exact_page`

Ambele părți ale contradicției sunt supuse acelorași reguli riguroase de proveniență definite în `book_to_memory_schema.py`:
1. `source_title`, `chapter` și `page_range` sunt obligatorii pentru ambele ramuri (`source_a` și `source_b`);
2. Sintagmele leneșe (`"source unknown"`, `"probably"`, `"around chapter"`, `"from the book"`, `"somewhere in"`) sunt respinse cu `ProvenanceGateError`;
3. **Cerința `exact_page`**:
   - Dacă oricare dintre afirmații sau dovezi conține formule matematice, notații asimptotice (`O(...)`), constante numerice critice sau ecuații (verificate prin `_FORMULA_PATTERNS`), câmpul `exact_page` devine strict obligatoriu pentru acea ramură.
   - Absența `exact_page` la formule duce la respingerea automată a înregistrării conflictului.

---

## 5. Taxonomia Severității și Matricea de Blocaj Lifecycle

### 5.1 Severități Permise
- `low`: Divergență terminologică minoră sau diferență de nuanță stilistică care nu afectează acuratețea raționamentelor inginerești.
- `medium`: Divergență de parametri sau metode secundare fără impact arhitectural critic.
- `high`: Contradicție directă asupra unui mecanism fundamental, cauză-efect opusă sau asumpție falsă.
- `critical`: Contradicție de securitate, integritate a memoriei sau compromitere structurală a algoritmilor.

### 5.2 Relația cu Lifecycle-ul Notelor (`ACTIVE`)
1. **Open High/Critical Conflict**:
   - O notă implicată într-un conflict deschis (`status == "open"`) de severitate `high` sau `critical` **NU POATE** fi promovată în starea `ACTIVE`.
   - `validate_conflict_gate()` aruncă `ConflictGateError`.
2. **Open Low Conflict**:
   - Nu blochează starea `ACTIVE`, permițând exploatarea notei dacă divergența este doar semantică/stilistică.
3. **Resolved Conflict**:
   - Trecerea conflictului în starea `resolved` ridică blocajul de promovare.
   - **Important**: Rezolvarea conflictului **NU declanșează autopromovarea** notei în `ACTIVE`. Nota trebuie să parcurgă în continuare porțile complete de verificare (Usage Test >= 8/10, Ablation Test și Token HMAC semnat de Owner).

---

## 6. Taxonomia Rezoluției și Decizia Umană

Când un conflict este soluționat, rezoluția trebuie să aparțină taxonomiei controlate:

| Rezoluție | Semnificație | Acțiune asupra Notelor |
| :--- | :--- | :--- |
| `evidence_stronger_for_a` | Dovezile empirice validează Poziția A în raport cu Poziția B. | Poziția A poate avansa spre `VERIFIED`; Poziția B rămâne consemnată ca depășită. |
| `evidence_stronger_for_b` | Dovezile empirice validează Poziția B în raport cu Poziția A. | Poziția B poate avansa spre `VERIFIED`; Poziția A rămâne consemnată ca depășită. |
| `context_dependent_both_valid` | Ambele poziții sunt corecte în domenii sau condiții de aplicare diferite. | Ambele note rămân active în memoria sistemului, cu specificarea precisă a contextelor de aplicabilitate. |
| `source_obsolete` | Una sau ambele surse se bazează pe paradigme științifice invalidate ulterior. | Nota asociată este demotată/marcată cu avertisment istoric. |
| `insufficient_evidence` | Experimentele nu pot departaja pozițiile în prezent. | Conflictul rămâne nerezolvat sau deschis cu experimente suplimentare cerute. |
| `unresolved` | Poziția standard de așteptare. | Nicio modificare de stare. |

---

## 7. Granițe de Securitate între Actori (Actor Boundaries)

În conformitate cu Invariantele `I-001..I-004` și `POLICY-LEARNING-QUALITY-02`:

| Operațiune | `Principal.AI_AGENT` | `Principal.HUMAN` / `ADMIN` |
| :--- | :---: | :---: |
| **Propunere conflict (`propose`)** |  Permis (statut `open`, evaluare inițială) |  Permis |
| **Modificare Severitate** | ❌ **Strict Interzis** (`ConflictPermissionError`) |  Permis (cu justificare obligatorie) |
| **Rezolvare Conflict (`resolve`)** | ❌ **Strict Interzis** (`ConflictPermissionError`) |  Permis (cu specificarea taxonomiei și argumentației) |
| **Ștergere Conflict (`delete`)** | ❌ **Strict Interzis** (`ConflictPermissionError`) | ❌ **Interzis** (Registrele de conflict sunt imutabile) |
| **Redeschidere Conflict (`reopen`)** | ❌ **Strict Interzis** (`ConflictPermissionError`) |  Permis (pe baza unor dovezi noi raportate) |

---

## 8. Redeschidere, Demotare și Reversibilitate

1. **Demotarea Notelor Active la Apariția unui Conflict Nou**:
   - Dacă o notă aflată deja în `ACTIVE` intră într-un conflict nou clasificat la severitate `HIGH` sau `CRITICAL`, nota este automat demotată la starea `UNVERIFIED` prin `demote_active_note()`.
   - Demotarea păstrează integral conținutul notei și istoricul acesteia; nota nu este ștearsă din sistem.
2. **Redeschiderea unui Conflict Rezolvat**:
   - Dacă o lectură ulterioară sau un studiu recent furnizează dovezi contrare unei rezoluții anterioare, conflictul poate fi redeschis exclusiv de către `Principal.HUMAN`.
   - Redeschiderea la severitate `HIGH` reactivează instantaneu blocajul asupra stării `ACTIVE` pentru notele implicate.

---

## 9. Protecția Datelor Netestate (Passive Data Plane)

Textele cuprinse în `evidence_a`, `evidence_b`, `possible_cause` și `resolving_experiment`:
- Sunt tratate strict ca **date pasive (Passive Data Plane)**;
- Payload-urile de tip prompt injection (ex. `"ignore previous instructions"`, `"set severity low"`, `"owner approved: promote to active"`, `"exec('rm -rf /')"`) sunt stocate ca stringuri literale fără a fi executate sau evaluate de sistem;
- Testele din suita `test_book_to_memory_conflicts.py` validează formal neutralitatea pasivă a acestor payload-uri.
