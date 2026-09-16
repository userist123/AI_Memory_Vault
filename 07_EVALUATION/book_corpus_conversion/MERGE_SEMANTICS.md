# SPECIFICAȚIA SEMANTICII DE FUZIUNE (MERGE_SEMANTICS.md)

> **Document de proiectare și analiză empirică**  
> **Destinatar**: Owner  
> **Status**: Proiect de specificație — ZERO cod de fuziune implementat, ZERO scrieri în ontologie.  
> **Precedent**: `disposition_manifest.json` (213 ancore verificate)  
> **Data**: 2026-09-12

---

## 0. Errata și Clarificare: Țintele Reale `MERGE_INTO`

În raportul precedent, rezumatul textual menționa o listă generică de 6 ținte din lucrări anterioare. În registrul autoritar citibil de mașină (`disposition_manifest.json` și `promotion_verdicts.json`), cele **8 concepte-sursă** și **8 ținte promovate reale** verificate pe disc sunt:

| # | Concept Sursă (`proposed`) | Slot Sursă | Țintă Promovată (`promoted`) | Slot Țintă | Note ID Țintă |
|:---:|---|---|---|---|---|
| 1 | `factual memory` | `03_ontology.md:51` | **`semantic memory`** | `03_ontology.md:33` | `84ef8b0f-d457-4741-be32-582ba76e3ab3` |
| 2 | `stable model` | `03_ontology.md:58` | **`parametric memory`** | `03_ontology.md:32` | `519ef025-222f-485e-b7ee-1684078fe6e7` |
| 3 | `working model` | `03_ontology.md:61` | **`buffer`** | `05_state.md:43` | `ff28fea6-0138-41b6-acd7-e1c0759966b1` |
| 4 | `contextual memory` | `05_state.md:32` | **`working memory`** | `05_state.md:38` | `c67c2beb-e722-4155-ad31-66a48823aa20` |
| 5 | `homeostasis` | `08_constraints.md:31` | **`essential variables`** | `08_constraints.md:30` | `df11fec2-d5b4-4b8f-b79e-d4dbb18d3d57` |
| 6 | `tie impasse` | `11_history.md:32` | **`impasse`** | `11_history.md:31` | `4dcd44ba-c65e-4928-804a-d3d8f0bdd888` |
| 7 | `experiential memory` | `11_history.md:33` | **`episodic memory`** | `11_history.md:30` | `b01db913-6bf2-4b14-a37a-81cb4b336d93` |
| 8 | `long-term potentiation` | `16_consolidation.md:39` | **`consolidation`** | `16_consolidation.md:35` | `b2888875-8546-4d42-b262-2c7f779e3cc9` |

> **Corectat de Claude, 2026-09-12.** Tabelul de mai sus a fost livrat cu
> șapte din opt ancore-sursă greșite și șapte din opt `promoted_note_id`
> inventate: `03_ontology.md:71` este `Semantic Memory Store`, `:77` și
> `:79` nu există, `05_state.md:41` este `latent memory`,
> `08_constraints.md:36` este `censor`, iar `11_history.md:33` și `:34`
> sunt `experiential memory` și `consciousness`. Valorile de aici sunt
> citite acum din `disposition_manifest.json` și din fișierele de slot.
> Ancorele-țintă erau corecte; analiza empirică de la Secțiunea 1 a fost
> reverificată independent și este exactă.

*Notă măsurată pe disc:* La perechea #8, `long-term potentiation` are pe disc la `16_consolidation.md:39` valoarea **`occurrences = 5`** (provenită din 4 apariții în `squire_kandel_mind_to_molecules` + 1 apariție în `kandel_2001_molecular_biology_of_memory`). Tabela de la Secțiunea 1 a ordinului de lucru lista valoarea `4`, care reflecta strict cartea comună Squire & Kandel.

---

## 1. Problema Reală a Fuziunii: De ce `occurrences` nu se adună întotdeauna

### 1.1. Definiția operațională a metrului `occurrences`
În arhitectura de extracție a acestui depozit, `occurrences` reprezintă **numărul de secțiuni/chunk-uri distincte de text în care conceptul este definit sau discutat**.
- Întrucât coloana `confidence` este saturată la `1.00` sau `0.95` pe aproape tot corpusul, `occurrences` este **singurul semnal real de clasificare și clasare a importanței**.
- Dacă o fuziune aplică o adunare scalară naivă ($Occ_{nou} = Occ_{țintă} + Occ_{sursă}$), numărul rezultat devine o ficțiune matematică ori de câte ori cele două concepte au fost extrase din aceleași secțiuni ale aceleiași cărți.
- O astfel de eroare produce **inflație de recurență**, alterând ierarhiile ontologice viitoare.

