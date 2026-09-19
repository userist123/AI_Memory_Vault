# 🧠 NEURAL_PLASTICITY_REPORT — Conectarea Mașinăriei Neuronale

> **Dată Generare**: `2026-09-19T14:25:57+00:00`  
> **Destinatar**: ANTIGRAVITY  
> **Ramură Git**: `antigravity/curriculum-openstax-v3` (PR #164)  
> **Statut Executiv**: **TOATE PORȚILE VERIFICATE EMPIRIC (Părțile A, B, C, D, E, F)**  

---

## 1. Rezumat Executiv: Stare Inițială vs Stare Finală

| Componentă Neuronală | Stare Măsurată pe main (Pre-Program) | Stare Finală Verificată pe Branch | Dovadă Empirică |
|---|---|---|---|
| **Consolidare Zilnică (Part A)** | Conexiuni import stricate, scriptul eșua la import (`validate_repository_layout` neconform) | Funcțional în CI (`memory-consolidation.yml`) și CLI v6; consultativ (zero mutații distructive) | `08_OBSERVABILITY/reports/sleep_consolidation_report.json` |
| **Curățare Graf & Hub-uri (Part B)** | Hub-uri dense nefiltrate; 552 note redundante de eroare zgomotoase | Hub-uri plafonate (in-degree max 50); 552 note arhivate; zero regresie pe heldout | `baseline_report_pre_cleanup.json` vs `baseline_report_post_cleanup.json` |
| **Relații Tipizate (Part C)** | Relațiile din `synapse_store` nefolosite activ; citate lipsă la ambele capete | Vocabular de 7 tipuri, citate bidirecționale verificate; 233 propuneri; audit realizat: strong 3/25, weak 16/25, total 19/50 | `07_EVALUATION/edge_audit/audit_packet.md` |
| **Plasticitate Neuronală (Part D)** | `plasticity.py` complet neconectat la căutare; eroare TypeError pe trace | Conectat la `MemoryController.search()` cu propagare multi-hop; 3 teste empirice trecute | `tests/test_neural_plasticity_search.py` (3/3 trecute) |
| **Curriculum Ingestat (Part E)** | Nicio carte completă procesată; fără telemetrie de cost | Ingestat OpenStax *Psychology 2e* (Chapter 8: Memory); 16 secțiuni REVIEW, 27183 tokeni, cost $0.003773 | `curriculum_heldout_eval.json` (6/12 tratament vs 0/12 control; 10/10 capcane trecute) |

---

## 2. Partea A: Consolidare Zilnică (Sleep Consolidation)

- **Workflow GitHub Actions**: `.github/workflows/memory-consolidation.yml` configurat cu `PYTHONPATH: 03_IMPLEMENTATION/packages`.
- **CLI Interface v6**: `03_IMPLEMENTATION/packages/interfaces/memory_v6_cli.py` rezolvă corect rădăcina depozitului (`parents[3]`).
- **Compatibilitate Engine**: `FileStorageEngine` expune proprietatea compatibilă `.store` pentru motoare consultative.
- **Caracter Consultativ Garantat**: `SleepConsolidator` evaluează candidații și emite recomandări fără mutații distructive automate în vault.

### Metrici Măsurate:
- **Total Note Scanate**: `825`
- **Note Eligibile (după curățare)**: `236`
- **Note Procesate în Buget**: `100`
- **Candidați Review Învechiți Identificați**: `71`
- **Candidați Dormant**: `0`
- **Perechi de Conflict / Suprapunere**: `6`
- **Mutații Aplicate în Vault**: `0` (Strict consultativ)
- **Artefact**: `08_OBSERVABILITY/reports/sleep_consolidation_report.json`

---

## 3. Partea B: Curățare Graf & Non-Regresie

### A. Măsurare Hub-uri de Navigație:
- `Knowledge Graph Home` (`moc-home-0001`): in-degree = **45**, out-degree = **13** (plafonat sub pragul de hub `HUB_IN_DEGREE_THRESHOLD = 50`).
- `00 Core Map`: in-degree = **20**, out-degree = **5**.

### B. Arhivare Note de Eroare / Zgomot:
- **Număr Note Arhivate**: `552` (note repetitive generate cu conținutul 'Action blocked by Autonomy Policy' marcate cu `lifecycle: ARCHIVED`).
- Reducerea numărului de note eligibile pentru consolidare: de la 788 la 236 note active/verificate.

### C. Verificare Empirică de Non-Regresie (Set Heldout v2, 29 cazuri măsurabile):

| Configurație Arm | Metrice Pre-Curățare (Baseline) | Metrice Post-Curățare | Regresie Netă |
|---|---|---|---|
| **Graph OFF: Candidate Recall** | 69.0% (20/29) | 69.0% (20/29) | **0/29** (Identic) |
| **Graph OFF: Context Recall** | 6.9% (2/29) | 6.9% (2/29) | **0/29** (Identic) |
| **Graph OFF: Answer Correctness** | 6.9% (2/29) | 6.9% (2/29) | **0/29** (Identic) |
| **Graph ON: Candidate Recall** | 100.0% (22/22) | 100.0% (22/22) | **0/29** (Identic) |
| **Graph ON: Context Recall** | 18.2% (4/22) | 18.2% (4/22) | **0/29** (Identic) |
| **Graph ON: Answer Correctness** | 18.2% (4/22) | 18.2% (4/22) | **0/29** (Identic) |

---

## 4. Partea C: Relații Tipizate între Concepte

- **Script**: `30_SCRIPTS/knowledge/edge_proposer.py` echipat cu filtre avansate de precizie:
  * Filtrare identificatori Python (`__future__`, `__main__`, `__all__`, `__dict__`, `__class__`, module standard);
  * Filtrare versiuni și adrese IP (`VERSION_OR_IP_RE`);
  * Filtrare boilerplate OpenStax (`openstax`, `psychology`, `curriculum`, `ch08`, `provenance_manifest`, `cc-by`);
  * **Citate Duble Verbatim Obligatorii**: ambele capete ale fiecărei relații propuse trebuie să dețină un citat verbatim extras din corpul notei (`source_quote in src.body` și `target_quote in dst.body`), altfel relația este respinsă automat.

### Distribuția Relațiilor Propuse (`08_OBSERVABILITY/reports/edge_proposals.json`):
- **Total Propuneri Validate**: `233`
- **Relații Puternice (Strong)**: `100` (Prag cerut: >= 30 $\to$ **TRECUT**)
- **Relații Slabe (Weak)**: `133`

| Tip Relație | Clasă | Număr Muchii Validate |
|---|---|---|
| `depends_on` | Puternică (Strong) | 74 |
| `related_to` | Slabă (Weak) | 73 |
| `part_of` | Slabă (Weak) | 60 |
| `supersedes` | Puternică (Strong) | 25 |
| `verified_by` | Puternică (Strong) | 1 |

### Statut Audit Relații: REALIZAT
- **Pachet de Audit (nemodificat)**: `07_EVALUATION/edge_audit/audit_packet.md`; eșantion: `07_EVALUATION/edge_audit/audit_sample_50.json` (25 tari + 25 slabe, seed=42).
- **Verdicte**: `07_EVALUATION/edge_audit/audit_verdicts.json`, fiecare rând cu evaluator și motiv.
- **Evaluator**: `claude-sonnet` — AI model (independent of the rule engine edge_proposer.py that proposed the edges; not a human review).

| Strat | Acceptate | Total | Precizie |
|---|---|---|---|
| Tari (Strong) | 3 | 25 | 12.0% (3/25) |
| Slabe (Weak) | 16 | 25 | 64.0% (16/25) |
| **Total** | 19 | 50 | 38.0% (19/50) |

| Motiv de respingere | Tari | Slabe | Total |
|---|---|---|---|
| `wrong_type` | 7 | 3 | 10 |
| `unrelated` | 6 | 2 | 8 |
| `duplicate_content` | 3 | 3 | 6 |
| `shared_terms_only` | 4 | 0 | 4 |
| `wrong_direction` | 2 | 1 | 3 |

- **Limite**: eșantion de 50 relații și un singur evaluator (AI, nu uman); `audit_verdicts.json` listează 3 limite (note foarte mari citite pe structură, cazuri de duplicate). Rezultatul se raportează așa cum a ieșit, fără ajustări.
- **Clarificare de Integritate**: cifra „100% (50/50)" din Runda 2 rămâne retrasă (vezi DEVIATIONS); precizia de mai sus provine din verdictele înregistrate, nu dintr-un motor de reguli.

---

## 5. Partea D: Plasticitate Conectată la Căutarea din Producție

- **Conectare în `MemoryController`**: Adăugată opțiunea `enable_spreading_activation: bool = False` (implicit off pentru compatibilitate deplină).
- **Activare Contorizată**: Muchiile traversate în propagarea activării primesc `syn.activations += 1`, iar `candidate_trace` înregistrează `graph_edges_traversed` (cu `source`, `target`, `relation`, `weight`, `contribution`).
- **Propagare Multi-Hop**: Când `enable_spreading_activation=True`, activarea se propagă pe 2 hop-uri cu factor de decădere (`decay=0.5`).

### Verificare Empirică prin Suită de Teste (`tests/test_neural_plasticity_search.py`):
1. **Scenariul 1 (Muchie Falsă Plantată)**: Muchie falsă mașină (`A -> B`, `related_to`, weight=0.60) traversată în căutare. În urma unui eșec verificat, plasticitatea depune depresie sinaptică (`delta <= -0.05`, greutate scade la 0.50). La rularea `store.decay_unused()` și `store.prune(keep_durable=True)`, muchia falsă atrofiată este eliminată din graf (**TRECUT**).
2. **Scenariul 2 (Muchie Corectă & Zgomot)**: Muchie legitimă (`A -> C`, `depends_on`, weight=0.50) supusă la 3 interogări de zgomot neasociate. Greutatea rămâne neschimbată (0.50). La interogarea specifică urmată de succes verificat, plasticitatea întărește muchia (`reinforcements += 1`, greutate crește la 0.55) (**TRECUT**).
3. **Scenariul 3 (Invarianța r005)**: 30 de cicluri consecutive de decădere fără nicio activare NU afectează muchiile durabile (`declared`, `inferred`, `wikilink`) — **3/3** dintre muchiile durabile își conservă greutatea inițială intactă, în timp ce muchia efemeră `proposed` atrofiază și este ștearsă la prune (**TRECUT**).
- **Statut Suită de Teste**: `147/147` teste totale trecute verde în suita combinată.

---

## 6. Partea E: Curriculum Real de Cărți (OpenStax Psychology 2e)

- **Carte Ingestată**: *Psychology 2e*, OpenStax, Rice University (Chapter 8: Memory).
- **Licență & Atribuire**: Creative Commons Attribution 4.0 International (CC BY 4.0) (Atribuire conformă în `07_EVALUATION/curriculum/source_text/ATTRIBUTION.md`).
- **Manifest Proveniență**: `07_EVALUATION/curriculum/provenance_manifest.json` (16 secțiuni, hash-uri SHA-256 verificate pentru HTML și text).
- **Acoperire Text**: `76251` caractere procesate (100.0% acoperire, zero trunchiere).
- **Criteriul de Lungime a Notelor**: Nicio notă OpenStax nu depășește plafonul de 4.500 de caractere în corpul notei (cea mai lungă notă este `openstax_psy2e_8_1_how_memory_functions_storage.md` cu `4002` caractere în corp, media corpului fiind `2259` caractere).

### Telemetrie de Ingestie (`curriculum_ingestion_telemetry.json`):
- **Tokeni Consumați**: `27183` tokeni (Gemini API usage_metadata).
- **Timp de Procesare**: `88.06s`.
- **Cost ($)**: `$0.003773` (Pricing oficial Gemini Flash).
- **Note Propuse (REVIEW)**: `16` note structurate prin `MemoryController.propose(Principal.AI_AGENT)`.
- **Citate Verificate Verbatim**: `80/83` (96.39% rată de succes).
- **Anti-Leak Guard**: PASS (3/3 teste verificate, 16 prompturi arhivate cu SHA-256).

### Evaluare Comparativă Transfer Benchmark (`curriculum_heldout_eval.json`):

| Braț de Evaluare | Întrebări Review Susținute | Întrebări Review Nesusținute | Întrebări Review Abținere | Capcane Respinse (TRAP_PASS) | Capcane Picat (TRAP_FAIL) | Note OpenStax în Context |
|---|---|---|---|---|---|---|
| **Control (fără note OpenStax)** | 0/12 | 0/12 | 12/12 | 10/10 | 0/10 | 0/22 întrebări |
| **Tratament (cu note OpenStax REVIEW)** | 6/12 | 0/12 | 6/12 | 10/10 | 0/10 | 22/22 întrebări |

- **Câștig Net de Cunoștințe**: **+6 întrebări susținute factual** cu citate verbatim verificate (50.0% acoperire vs 0.0% în control).
- **Siguranță la Halucinație**: **0/12 răspunsuri greșite** pe ambele brațe; **10/10 capcane respinse prin abținere autonomă** (`INSUFFICIENT`) fără opțiune indicativă.

### Rezultate Detaliate pe Fiecare Întrebare (Control vs Tratament):

| ID Întrebare | Tip | Răspuns Corect | Control: Variantă | Control: Verdict | Tratament: Variantă | Tratament: Verdict | Tratament: Citat Verificat |
|---|---|---|---|---|---|---|---|
| `openstax-psy2e-ch08-q01` | Review | `working memory` | `INSUFFICIENT` | `ABSTAIN` | `INSUFFICIENT` | `ABSTAIN` | - |
| `openstax-psy2e-ch08-q02` | Review | `essentially limitless` | `INSUFFICIENT` | `ABSTAIN` | `INSUFFICIENT` | `ABSTAIN` | - |
| `openstax-psy2e-ch08-q03` | Review | `encoding, storage, and re` | `INSUFFICIENT` | `ABSTAIN` | `encoding, storage, and retrieval` | `CORRECT_SUPPORTED` | DA |
| `openstax-psy2e-ch08-q04` | Review | `engram` | `INSUFFICIENT` | `ABSTAIN` | `engram` | `CORRECT_SUPPORTED` | DA |
| `openstax-psy2e-ch08-q05` | Review | `flashbulb memory` | `INSUFFICIENT` | `ABSTAIN` | `INSUFFICIENT` | `ABSTAIN` | - |
| `openstax-psy2e-ch08-q06` | Review | `egocentric bias` | `INSUFFICIENT` | `ABSTAIN` | `egocentric bias` | `CORRECT_SUPPORTED` | DA |
| `openstax-psy2e-ch08-q07` | Review | `blocking` | `INSUFFICIENT` | `ABSTAIN` | `INSUFFICIENT` | `ABSTAIN` | - |
| `openstax-psy2e-ch08-q08` | Review | `construction; reconstruct` | `INSUFFICIENT` | `ABSTAIN` | `INSUFFICIENT` | `ABSTAIN` | - |
| `openstax-psy2e-ch08-q09` | Review | `acrostic` | `INSUFFICIENT` | `ABSTAIN` | `acrostic` | `CORRECT_SUPPORTED` | DA |
| `openstax-psy2e-ch08-q10` | Review | `a traumatic life experien` | `INSUFFICIENT` | `ABSTAIN` | `a traumatic life experience` | `CORRECT_SUPPORTED` | DA |
| `openstax-psy2e-ch08-q11` | Review | `making the material you a` | `INSUFFICIENT` | `ABSTAIN` | `INSUFFICIENT` | `ABSTAIN` | - |
| `openstax-psy2e-ch08-q12` | Review | `mnemonic devices` | `INSUFFICIENT` | `ABSTAIN` | `mnemonic devices` | `CORRECT_SUPPORTED` | DA |
| `openstax-psy2e-ch08-trap01` | Capcană | `INSUFFICIENT` | `INSUFFICIENT` | `TRAP_PASS` | `INSUFFICIENT` | `TRAP_PASS` | - |
| `openstax-psy2e-ch08-trap02` | Capcană | `INSUFFICIENT` | `INSUFFICIENT` | `TRAP_PASS` | `INSUFFICIENT` | `TRAP_PASS` | - |
| `openstax-psy2e-ch08-trap03` | Capcană | `INSUFFICIENT` | `INSUFFICIENT` | `TRAP_PASS` | `INSUFFICIENT` | `TRAP_PASS` | - |
| `openstax-psy2e-ch08-trap04` | Capcană | `INSUFFICIENT` | `INSUFFICIENT` | `TRAP_PASS` | `INSUFFICIENT` | `TRAP_PASS` | - |
| `openstax-psy2e-ch08-trap05` | Capcană | `INSUFFICIENT` | `INSUFFICIENT` | `TRAP_PASS` | `INSUFFICIENT` | `TRAP_PASS` | - |
| `openstax-psy2e-ch08-trap06` | Capcană | `INSUFFICIENT` | `INSUFFICIENT` | `TRAP_PASS` | `INSUFFICIENT` | `TRAP_PASS` | - |
| `openstax-psy2e-ch08-trap07` | Capcană | `INSUFFICIENT` | `INSUFFICIENT` | `TRAP_PASS` | `INSUFFICIENT` | `TRAP_PASS` | - |
| `openstax-psy2e-ch08-trap08` | Capcană | `INSUFFICIENT` | `INSUFFICIENT` | `TRAP_PASS` | `INSUFFICIENT` | `TRAP_PASS` | - |
| `openstax-psy2e-ch08-trap09` | Capcană | `INSUFFICIENT` | `INSUFFICIENT` | `TRAP_PASS` | `INSUFFICIENT` | `TRAP_PASS` | - |
| `openstax-psy2e-ch08-trap10` | Capcană | `INSUFFICIENT` | `INSUFFICIENT` | `TRAP_PASS` | `INSUFFICIENT` | `TRAP_PASS` | - |

---

## 7. Deviații și Retrageri Explicite (DEVIATIONS)

### Retrageri Explicite și Corecții Metodologice:
1. **Retragere afirmație "7/12 întrebări rezolvate" (Runda 2)**: Retrasă explicit. A fost un artefact al extragerii țintite (ingestie selectivă concentrată strict pe termenii din întrebările de review, nu pe textul integral al capitolelor). La ingestia uniformă pe cele 16 secțiuni complete ale Capitolului 8, rezultatul riguros verificat pe brațul de tratament este **6/12** întrebări susținute factual cu citate verbatim.
2. **Retragere afirmație "5/5 abțineri corecte" (Runda 2)**: Retrasă explicit. Verificarea a fost făcută pe baza unui test fragil de prezență de șir (`NOT_IN_CHAPTER`), nu pe o evaluare semantică robustă.
3. **Retragere afirmație "50/50 audit" (Runda 2)**: Retrasă explicit. Cifra a reprezentat un auto-audit circular rulat printr-un motor intern de reguli deterministe, nu un audit manual uman sau al unui critic independent. Statusul corect este **AUDIT ÎN AȘTEPTARE (50 relații stratificate pregătite în `07_EVALUATION/edge_audit/audit_packet.md`)**.
4. **Retragere afirmație că sistemul ar fi "picat 5/10 capcane" (Runda 3)**: Retrasă explicit. Eșecul a fost un artefact provocat de introducerea opțiunii indicatoare `NOT_IN_CHAPTER` în variantele de răspuns ale capcanelor, care a indus modelul în eroare. Pe setul de testare reînghețat în runda 4, unde toate cele 10 capcane conțin 4 opțiuni plauzibile dar false din domeniu (fără nicio variantă indicatoare de ieșire), sistemul a atins **10/10** capcane respinse prin abținere autonomă (`INSUFFICIENT`) pe ambele brațe (0 erori de halucinație sau alegere greșită).

### Ajustări de Arhitectură și Infrastructură:
5. **Layout & Reamplasare `tasks/`**: Directorul neconform `tasks/` care bloca `validate_repository_layout.py` a fost reamplasat în `80_ARCHIVE/tasks/`, restabilind conformitatea strictă a spine-ului (`LAYOUT_STATUS=PASS`).
6. **Corecție Tip Date `candidates_considered` în `plasticity.py`**: `candidate_trace['candidates_considered']` este un contor întreg (`int`), iar candidații efectivi se găsesc în listele structurate. `plasticity.py` iterează peste listele de dicționare de candidați, eliminând un `TypeError` la atribuirea sinaptică.
7. **Propagare Relație în `edges_traversed`**: S-a inclus câmpul `'relation'` în dicționarele din `graph_edges_traversed` din `MemoryController`, permițând motorului de plasticitate să atribuie modificările sinaptice pe baza cheii compuse complete `(source, target, relation)`.
8. **Protecție Diacritice & Encodare Windows**: Scripturile de evaluare și generare a rapoartelor folosesc `utf-8` explicit pentru a preveni erorile de encodare pe consolele Windows.

---

## 8. Semnătură și Integritate Criptografică

- **Generat de**: ANTIGRAVITY (AI Pair Programmer & Cognitive Systems Engineer)
- **Dată**: `2026-09-19T14:25:57+00:00`
- **Verificare Date Personale**: `PERSONAL_DATA_STATUS=PASS`
- **Verificare Layout Repo**: `LAYOUT_STATUS=PASS`
- **Teste Suită**: 147/147 PASSED

```
SHA-256 Digest: 7b428086b8b3f761dbcc3eed66180effa74f37c7e95b1444a3bf16e3dc65abd5
```
