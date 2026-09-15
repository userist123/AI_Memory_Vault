# Raport de Verificare și Validare: Manifestul de Verdicte de Promovare

> **Status**: FINALIZAT — CONFORM CU SPECIFICAȚIA  
> **Dată**: 2026-09-12  
> **Autor**: ANTIGRAVITY  
> **Ramură**: `antigravity/r064-manifest-conversion` (bază: `origin/r064-verdicts-gate`, PR #112)  
> **Fișiere Generate**:
> - `07_EVALUATION/book_corpus_conversion/promotion_verdicts.json`
> - `07_EVALUATION/book_corpus_conversion/VERDICTS_MANIFEST_REPORT.md`

---

## 1. Distribuția Reală Obținută vs. Așteptată

Manifestul de verdicte `07_EVALUATION/book_corpus_conversion/promotion_verdicts.json` a fost generat prin parsarea și maparea fidelă, fără nicio re-judecare sau alterare de fond, a celor 91 de rânduri din **Tabelul 2** din `07_EVALUATION/book_corpus_conversion/PROMOTION_REVIEW.md`.

Toate numele de concepte au fost curățate de caractere Markdown (`**`, backticks ```` ` ````) și spații redundante. Toate intrările conțin justificarea `reason` extrasă direct din coloana *Justificare Grounded pe Text*.

| Verdict în Recenzie | Verdict în Manifest | Așteptat | Obținut Real | Diferență |
|---|---|---|---|---|
| `PROMOVEAZĂ` | `PROMOTE` | 48 | **48** | 0 |
| `FUZIONEAZĂ` | `MERGE` | 8 | **8** | 0 |
| `DESPARTE` | `SPLIT` | 1 | **1** | 0 |
| `RESPINGE` | `REJECT` | 28 | **28** | 0 |
| `NESIGUR` | `UNSURE` | 6 | **6** | 0 |
| **TOTAL** | | **91** | **91** | **0** |

Toate cele 91 de intrări sunt unice în manifest (`len(manifest) == 91`), respectă schema `polymarket-promotion-verdicts.v1` și au fost validate cu `promotion_verdicts.load_manifest()`.

---

## 2. Verificarea Celor 8 Ținte `merge_into` în Fișierele de Slot

Fiecare dintre cele 8 verdicte de tip `MERGE` specifică un concept-țintă prin câmpul `merge_into`. Toate cele 8 concepte-țintă au fost verificate pe disc în `01_ARCHITECTURE/ontology/slots/*.md` folosind parserul oficial `30_SCRIPTS/ingestion/slot_rows.py`.

Toate cele 8 ținte există deja în ontologie, au `status=promoted` și dețin un `promoted_note_id` valid generat la commit-ul `596aabfb5`:

| # | Concept Sursă (Cand.) | Slot Sursă | Occ. | Țintă `merge_into` | Fișier Slot Țintă | Linie | Status | ID Notă Promovată (`promoted_note_id`) |
|---|---|---|---|---|---|---|---|---|
| 8 | `long-term potentiation` | `consolidation` | 5 | `consolidation` | `16_consolidation.md` | 35 | `promoted` | `b2888875-8546-4d42-b262-2c7f779e3cc9` |
| 11 | `homeostasis` | `constraints` | 6 | `essential variables` | `08_constraints.md` | 30 | `promoted` | `df11fec2-d5b4-4b8f-b79e-d4dbb18d3d57` |
| 14 | `experiential memory` | `history` | 8 | `episodic memory` | `11_history.md` | 30 | `promoted` | `b01db913-6bf2-4b14-a37a-81cb4b336d93` |
| 15 | `tie impasse` | `history` | 5 | `impasse` | `11_history.md` | 31 | `promoted` | `4dcd44ba-c65e-4928-804a-d3d8f0bdd888` |
| 35 | `factual memory` | `ontology` | 9 | `semantic memory` | `03_ontology.md` | 33 | `promoted` | `84ef8b0f-d457-4741-be32-582ba76e3ab3` |
| 38 | `working model` | `ontology` | 7 | `buffer` | `05_state.md` | 43 | `promoted` | `ff28fea6-0138-41b6-acd7-e1c0759966b1` |
| 52 | `stable model` | `ontology` | 3 | `parametric memory` | `03_ontology.md` | 32 | `promoted` | `519ef025-222f-485e-b7ee-1684078fe6e7` |
| 91 | `contextual memory` | `state` | 3 | `working memory` | `05_state.md` | 38 | `promoted` | `c67c2beb-e722-4155-ad31-66a48823aa20` |

---

## 3. Rezolvarea Enigmei 90 vs. 91 (De unde provine conceptul suplimentar)

În ordinul de lucru s-a semnalat o aparentă discrepanță matematică:
> *„133 de concepte au `occurrences >= 3` în agregat, 43 erau deja promovate, ceea ce lasă 90 — nu 91, cât evaluează recenzia ta. Diferența e de un singur concept. Găsește-l și spune care e; s-ar putea să fie un rând evaluat de două ori, sau unul intrat sub prag. Nu-l trata ca eroare de rotunjire.”*

### Cauza Exactă Identificată Empiric

A fost inspectată intersecția dintre setul celor **43 de concepte promovate** existente în sloturi și cele **133 de concepte cu `occurrences >= 3`** din agregatul celor 21 de cărți din `staging/`:

1. Din cele 43 de rânduri cu `status=promoted` din sloturi, **exact 42** apar în agregatul staging cu `occurrences >= 3`.
2. Cel de-al 43-lea concept promovat este **`Reservoir Sampling`** (`06_procedures.md`, linia 55).
   - Acesta a fost adăugat la `2026-09-07` ca seed inițial de către Sarfraz et al. (fără numărător de occurrences în slot).
   - **`Reservoir Sampling` NU este prezent deloc în fișierele curente din staging**.
3. Prin urmare:
   $$\text{Concepte în Staging cu } occ \ge 3 = 133$$
   $$\text{Concepte Promovate prezente în Staging cu } occ \ge 3 = 42$$
   $$\text{Concepte Candidate Rămase Nepromovate} = 133 - 42 = \mathbf{91}$$

Testul de diferență simetrică a mulțimilor între conceptele evaluate în Tabelul 2 și $( \text{agregat}_{occ \ge 3} \setminus \text{promovate} )$ a returnat `set()` (mulțimea vidă). Nu a existat niciun concept duplicat, niciun concept sub prag și nicio rotunjire: numărul de 91 este riguros și exact.

---

## 4. Analiza `NO_VERDICT` în Rularea Dry-Run peste Staging

În simularea rulată peste copia temporară a sloturilor (`tempfile.mkdtemp()`):
- Agregatul complet din `staging/` conține **201 concepte unice**.
- Manifestul evaluează **91 de concepte** (cele nepromovate cu $occ \ge 3$).
- Dintre acestea, **48 sunt admise** prin `PROMOTE` și **43 sunt reținute** (`28 REJECT + 8 MERGE + 6 UNSURE + 1 SPLIT`).
- Restul de **110 concepte** apar în grupul `NO_VERDICT` ($201 - 91 = 110$).

### Descompunerea celor 110 Concepte din `NO_VERDICT`

| Categorie | Număr | Explicație |
|---|---|---|
| **Concepte deja promovate** | **42** | Cele 42 de concepte cu $occ \ge 3$ deja promovate în sloturi (commit `596aabfb5`). Nu au fost evaluate în `PROMOTION_REVIEW.md` deoarece erau deja parte integrantă a ontologiei active. |
| **Concepte sub prag** ($occ < 3$) | **68** | Concepte cu frecvență scăzută (1 sau 2 apariții), excluse deliberat de la recenzia preliminară conform regulii de filtrare. |
| **Concepte nepromovate cu $occ \ge 3$** | **0** | **Exact ZERO.** Toți candidații eligibili pentru promovare au o decizie explicită în manifest. |
| **TOTAL** | **110** | |

---

## 5. Revizuire Recomandări / Concepte Re-Evaluate

La recitirea meticuloasă a justificărilor din Tabelul 2:
- **Niciun verdict nu a fost modificat sau considerat greșit.**
- Cazul `familiarity` (#3) rămâne strict `SPLIT`, reflectând homonimia dintre semnalul epistemic perceptual ($occ=3$, `why_we_forget`) și filtrul de RL ($occ=6$, `2504.05840v1`).
- Cazul `plasticity` (#44) rămâne corect clasificat ca `REJECT` (sintagmă generică fără structură ontologică autonomă nouă, mecanismul fizic fiind captat de `synaptic plasticity`).
- Toate cele 6 intrări `UNSURE` (`component process`, `dual memory`, `in-context learning`, `integrated information`, `shared memory`, `symbolic`) rămân reținute la poartă până la decizia arhitecturală a lui Owner.

---

## 6. Ieșirea Brută a Comenzilor de Validare

### Comanda 1: Validare Manifest (`load_manifest`)

**Comandă executată:**
```bash
python -c "import sys; sys.path.insert(0,'30_SCRIPTS/ingestion'); from promotion_verdicts import load_manifest; m=load_manifest('07_EVALUATION/book_corpus_conversion/promotion_verdicts.json'); print('intrari unice:', len(m)); print('distributie:', m.counts())"
```

**Ieșire brută:**
```text
intrari unice: 91
distributie: {'REJECT': 28, 'SPLIT': 1, 'PROMOTE': 48, 'MERGE': 8, 'UNSURE': 6}
```

---

### Comanda 2: Simulare Merge cu Poarta Activă pe Copie Temporară

**Comandă executată:**
```bash
python -c "
import sys, json, glob, os, shutil, tempfile
sys.path.insert(0, '30_SCRIPTS/ingestion')
from merge_candidate_concepts import combine_across_books, find_duplicate_rows, merge_candidate_concepts
rows = []
for p in sorted(glob.glob('staging/*.json')):
    b = os.path.basename(p)
    if b.endswith('_rej.json') or 'report' in b: continue
    d = json.load(open(p, encoding='utf-8'))
    if isinstance(d, list): rows += d
assert not find_duplicate_rows(rows), 'randuri duplicate in staging'
agg = combine_across_books(rows)
json.dump(agg, open('scratch/corpus_aggregate.json','w',encoding='utf-8'))
tmp = tempfile.mkdtemp()
shutil.copytree('01_ARCHITECTURE/ontology/slots', tmp + '/slots')
print(json.dumps(merge_candidate_concepts('scratch/corpus_aggregate.json', tmp + '/slots', verdicts_file='07_EVALUATION/book_corpus_conversion/promotion_verdicts.json'), indent=2, ensure_ascii=False))
"
```

**Ieșire brută:**
```json
{
  "staging_file": "scratch/corpus_aggregate.json",
  "total_records_processed": 48,
  "net_new_concepts_merged": 7,
  "combined_across_books": 0,
  "already_in_slot_file": 41,
  "slot_conflicts": {},
  "withheld_by_verdict": {
    "NO_VERDICT": [
      "12-2 momentum",
      "ACT-R",
      "CLARION",
      "Hierarchical Temporal Memory",
      "Maxwell demon",
      "OpenCog",
      "adaptation",
      "agent",
      "agent architecture",
      "amount of information",
      "anomaly detection",
      "background noise",
      "biological band",
      "buffer",
      "calcineurin",
      "central inhibition",
      "chunking",
      "classical conditioning",
      "cognitive appraisal",
      "cognitive architecture",
      "cognitive band",
      "cognitive science",
      "common vocabulary",
      "communication engineering",
      "communication theory",
      "computational intelligence",
      "computational modeling",
      "computing machine",
      "connectionist",
      "consolidation",
      "constraint",
      "correlation",
      "cue based retrieval",
      "cybernetics",
      "decision cycle",
      "declarative memory",
      "deep learning",
      "early phase of LTP",
      "elaboration phase",
      "emotion",
      "entropy",
      "episodic memory",
      "essential variables",
      "explicit memory",
      "feedback",
      "feeling state",
      "forgetting",
      "frog-in-the-pan momentum",
      "hard fork",
      "homeostat",
      "hybrid",
      "impasse",
      "implicit memory",
      "integrated theories",
      "intelligent systems",
      "knowledge level",
      "late phase of LTP",
      "latent memory",
      "learning from experience",
      "long term identifier",
      "long-term memory",
      "mental imagery",
      "message",
      "metacognition",
      "microneme",
      "nondeclarative memory",
      "online learning",
      "operator",
      "organization",
      "parametric memory",
      "place cells",
      "planning",
      "preference",
      "priming",
      "problem space computational model",
      "procedural memory",
      "production system",
      "psychological theories",
      "quality of momentum",
      "rational band",
      "reactive execution",
      "reasoning",
      "recognition memory",
      "recollection",
      "regulation",
      "reinforcement learning",
      "retrieval",
      "search control",
      "second law of thermodynamics",
      "semantic memory",
      "sensitization",
      "short-term memory",
      "social band",
      "spatial visual system",
      "stability",
      "state",
      "state-determined system",
      "step-mechanism",
      "subgoal",
      "substate",
      "symbol system",
      "synapse specificity",
      "task environment",
      "time series",
      "transducer",
      "transformation",
      "ultrastable system",
      "variety",
      "volatility",
      "working memory"
    ],
    "SPLIT": [
      "familiarity"
    ],
    "REJECT": [
      "agentic memory",
      "architecture",
      "artificial",
      "brain-computer",
      "coherence",
      "consciousness",
      "continual learning",
      "contrastive learning",
      "creb",
      "entanglement",
      "experience",
      "external memory",
      "fine-tuning",
      "functional dissociation",
      "learning",
      "loss",
      "momentum",
      "neural",
      "optimization",
      "plasticity",
      "qualia",
      "quantum",
      "regularization",
      "representation",
      "retrieval-augmented generation",
      "reward",
      "safety",
      "topological"
    ],
    "MERGE": [
      "contextual memory",
      "experiential memory",
      "factual memory",
      "homeostasis",
      "long-term potentiation",
      "stable model",
      "tie impasse",
      "working model"
    ],
    "UNSURE": [
      "component process",
      "dual memory",
      "in-context learning",
      "integrated information",
      "shared memory",
      "symbolic"
    ]
  },
  "gated": true,
  "per_slot_summary": {
    "skills": {
      "added": 0,
      "combined_across_books": 0,
      "already_in_slot_file": 3
    },
    "retrieval": {
      "added": 0,
      "combined_across_books": 0,
      "already_in_slot_file": 4
    },
    "map": {
      "added": 0,
      "combined_across_books": 0,
      "already_in_slot_file": 8
    },
    "ontology": {
      "added": 3,
      "combined_across_books": 0,
      "already_in_slot_file": 6
    },
    "constraints": {
      "added": 0,
      "combined_across_books": 0,
      "already_in_slot_file": 2
    },
    "state": {
      "added": 1,
      "combined_across_books": 0,
      "already_in_slot_file": 3
    },
    "routing": {
      "added": 0,
      "combined_across_books": 0,
      "already_in_slot_file": 3
    },
    "procedures": {
      "added": 3,
      "combined_across_books": 0,
      "already_in_slot_file": 2
    },
    "consolidation": {
      "added": 0,
      "combined_across_books": 0,
      "already_in_slot_file": 3
    },
    "judgement": {
      "added": 0,
      "combined_across_books": 0,
      "already_in_slot_file": 5
    },
    "confidence": {
      "added": 0,
      "combined_across_books": 0,
      "already_in_slot_file": 1
    },
    "relationships": {
      "added": 0,
      "combined_across_books": 0,
      "already_in_slot_file": 1
    }
  }
}
```

---

## 7. Verificarea Integrității Sloturilor Canonice

Directorul canonic `01_ARCHITECTURE/ontology/slots/` este protejat împotriva oricărei modificări accidentale:
- Niciun apel de scriere nu a fost direcționat către sloturile reale.
- Simulările de merge s-au executat strict în directoare temporare izolate (`tempfile.mkdtemp()`).

**Comandă:**
```bash
git diff --stat 01_ARCHITECTURE/ontology/slots/
```

**Rezultat:**
*(Ieșire complet vidă — fișierele de slot sunt 100% neatinse, identice bit-cu-bit).*