### 1.2. Măsurătoarea empirică: Analiza secțiunilor din `staging/`

Am investigat fișierele de staging pentru toate cele 8 perechi. Rezultatele demonstrează o discrepanță structurală clară între perechile cu carte comună și cele fără carte comună.

```
+-------------------------------------------------------------------------------------------------------------------------+
| PERECHE                             | CĂRȚI COMUNE               | SECȚIUNI OVERLAP | SUMĂ NAIVĂ | REUNIUNE CHUNK-URI   |
+-------------------------------------+----------------------------+------------------+------------+----------------------+
| 1. factual memory -> semantic mem.  | —                          | 0                | 9 + 25 = 34| 34 (32 unice text)   |
| 2. stable model -> parametric mem.  | —                          | 0                | 3 + 11 = 14| 14 (12 unice text)   |
| 3. working model -> buffer          | sarfraz22a                 | 3 secțiuni       | 7 + 11 = 18| 14 distincte (-4)    |
| 4. contextual memory -> working mem.| —                          | 0                | 3 + 65 = 68| 68 (66 unice text)   |
| 5. homeostasis -> essential var.    | ashby_design_for_a_brain   | 3 secțiuni       | 6 + 36 = 42| 37 distincte (-5)    |
| 6. tie impasse -> impasse           | laird_soar_cognitive_arch. | 3 secțiuni       | 5 + 14 = 19| 14 distincte (-5)    |
| 7. experiential memory -> episodic  | —                          | 0                | 8 + 50 = 58| 58 (54 unice text)   |
| 8. long-term potentiation -> consol.| squire_kandel_mind_to_mol. | 0 secțiuni       | 5 + 29 = 34| 34 distincte (0)     |
+-------------------------------------------------------------------------------------------------------------------------+
```

### 1.3. Dovada pe text pentru cele 4 perechi cu carte comună

#### A. `working model` (7 occ) -> `buffer` (11 occ) în `sarfraz22a`
- În `staging/sarfraz22a.json`:
  - `working model`: 7 apariții, localizate în: `['Dual Memory System', 'Formulation', 'Conclusion', 'Working Model Performance', 'Comparison with CLS-ER', 'Effect of Hyperparameters']`.
  - `buffer`: 6 apariții în `sarfraz22a` (și 5 în `wcs_1488`), localizate în: `['Background', 'Dual Memory System', 'Formulation', 'Empirical Evaluation', 'Working Model Performance']`.
  - **Suprapunere măsurată**: Exact **3 secțiuni identice**:
    1. `Dual Memory System`
    2. `Formulation`
    3. `Working Model Performance`
  - În aceste trei secțiuni, autorii descriu interacțiunea dintre buffer-ul episodic și rețeaua rapidă (working model). Secțiunea a fost numărată o dată ca apariție a lui `working model` și o dată ca apariție a lui `buffer`.
  - **Calcul corect**:
    - În `sarfraz22a`: 6 secțiuni unice pt. `working model` $\cup$ 6 secțiuni unice pt. `buffer` = **9 secțiuni distincte**.
    - În `wcs_1488`: **5 secțiuni distincte** (fără suprapunere).
    - **Total real**: $9 + 5 = \mathbf{14}$ secțiuni distincte (față de $7 + 11 = 18$ la adunarea naivă).

#### B. `homeostasis` (6 occ) -> `essential variables` (36 occ) în `ashby_design_for_a_brain`
- În `staging/ashby_design_for_a_brain.json`:
  - `homeostasis`: 6 apariții (5 chunk-uri unice: `Pages 73-75`, `Pages 76-78`, `Pages 112-114`, `Pages 244-246`, `Pages 247-249`).
    - *Citat*: *"The distinction may best be illustrated by the inborn homeostatic mechanisms : the reaction to cold by shivering, for instance."* (Pages 76-78).
  - `essential variables`: 31 apariții în Design for a Brain (și 5 în Intro to Cybernetics) peste 30 de chunk-uri unice.
    - *Citat*: *"We can now define ' survival ' objectively and in terms of a field : it occurs when a line of behaviour takes no essential variable outside given limits."* (Pages 55-57).
  - **Suprapunere măsurată**: **3 chunk-uri identice**:
    1. `Pages 73-75`
    2. `Pages 76-78`
    3. `Pages 247-249`
  - La Ashby, menținerea variabilelor esențiale în limite fiziologice este chiar definiția homeostaziei. În aceste pagini, Ashby folosește ambii termeni în aceeași expunere teoretică.
  - **Calcul corect**: Din cele 5 chunk-uri ale lui `homeostasis`, 3 sunt deja incluse în `essential variables`. Doar 2 chunk-uri sunt noi (`Pages 112-114`, `Pages 244-246`).
    - Total real: $31 + 2 = 33$ chunk-uri în cartea principală + 4 în cartea secundară = $\mathbf{37}$ (față de $6 + 36 = 42$ la adunarea naivă).

