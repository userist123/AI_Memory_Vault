# Triajul Surselor Neverificate (Cele 10 Rânduri Sarfraz et al., 2022)

> **Data Evaluării**: 2026-09-12  
> **Status**: FINAL / VERIFICAT EMPIRIC  
> **Corpus de Referință**: `06_INBOX/Carti/Consolidation/sarfraz22a.txt` & `scratch/agent_corpus/sarfraz22a_chunks.json` (12 chunks)  
> **Ținte Analizate**: Toate cele 10 rânduri cu `status=unverified_source` din `01_ARCHITECTURE/ontology/slots/*.md`.

---

## 1. Constatarea de Fond: Semnătura Dicționarului Static

Toate cele 10 rânduri analizate au pe disc o caracteristică structurală comună care le trădează originea:
- **`evidence` este GOL (`| |`)**
- **`occurrences` este GOL (`| |`)**

Toate rândurile produse de pipeline-ul dinamic de ingestie au ambele câmpuri populate obligatoriu. Această absență reprezintă **amprenta incontestabilă a unui rând scris dintr-un dicționar static presetat**, nu extras dinamic dintr-un text. 

> [!IMPORTANT]
> **Testul Mecanic de Detecție Rapidă**:  
> O regulă mecanică trivială (`assert evidence != "" and occurrences is not None`) ar fi semnalat și izolat instantaneu toate cele 10 rânduri din prima secundă, fără a fi nevoie de lectura lucrării sau auditarea istoricului git. Lipsa dovezilor textuale explică direct de ce **7 din cele 10 rânduri sunt fie fabricate, fie redenumiri redundante**.

---

## 2. Exemplul Paradigmă: Cazul `Experience Replay` (Coliziune și Poluare între Sloturi)

Cazul `Experience Replay` ilustrează exact ce se întâmplă când un artefact static este lăsat în urmă fără arbitraj:

