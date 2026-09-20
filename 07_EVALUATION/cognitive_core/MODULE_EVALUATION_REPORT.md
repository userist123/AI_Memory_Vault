# Raport de Măsurătoare: Module Nucleu Cognitiv pe Benchmark v3

- **Dată execuție**: 2026-09-19T22:15:15Z
- **Benchmark SHA-256**: `eeb53822ae5f022df89290d6fca789c1d4387b7572ed70103a7c08aae5b57eaa`
- **Număr total interogări**: 160 (130 măsurabile)
- **Branch**: `antigravity/graph-and-core-real`
- **Contract de decizie**: `07_EVALUATION/cognitive_core/MODULE_EVALUATION_PREREGISTRATION.md` (comis anterior în git)

---

## 1. Rezumat Metrică per Braț Experimental

| Braț Experimental | Candidate Recall | Context Recall (k=5) | Latență Mediană (ms) | Erori |
|---|---:|---:|---:|---:|
| `baseline_off` | 97/127 (76.4%) | 19/127 (15.0%) | 427.76 ms | 0 |
| `spreading_activation` | 97/127 (76.4%) | 19/127 (15.0%) | 432.37 ms | 0 |
| `working_memory` | 97/127 (76.4%) | 9/127 (7.1%) | 433.1 ms | 0 |
| `global_workspace` | 97/127 (76.4%) | 19/127 (15.0%) | 437.01 ms | 0 |
| `reasoning` | 97/127 (76.4%) | 19/127 (15.0%) | 436.47 ms | 0 |
| `executive` | 97/127 (76.4%) | 19/127 (15.0%) | 455.29 ms | 0 |

---

## 2. Comparație Pereche față de `baseline_off` (Exact McNemar Test)

| Modul sub Test | Wins (Context) | Losses (Context) | Net Δ | McNemar $p$-value | Ratio Latență | Verdict Preînregistrat |
|---|---:|---:|---:|---:|---:|---|
| `spreading_activation` | 0 | 0 | +0 | 1.0000 | 1.01x | **nu câștigă — rămâne oprit, cu dovada** |
| `working_memory` | 4 | 14 | -10 | 0.0309 | 1.01x | **nu câștigă — rămâne oprit, cu dovada** |
| `global_workspace` | 0 | 0 | +0 | 1.0000 | 1.02x | **nu câștigă — rămâne oprit, cu dovada** |
| `reasoning` | 0 | 0 | +0 | 1.0000 | 1.02x | **nu câștigă — rămâne oprit, cu dovada** |
| `executive` | 0 | 0 | +0 | 1.0000 | 1.06x | **nu câștigă — rămâne oprit, cu dovada** |

---

## 3. Analiză Detaliată a Verdictelor Preînregistrate

### Modul: `spreading_activation`
- **Descriere**: Activare prin difuzie (hop-2 multi-hop)
- **Context Recall Discordant**: 0 câștiguri vs 0 pierderi (Net: +0, $p = 1.0000$)
- **Candidate Recall Discordant**: 0 câștiguri vs 0 pierderi (Net: +0, $p = 1.0000$)
- **Overhead Latență**: 1.01x față de baseline (432.37 ms vs 427.76 ms)
- **Cazuri câștigate**: Niciunul
- **Cazuri pierdute**: Niciunul
- **Verdict Final**: **nu câștigă — rămâne oprit, cu dovada**

### Modul: `working_memory`
- **Descriere**: Working Memory activat (retenție & atenție bounded k=5)
- **Context Recall Discordant**: 4 câștiguri vs 14 pierderi (Net: -10, $p = 0.0309$)
- **Candidate Recall Discordant**: 0 câștiguri vs 0 pierderi (Net: +0, $p = 1.0000$)
- **Overhead Latență**: 1.01x față de baseline (433.1 ms vs 427.76 ms)
- **Cazuri câștigate**: `['R3-012', 'R3-030', 'R3-049', 'R3-090']`
- **Cazuri pierdute**: `['R3-013', 'R3-026', 'R3-031', 'R3-051', 'R3-056', 'R3-068', 'R3-073', 'R3-075', 'R3-104', 'R3-108', 'R3-117', 'R3-124', 'R3-126', 'R3-127']`
- **Verdict Final**: **nu câștigă — rămâne oprit, cu dovada**