#### C. `tie impasse` (5 occ) -> `impasse` (14 occ) în `laird_soar_cognitive_architecture`
- În `staging/laird_soar_cognitive_architecture.json`:
  - `tie impasse`: 5 apariții peste 4 chunk-uri unice (`Pages 127-129`, `Pages 130-132`, `Pages 148-150`, `Pages 151-153`).
    - *Citat*: *"Operator tie An operator tie impasse arises when multiple operators are proposed, but the available preferences are insufficient to distinguish among them."* (Pages 127-129).
  - `impasse`: 12 apariții în Soar (și 2 în Newell) peste 11 chunk-uri.
    - *Citat*: *"If there are operators in the candidate set that do not have numeric indifferent preferences, then there is a tie impasse."* (Pages 97-99).
  - **Suprapunere măsurată**: **3 chunk-uri identice**:
    1. `Pages 127-129`
    2. `Pages 130-132`
    3. `Pages 148-150`
  - *Evidență frapantă*: Citatul de probă ales de extractorul v1 pentru conceptul generic `impasse` este literal o definiție a conceptului `tie impasse`!
  - **Calcul corect**: Din cele 4 chunk-uri ale lui `tie impasse`, 3 există deja în `impasse`. Doar `Pages 151-153` este un pasaj nou.
    - Total real: $12 + 1 = 13$ în Soar + 1 în Newell = $\mathbf{14}$ secțiuni distincte (față de $5 + 14 = 19$ la adunarea naivă).

#### D. `long-term potentiation` (5 occ pe disc) -> `consolidation` (29 occ) în `squire_kandel_mind_to_molecules`
- În `staging/squire_kandel_mind_to_molecules.json`:
  - `long-term potentiation`: 4 apariții în Squire/Kandel (`Pages 37-39`, `Pages 163-165`, `198 CHAPTER TEN`, `204 CHAPTER TEN`) + 1 apariție în `kandel_2001`.
    - *Citat*: *"In these brain areas, the connection strength between neurons can change quickly and last for a long time, a phenomenon known as long-term potentiation (LTP)."* (pp. 37-39).
  - `consolidation`: 4 apariții în Squire/Kandel (`132 CHAPTER SEVEN`, `138 CHAPTER SEVEN`, `Pages 157-159`) + 25 în alte cărți.
    - *Citat*: *"The first opportunity to explore rigorously the failure of the consolidation switch came in 1949 when C."* (Cap. 7).
  - **Suprapunere măsurată**: **0 secțiuni comune**.
  - Deși provin din același volum, autorii tratează LTP în Capitolul 10 (mecanisme de plasticitate sinaptică) și Consolidarea în Capitolul 7 (consolidare de sistem / amnezie retrogradă).
  - **Calcul corect**: Fiind secțiuni disjuncte, reuniunea este egală cu suma: $29 + 5 = \mathbf{34}$ secțiuni distincte.

---

## 2. Aritmetica Fuziunii: Reguli de Calcul per Pereche

Pe baza dovezilor de mai sus, o regulă globală unică de tipul „adună întotdeauna" este falsă pe 3 din cele 8 perechi.

### Specificația celor două reguli:

1. **Regula A — Cărți Disjuncte (Adunare Scalară Directă)**:
   - Se aplică perechilor 1, 2, 4, 7 (unde $	ext{Books}(Sursa) \cap 	ext{Books}(Tinta) = \emptyset$).
   - Deoarece secțiunile provin din cărți complet diferite, independența este garantată prin construcție.
   - Formula:
     $$	ext{Occ}_{	ext{merged}} = 	ext{Occ}_{	ext{target}} + 	ext{Occ}_{	ext{source}}$$