| Slot | Linie | Denumire în Tabelă | Status | Sursă | Evidence | Occurrences |
| :--- | :---: | :--- | :--- | :--- | :---: | :---: |
| [`15_retrieval.md`](file:///c:/Users/user/Documents/Codex/AI_Memory_Vault_CODEX_READY/01_ARCHITECTURE/ontology/slots/15_retrieval.md#L43) | L43 | `Experience Replay` | `unverified_source` | Sarfraz et al. (2022) | *(gol)* | *(gol)* |
| [`16_consolidation.md`](file:///c:/Users/user/Documents/Codex/AI_Memory_Vault_CODEX_READY/01_ARCHITECTURE/ontology/slots/16_consolidation.md#L43) | L43 | `experience replay` | `proposed` | memory_in_the_age_of_ai_agents; sarfraz22a | Citat valid | 3 |

**Consecințe**:
1. **Același concept figurează de două ori**: o dată cu majuscule în slotul `15_retrieval.md` și o dată cu minuscule în `16_consolidation.md`.
2. **Două statusuri contradictorii**: `unverified_source` (moștenit din 7 septembrie) vs `proposed` (generat dinamic pe 12 septembrie).
3. **Două sloturi concurente**: Dicționarul v1 l-a forțat în *Retrieval*, în timp ce ingestia din 12 septembrie l-a clasificat în *Consolidation*.

Rândul din `15_retrieval.md` nu este o intrare legitimă, ci o fantomă a extracției v1 care blochează deduplicarea canonică.

---

## 3. Triajul Exhaustiv al celor 10 Concepte

```
Total Concepte Neverificate: 10
├── RECUPERABIL:       2 (20%) ── Textul le conține verbatim, cu frecvență mare și specificitate certă (fără coliziune).
├── REDENUMIT/COMASAT: 5 (50%) ── Există în text, dar se comasează/subordonează unor concepte deja canonice în ontologie.
└── ABSENT:            3 (30%) ── Sintagma a fost complet fabricată de dicționarul v1.
```

---

### A. Concepte RECUPERABIL (2)

#### 1. `Synaptic Consolidation` (`16_consolidation.md:L46`)
- **Frecvență Reală în Text**: **52 de apariții** în 8 chunks (Chunks 0, 1, 2, 3, 4, 5, 6, 8).
- **Citat Verbatim (Chunk 0 - Introduction)**:
  > *„synaptic consolidation protects previously acquired knowledge by consolidating the importance of parameters."*
- **Verificare de Coliziune**:
  - În `16_consolidation.md:L38` există conceptul `synaptic plasticity` (`status=proposed`, 4 apariții).
  - **Verdict Relație**: **NU este același concept**. În neurobiologie și continual learning, *synaptic plasticity* este mecanismul de achiziție/modificare labilă a conexiunilor în faza inițială de învățare, în timp ce *synaptic consolidation* este mecanismul post-achiziție de stabilizare biochimică/arhitecturală și protecție împotriva suprascrierii. Sunt două stadii complementare distincte.
- **Recomandare**: Recuperabil cu drepturi depline. 52 de apariții în textul de bază al lucrării justifică un rând canonic în `16_consolidation.md`.

#### 2. `Complementary Learning Systems` (`16_consolidation.md:L47`)
- **Frecvență Reală în Text**: **4 apariții** în 3 chunks (Chunks 0, 1, 6).
- **Citat Verbatim (Chunk 0 - Introduction)**:
  > *„inspired by the Complementary Learning Systems (CLS) theory of the brain, uses a dual-memory system..."*
- **Verificare de Coliziune**:
  - Nu există nicio coliziune în `16_consolidation.md` sau în alte sloturi (în `03_ontology.md:L60` există doar `dual memory`, 1 apariție).
  - Teoria CLS (McClelland et al., 1995; Kumaran et al., 2016) este fundamentul teoretic formal pentru sistemele duale de memorie AI.
- **Recomandare**: Recuperabil ca noțiune teoretică structurală majoră.

---

### B. Concepte REDENUMIT / COMASAT (5)

#### 3. `Episodic Memory Buffer` (`11_history.md:L36`)
- **Frecvență Reală în Text**: **2 apariții** în Chunks 0 și 2 (*„an episodic memory buffer that stores past experiences"*).
- **Echivalent Canonic pe Disc**: [`11_history.md:L30`](file:///c:/Users/user/Documents/Codex/AI_Memory_Vault_CODEX_READY/01_ARCHITECTURE/ontology/slots/11_history.md#L30) conține deja promovat conceptul umbrelă **`episodic memory`** cu **50 de apariții** în 7 cărți.
- **Decizie**: **COMASARE definitivă** în `episodic memory`. 2 apariții izolate într-o singură lucrare nu justifică fragmentarea ontologică a conceptului deja promovat cu 50 de apariții. Rândul se purjează în Lotul D1.

#### 4. `Experience Replay` (`15_retrieval.md:L43`)
- **Stare în Text**: 16 apariții ca `experience replay`.
- **Echivalent Canonic pe Disc**: Deja prezent în [`16_consolidation.md:L43`](file:///c:/Users/user/Documents/Codex/AI_Memory_Vault_CODEX_READY/01_ARCHITECTURE/ontology/slots/16_consolidation.md#L43) ca `experience replay` (`proposed`, 3 occurrences).
- **Decizie**: Se purjează din `15_retrieval.md` pentru a elimina duplicatul și conflictul de clasificare.

#### 5. `Semantic Memory Store` (`03_ontology.md:L71`)
- **Căutare Textuală**: `semantic memory store` = 0 apariții, `semantic store` = 0 apariții.
- **Termen Real în Text**: **`semantic memory`** (52 apariții).
- **Echivalent Canonic pe Disc**: Deja prezent și **promovat** în [`03_ontology.md:L33`](file:///c:/Users/user/Documents/Codex/AI_Memory_Vault_CODEX_READY/01_ARCHITECTURE/ontology/slots/03_ontology.md#L33) (`semantic memory`, `status=promoted`, 25 occurrences).
- **Decizie**: Redenumire redundantă inventată de dicționar. Se purjează.

#### 6. `Continual Incremental Learning` (`06_procedures.md:L56`)
- **Căutare Textuală**: `continual incremental learning` = 0 apariții.
- **Termen Real în Text**: **`continual learning`** (14 apariții) și `incremental learning` (7 apariții).
- **Echivalent Canonic pe Disc**: Deja prezent în [`06_procedures.md:L47`](file:///c:/Users/user/Documents/Codex/AI_Memory_Vault_CODEX_READY/01_ARCHITECTURE/ontology/slots/06_procedures.md#L47) (`continual learning`, `status=proposed`, 3 occurrences).
- **Decizie**: Fuziune lexicală artificială. Se purjează.

#### 7. `Catastrophic Forgetting Mitigation` (`08_constraints.md:L45`)
- **Căutare Textuală**: `catastrophic forgetting mitigation` = 0 apariții, `mitigate catastrophic forgetting` = 0 apariții.
- **Termen Real în Text**: **`catastrophic forgetting`** (8 apariții).
- **Echivalent Canonic pe Disc**: Conceptul umbrelă `forgetting` este deja promovat în [`16_consolidation.md:L34`](file:///c:/Users/user/Documents/Codex/AI_Memory_Vault_CODEX_READY/01_ARCHITECTURE/ontology/slots/16_consolidation.md#L34).
- **Decizie**: Sintagmă compusă fabricată prin alipirea sufixului *Mitigation*. Se purjează.

---

### C. Concepte ABSENT (3)

#### 8. `Stochastic Weight Consolidation` (`16_consolidation.md:L48`)
- **Căutare Textuală Exhaustivă**:
  - `stochastic weight consolidation`: **0 apariții**
  - `weight consolidation`: **0 apariții**
  - `stochastic weight`: **0 apariții**
  - `stochastic`: 5 apariții (strict în sintagme precum *stochastic gradient descent*).
- **Origine**: Halucinație a dicționarului static inițial. Lucrarea propune *Synaptic Consolidation*, nu *Stochastic Weight Consolidation*.
- **Decizie**: Total absent. Se purjează ca termen fabricat.

#### 9. `Plasticity-Stability Balance` (`05_state.md:L46`)
- **Căutare Textuală Exhaustivă**:
  - `plasticity-stability balance`: **0 apariții**
  - `plasticity-stability`: **0 apariții**
  - `stability-plasticity`: 3 apariții, strict ca:
    - *„Stability-Plasticity Trade-off"* (2 apariții)
    - *„stability-plasticity dilemma"* (1 apariție: *„tackles the stability-plasticity dilemma that lies at the core of CL..."*)
- **Origine**: Numele *Balance* este absent. În ontologie nu există nici *dilemma*, nici *trade-off*.
- **Decizie**: Sintagmă absentă. Se purjează.

#### 10. `Parameter Importance Weighting` (`07_judgement.md:L47`)
- **Căutare Textuală Exhaustivă**:
  - `parameter importance weighting`: **0 apariții**
  - `importance weighting`: **0 apariții**
  - `parameter importance`: 2 apariții (*„calculating the parameter importance at the filter level"*).
- **Origine**: Termenul nominal compus cu sufixul *Weighting* nu există în text.
- **Decizie**: Sintagmă absentă. Se purjează.

---

## 4. Plan de Curățare: Sub-Loturile D1 și D2

Pentru a menține o pistă de audit ireproșabilă, cele 8 rânduri destinate eliminării sunt separate conceptual în catalogul de purjare în funcție de motivul ștergerii:

### Lotul D1 — REDENUMIT / COMASAT (5 Rânduri)
*Motiv*: Conceptele există deja sub o formă canonică sau propusă în ontologie; menținerea lor creează duplicate inter-sloturi și denaturări lexicale.

| Slot | Concept de Șters | Echivalentul Canonic Păstrat |
| :--- | :--- | :--- |
| `03_ontology.md` | `Semantic Memory Store` | `semantic memory` (`03_ontology.md:L33`, `promoted`) |
| `06_procedures.md` | `Continual Incremental Learning` | `continual learning` (`06_procedures.md:L47`, `proposed`) |
| `08_constraints.md` | `Catastrophic Forgetting Mitigation` | `forgetting` (`16_consolidation.md:L34`, `promoted`) |
| `11_history.md` | `Episodic Memory Buffer` | `episodic memory` (`11_history.md:L30`, `promoted`, 50 occ vs 2 occ) |
| `15_retrieval.md` | `Experience Replay` | `experience replay` (`16_consolidation.md:L43`, `proposed`) |

### Lotul D2 — ABSENT / FABRICAT (3 Rânduri)
*Motiv*: Termenii nu au existat niciodată în corpul lucrării `sarfraz22a.txt`, fiind artefacte generate de tabela regex statică inițială.

| Slot | Concept de Șters | Căutare Efectuată (Dovadă) |
| :--- | :--- | :--- |
| `05_state.md` | `Plasticity-Stability Balance` | `plasticity-stability` = 0; textul folosește `stability-plasticity dilemma/trade-off` |
| `07_judgement.md` | `Parameter Importance Weighting` | `importance weighting` = 0; textul are doar `parameter importance` (2x) |
| `16_consolidation.md` | `Stochastic Weight Consolidation` | `weight consolidation` = 0, `stochastic weight` = 0 |

---

## 5. Sinteza Decizională pentru Owner

1. **Pentru Curățare (8 concepte)**:
   - Sunt adăugate în catalogul [`30_SCRIPTS/ingestion/purge_batches_catalog.json`](file:///c:/Users/user/Documents/Codex/AI_Memory_Vault_CODEX_READY/30_SCRIPTS/ingestion/purge_batches_catalog.json) ca loturile **D1** (5 rânduri) și **D2** (3 rânduri).
   - Rularea deliberată a scriptului cu garda explicită:
     ```bash
     python 30_SCRIPTS/ingestion/purge_rejected_rows.py --batch D1 --allow-status unverified_source --apply
     python 30_SCRIPTS/ingestion/purge_rejected_rows.py --batch D2 --allow-status unverified_source --apply
     ```
     va elimina definitiv aceste 8 artefacte, menținând protecția necondiționată pe conceptele `promoted`.
2. **Pentru Salvare (2 concepte)**:
   - `Synaptic Consolidation` (52 apariții) -> actualizare ca rând complet `proposed` (cu citat și occurrences) în `16_consolidation.md`, fără conflict cu `synaptic plasticity`.
   - `Complementary Learning Systems` (4 apariții) -> actualizare ca rând complet `proposed` în `16_consolidation.md`.