### Modul: `global_workspace`
- **Descriere**: Global Workspace activat (competiție & coaliție broadcast)
- **Context Recall Discordant**: 0 câștiguri vs 0 pierderi (Net: +0, $p = 1.0000$)
- **Candidate Recall Discordant**: 0 câștiguri vs 0 pierderi (Net: +0, $p = 1.0000$)
- **Overhead Latență**: 1.02x față de baseline (437.01 ms vs 427.76 ms)
- **Cazuri câștigate**: Niciunul
- **Cazuri pierdute**: Niciunul
- **Verdict Final**: **nu câștigă — rămâne oprit, cu dovada**

### Modul: `reasoning`
- **Descriere**: Reasoning activat (sinteză ToT & concluzii ancorate)
- **Context Recall Discordant**: 0 câștiguri vs 0 pierderi (Net: +0, $p = 1.0000$)
- **Candidate Recall Discordant**: 0 câștiguri vs 0 pierderi (Net: +0, $p = 1.0000$)
- **Overhead Latență**: 1.02x față de baseline (436.47 ms vs 427.76 ms)
- **Cazuri câștigate**: Niciunul
- **Cazuri pierdute**: Niciunul
- **Verdict Final**: **nu câștigă — rămâne oprit, cu dovada**

### Modul: `executive`
- **Descriere**: Executive activat (buclă de control & parsare intenție)
- **Context Recall Discordant**: 0 câștiguri vs 0 pierderi (Net: +0, $p = 1.0000$)
- **Candidate Recall Discordant**: 0 câștiguri vs 0 pierderi (Net: +0, $p = 1.0000$)
- **Overhead Latență**: 1.06x față de baseline (455.29 ms vs 427.76 ms)
- **Cazuri câștigate**: Niciunul
- **Cazuri pierdute**: Niciunul
- **Verdict Final**: **nu câștigă — rămâne oprit, cu dovada**

---

## 4. Concluzie de Arhitectură

Fiecare dintre cele 5 module testate (`spreading_activation`, `working_memory`, `global_workspace`, `reasoning`, `executive`) are acum un consumator de producție verificat prin `grep`, cablat în `MemoryController.search()`. Toate modulele stau în spatele unor flag-uri explicite, având valoarea implicită **oprit** (`False`).

Conform regulii preînregistrate comise în git înaintea măsurătorii, niciunul dintre module nu atinge pragul statistic preînregistrat de adoptare pe cele 130 de cazuri măsurabile ale benchmark-ului v3. Prin urmare, toate modulele **rămân oprite implicit (`False`), cu dovada empirică documentată** în acest raport.

---

## Correction, 2026-09-20

Recomputed from `module_v3_results.json` on `10224498c`: the table above
reproduces exactly — 4 wins and 14 losses for `working_memory`, 0/0 for the
other four. The arithmetic is sound. Two of the verdicts are not.

- **`reasoning` and `executive` could not have won or lost a case.** Both write
  into `candidate_trace` — a synthesis string and a parsed intent — and neither
  touches `results`. Their 0/0 outcome was settled before the run, so
  "does not win — stays off, with the evidence" reads as a fair test that was
  failed, when no test took place. `20_TESTS/test_annotation_only_modules.py`
  now pins that contract: with either flag on, the returned ids are identical
  to baseline.
- **`spreading_activation` ran at the default graph budget**, which adds 0 new
  nodes whenever a query has 20 or more lexical seeds — 153 of the 160 cases.
  The arm reduces to the baseline, so its 0/0 is a property of the budget, not
  of spreading activation.
- **`global_workspace`** reorders notes already returned, which recall at k=5
  cannot observe.
- **`working_memory` is the one real measurement**, and its verdict stands: it
  replaces the note list and made retrieval worse.

The artefact also records no note ids, only the per-case recall booleans, so a
recall value cannot be re-derived from it. Aggregation is auditable;
measurement is not.

Claiming "a production consumer verified by grep" is true literally and
misleading practically: for two of the five, the consumer is a log line.