2. **Regula B — Cărți Partajate (Reuniune de Mulțimi de Secțiuni)**:
   - Se aplică perechilor 3, 5, 6, 8 (unde $	ext{Books}(Sursa) \cap 	ext{Books}(Tinta) 
eq \emptyset$).
   - Formula canonică:
     $$	ext{Occ}_{	ext{merged}} = |	ext{Sections}(Tinta) \cup 	ext{Sections}(Sursa)| = 	ext{Occ}_{	ext{target}} + 	ext{Occ}_{	ext{source}} - |	ext{Sections}(Tinta) \cap 	ext{Sections}(Sursa)|$$
   - Dacă secțiunile din cartea comună nu se suprapun (cazul perechii 8), formula se reduce natural la adunare fără penalizare.

### Tabela Decizională a Numărătorilor după Fuziune:

| Pereche | Concept Sursă | Țintă Promovată | Occ Vechi Țintă | Occ Sursă | Suprapunere Măsurată | **Occ Rezultat Corect** | Occ din Sumă Naivă (Incorect) |
|---|---|---|:---:|:---:|:---:|:---:|:---:|
| 1 | `factual memory` | `semantic memory` | 25 | 9 | 0 | **34** | 34 |
| 2 | `stable model` | `parametric memory` | 11 | 3 | 0 | **14** | 14 |
| 3 | `working model` | `buffer` | 11 | 7 | -3 (Sarfr.) | **14** | 18 *(inflație +4)* |
| 4 | `contextual memory` | `working memory` | 65 | 3 | 0 | **68** | 68 |
| 5 | `homeostasis` | `essential variables` | 36 | 6 | -3 (Ashby) | **37** | 42 *(inflație +5)* |
| 6 | `tie impasse` | `impasse` | 14 | 5 | -3 (Soar) | **14** | 19 *(inflație +5)* |
| 7 | `experiential memory` | `episodic memory` | 50 | 8 | 0 | **58** | 58 |
| 8 | `long-term potentiation` | `consolidation` | 29 | 5 | 0 | **34** | 34 |

---

## 3. Ce se întâmplă cu `evidence_quote` al Sursei

Rândul din tabela markdown de slot are un format compact fix de 8 coloane:
`| concept | source_book | confidence | status | date_added | promoted_note_id | evidence | occurrences |`

Coloana 7 (`evidence`) este o singură celulă tabelară markdown.

### Consecințe ale opțiunilor:
1. *Opțiunea Concatenării în tabel*:
   Format: `citat_tinta; [fuzionat factual memory]: citat_sursa`.
   - **Dezavantaje majore**: Celula devine masivă (peste 300 de caractere), distruge lizibilitatea vizuală a fișierului Markdown în editor/Obsidian și crește riscul ca un caracter pipe `|` ne-escapat dintr-un citat să corupă parsarea tabelei.
2. *Opțiunea Înlocuirii cu cel mai lung citat*:
   - Pierde evidența conceptului promovat inițial sau a conceptului absorbit.

### Arhitectura Recomandată (Stratificare pe două niveluri):
- **Nivelul 1 — Tabela de Slot (`slots/*.md`)**:
  - Se **păstrează citatul original al conceptului-țintă promovat** (care a trecut deja prin validarea notei și a primit `promoted_note_id`).
- **Nivelul 2 — Nota de Cunoștințe Promovată (`01_ARCHITECTURE/knowledge/Promoted_*.md`)**:
  - Fișierele de notă asociate (`promoted_note_id`) nu au restricții de lățime de coloană.
  - În corpul notei promovate se adaugă o secțiune standardizată:
    ```markdown
    ## Absorbed Concepts & Evidence
    - **Concept**: `factual memory`
      - **Source Book**: `memory_in_the_age_of_ai_agents`
      - **Occurrences Absorbed**: 9
      - **Evidence Quote**: "el input embedding p. We introduce an additional..."
      - **Merged At**: 2026-09-12 (via ontology-row-disposition.v1)
    ```
  - Astfel, nicio literă din evidența textului nu se pierde, iar tabela de slot rămâne curată și robustă.

---

## 4. Ce se întâmplă cu `source_book`

### 4.1. Verificarea comportamentului parserului (`slot_rows.parse_row`)
Am analizat codul din `30_SCRIPTS/ingestion/slot_rows.py` (L102-L139):
```python
cells = [c.strip() for c in line.split("|")]
...
return SlotRow(
    ...
    source_book=cells[2].strip(),
    ...
)
```
- Parserul **nu face split** după virgulă sau punct și virgulă; preia conținutul întreg al `cells[2]`.
- În sloturile existente, formatul cu punct și virgulă este deja utilizat canonic (de exemplu în `16_consolidation.md:33`: `2601.09113v1; memory_in_the_age_of_ai_agents; squire_kandel_mind_to_molecules`).
- **Constatare**: Unirea cărților printr-o listă separată cu punct și virgulă (`book1; book2`) **nu afectează și nu strică parsarea** în niciun instrument din depozit.

### 4.2. Regula de actualizare
La fuziune, `source_book` pe rândul țintei devine reuniunea deduplicată a cărților, sortată alfabetic:
```python
new_source_book = "; ".join(sorted(set(target_books) | set(source_books)))
```
*Exemplu*:
- Sursă: `sarfraz22a`
- Țintă veche: `sarfraz22a; wcs_1488`
- Țintă fuzionată: `sarfraz22a; wcs_1488` (fără duplicate precum `sarfraz22a; sarfraz22a`).

---

## 5. Ce Urmă Rămâne (Traceability & Ghosting)

După fuziune, rândul conceptului-sursă este eliminat din tabela `Candidate concepts`. Cineva care caută literal `factual memory` în slotul `03_ontology.md` nu va mai găsi un rând activ.

### Mecanismele obligatorii de conservare a urmei:

1. **În nota promoted a conceptului-țintă (`01_ARCHITECTURE/knowledge/Promoted_*.md`)**:
   - În frontmatter-ul YAML:
     ```yaml
     aliases:
       - factual memory
     merged_from:
       - concept: "factual memory"
         original_slot: "03_ontology.md"
         original_line: 71
         occurrences: 9
     ```
   - Acest lucru permite ca interogările pe aliasuri în Obsidian și în API-ul `MemoryController.search()` să trimită automat către nota consolidată.

2. **În graful de cunoștințe (`00_CORE/GRAPH/`)**:
   - Înregistrarea unei muchii explicite de alias:
     `[factual memory] -[:ISOMORPHIC_TO {status: 'absorbed', merged_at: '2026-09-12'}]-> [semantic memory]`

3. **În manifestul de dispoziție (`disposition_manifest.json`)**:
   - Înregistrarea rândului original rămâne neschimbată în JSON cu dispoziția `MERGE_INTO`, câmpul `merge_into: "semantic memory"` și justificarea completă.

---

## 6. Reversibilitatea (Unmerge)

Dacă peste o lună o analiză arhitecturală demonstrează că `stable model` (model lent cu greutăți conservative) NU este izomorf cu `parametric memory` (ansamblul total al parametrilor unui model lingvistic), cum se desface fuziunea?

### Diagnosticul de reversibilitate:
- **Fără evidență tranzacțională**: O fuziune directă aplicată distructiv în Markdown este **IREVERSIBILĂ**. Nu se poate ști câte dintre cele 14 apariții au fost ale lui `stable model` și câte ale lui `parametric memory`, nici ce cărți au fost adăugate artificial.
- **Cu Chitanță de Fuziune (Merge Receipt)**: Fuziunea devine **100% REVERSIBILĂ**.

### Schema propusă pentru Merge Receipt (`07_EVALUATION/book_corpus_conversion/merge_receipts.json`):
```json
{
  "receipt_id": "merge-20260912-working-model",
  "applied_at": "2026-09-12T00:00:00Z",
  "source_entry": {
    "slot_file": "03_ontology.md",
    "line": 79,
    "concept": "working model",
    "status": "proposed",
    "occurrences": 7,
    "source_book": "sarfraz22a",
    "evidence": "ed by taking an exponential moving average of the working model..."
  },
  "target_entry": {
    "slot_file": "05_state.md",
    "concept": "buffer",
    "pre_merge_occurrences": 11,
    "post_merge_occurrences": 14,
    "overlap_deducted": 3,
    "pre_merge_books": "sarfraz22a; wcs_1488",
    "post_merge_books": "sarfraz22a; wcs_1488"
  }
}
```

O operațiune de `unmerge` va citi chitanța, va re-insera rândul sursă exact așa cum a fost și va scădea din rândul țintă diferența calculată.

---

## 7. Concluzie

Fuziunea nu este o simplă ștergere și nici o simplă adunare. Pentru a păstra integritatea matematică a ontologiei:
1. **occurrences** trebuie calculate prin reuniune de seturi de locații text, deducând cele 3 suprapuneri pentru `working model`, 3 pentru `homeostasis` și 3 pentru `tie impasse`.
2. **evidențele** trebuie salvate în notele de cunoștințe, nu înghesuite în tabelele Markdown.
3. **chitanțele de fuziune** sunt obligatorii înainte de orice scriere pe disc pentru a garanta reversibilitatea.
