# Raport Diagnostic: Pâlnia Pierderilor de Regăsire (Loss Funnel Diagnostic)

> **Raport de Diagnostic Empiric — Programul de Măsurare (Partea 2, PR 3)**  
> **Depozit**: `userist123/AI_Memory_Vault_CODEX_READY`  
> **Hash Benchmark Înghețat (SHA-256)**: `eeb53822ae5f022df89290d6fca789c1d4387b7572ed70103a7c08aae5b57eaa`  
> **Cazuri măsurabile**: 130 din 160 (30 cazuri de abținere excluse conform protocolului)  
> **Stare**: FINALIZAT — VALIDAT PRIN CONTROALE NEGATIVE ȘI ARTEFACT DETERMINIST  

---

## 1. Declarația Punctelor de Operare și Clarificarea Discrepanței (21 vs 39)

Înainte de prezentarea oricărei distribuții sau analize cauzale, este obligatorie definirea precisă a punctelor de operare măsurate.

În literatura și documentele anterioare ale proiectului (`BENCHMARK_V3_REPORT.md`, `run_retrieval_benchmark_v3.py`), cifra de referință citată este **39 din 130 reușite** (context recall 30.00%). Aceasta a fost măsurată sub punctul de operare **`Principal.HUMAN` cu `page_size=10` și fără prag de ciclu de viață**.

Rularea harnessului de producție sub rolul real de execuție raportează **21 din 130 reușite** (16.15%). Aceasta nu reprezintă o contradicție și nici o regresie de sistem, ci reflectă diferența structurală dintre două puncte de operare distincte:

1. **Punctul de Operare de Producție (Brațul Agent)**:
   - Principal: `Principal.AI_AGENT`
   - Fereastră de paginare: `page_size = 5` (bugetul canonic de context spars definit în `AGENTS.md`)
   - Prag de ciclu de viață: **ACTIV** (`AGENT_LIFECYCLE_FLOOR` filtrează notele care nu sunt `ACTIVE` sau `REVIEW`)
   - Reușite măsurate: **21 din 130** (16.15%, $CI_{95\%}$ [10.82%, 23.44%])

2. **Punctul de Operare Istoric de Referință (Brațul Om)**:
   - Principal: `Principal.HUMAN`
   - Fereastră de paginare: `page_size = 10`
   - Prag de ciclu de viață: **INACTIV** (proprietarul uman are acces neîngrădit la întregul istoric)
   - Reușite măsurate pe starea curentă a depozitului: **38 din 130** (29.23%, $CI_{95\%}$ [22.10%, 37.56%])

3. **Descompunerea Exactă a Diferenței (Cât costă pragul de ciclu de viață vs paginarea)**:
   - **Costul ferestrei de paginare (5 vs 10)**: La același rol `Principal.HUMAN`, reducerea ferestrei de la 10 la 5 scade recall-ul de la 38 la 21 (**-17 cazuri, -13.08 puncte procentuale**). Pe rolul `Principal.AI_AGENT`, trecerea de la 10 la 5 scade recall-ul de la 37 la 21 (**-16 cazuri, -12.31 puncte procentuale**). Fereastra redusă de paginare explică peste 88% din ecart.
   - **Costul pragului de ciclu de viață (`AGENT_LIFECYCLE_FLOOR`)**: 
     - La `page_size = 5`: AI_AGENT obține 21/130, iar HUMAN obține 21/130. Costul pragului de ciclu de viață la punctul de operare al agentului este **exact 0 cazuri din 130 (0.00 puncte procentuale)**, deoarece toate cele 15 note sub pragul de ciclu de viață aveau oricum ranguri $\ge 6$.
     - La `page_size = 10`: AI_AGENT obține 37/130, iar HUMAN obține 38/130. Costul pragului de ciclu de viață este de **exact 1 caz din 130 (0.77 puncte procentuale)**, replicând bit-cu-bit măsurătoarea documentată în `controller.py` (*"the floor costs 1 case out of 130"*).
   - **Deviația istorică (39 vs 38 pe brațul uman)**: 1 caz (`R3-052`) a trecut de la rangul 10 la rangul 14 în urma evoluției numărului de note de tip procedură din depozit între commit-ul înghețat `b3ada1b54` și starea curentă.

### Determinismul Măsurătorilor
Conform validării empirice din Controlul Negativ C (PR 2), **130 din 130 de cazuri sunt perfect identice** pe 3 rulări repetate complete (abatere standard $\sigma = 0.0000$ pe hit-uri, 0 cazuri fluctuante). Testul avertismentului privind spargerea egalităților de scor prin ID-uri aleatorii nu se manifestă în date. Cifrele din prezentul raport sunt **strict deterministe și nu necesită bare de eroare derivate din instabilitatea ordonării**.

---

## 2. Tabelul 1 — Pâlnia Pierderilor de Regăsire
### [Punct de operare: Principal.AI_AGENT, page_size=5, prag ciclu de viață ACTIV]

Din cele 130 de cazuri măsurabile, **21 sunt reușite** (16.15%), iar **109 sunt ratări** (83.85%). Distribuția cauzală a celor 109 ratări se prezintă astfel:

| Categorie Pierdere | Ratări (N=109) | Proporție din Ratări | Interval Wilson 95% | Explicație Mecanică Cauzală |
|:---|:---:|:---:|:---:|:---|
| `PAGINATION_CUT` | **78** | 71.56% | [62.47%, 79.18%] | Nota a fost generată în pool-ul extins de candidați ($\le 200$), dar a fost clasată dincolo de top 5. |
| `AGENT_LIFECYCLE_FLOOR_EXCLUDED` | **15** | 13.76% | [8.52%, 21.47%] | Nota a fost respinsă la politica de stocare deoarece ciclul său de viață este sub prag (`NORMALIZED`, `PROPOSED`, `UNVERIFIED`). |
| `NEVER_CANDIDATE` | **13** | 11.93% | [7.10%, 19.34%] | Nota nu a fost generată ca și candidat de niciun generator lexical (scor nul sau sub limita pool-ului). |
| `CANDIDATE_LIMIT_CUT` | **2** | 1.83% | [0.50%, 6.44%] | Nota a fost generată, dar a depășit plafonul de 200 de candidați fuzionați. |
| `RAW_EXCLUDED` | **1** | 0.92% | [0.16%, 5.01%] | Nota de aur are starea `RAW` și a fost exclusă la politica de securitate a depozitului. |
| `UNDETERMINED` | **0** | 0.00% | [0.00%, 3.40%] | Cauza nu a putut fi atribuită determinist uneia dintre categoriile canonice. |

> [!IMPORTANT]
> **Raportare UNDETERMINED**: Numărul cazurilor neclasificate este **0 (0.00%)**, confortabil sub pragul de alertă de 10%. Trasabilitatea cauzală a pâlniei este de 100%.

---

## 3. Tabelul 2 — Distanța până la Reușită (Distribuția Rangurilor Notei de Aur)
### [Punct de operare: Principal.AI_AGENT, page_size=5, prag ciclu de viață ACTIV]

Distribuția rangului ocupat de nota corectă în clasamentul candidaților determină direct dacă un algoritm de re-clasare (reranker) are sens tehnic:

| Interval Rang | Total Cazuri (N=130) | Proporție Total | Ratări (N=109) | Proporție Ratări | Semnificație pentru Re-clasare |
|:---|:---:|:---:|:---:|:---:|:---|
| 1–5 | **43** | 33.08% | **22** | 20.18% | Reușite imediate sau candidați generați în top 5 dar retrogradați la scorare. |
| 6–10 | **10** | 7.69% | **10** | 9.17% | Zonă imediat recuperabilă de un reranker (efort minim de deplasare). |
| 11–30 | **16** | 12.31% | **16** | 14.68% | Zonă realist recuperabilă de un model Cross-Encoder standard. |
| 31–100 | **23** | 17.69% | **23** | 21.10% | Zonă dificil de recuperat; necesită funcție de scor puternic calibrată. |
| > 100 | **9** | 6.92% | **9** | 8.26% | Zonă profundă; un reranker standard nu recuperează aceste poziții. |
| Neclasată deloc | **29** | 22.31% | **29** | 26.61% | Notă absentă din candidați sau exclusă de politicile de securitate/ciclu de viață. |

- **Rangul median pe ratările clasate**: **20.5**
- **Concluzie critică**: 26 de cazuri ratate se situează în intervalul realist recuperabil (rang 6–30), iar 22 de cazuri au fost generate în top 5 dar retrogradate de funcția actuală de scorare bazată pe euristică. Un reranker adresat clasamentului 1–30 are potențialul de a recupera până la 48 de cazuri.

---

## 4. Tabelul 3 — Plafonul Oracol al Reordonării Candidaților Existenți
### [Punct de operare: Principal.AI_AGENT, page_size=5, prag ciclu de viață ACTIV]

Plafonul oracol reprezintă rata de succes maximă teoretică ce ar putea fi atinsă de o reordonare perfectă a primilor $k$ candidați generați:

| Fereastră $k$ Candidați | Cazuri Atinse | Recall Maxim Teoretic (Oracol) | Interval Wilson 95% | Interpretare Arhitecturală |
|:---:|:---:|:---:|:---:|:---|
| $k = 5$ | **43 / 130** | **33.08%** | [25.58%, 41.55%] | Nivelul obținut dacă top 5 candidați fuzionați ar fi păstrați fără pierderi la scorare. |
| $k = 10$ | **53 / 130** | **40.77%** | [32.70%, 49.36%] | Plafonul atins prin extinderea ferestrei reranker-ului la top 10 candidați. |
| $k = 20$ | **61 / 130** | **46.92%** | [38.56%, 55.47%] | Plafonul atins prin examinarea a 20 de candidați cu un reranker neural. |
| $k = 50$ | **85 / 130** | **65.38%** | [56.87%, 73.01%] | Peste 65% din întrebări au nota de aur în primii 50 de candidați. |
| $k = 100$ | **92 / 130** | **70.77%** | [62.44%, 77.90%] | Peste 70% din întrebări pot fi rezolvate fără modificarea generării de candidați. |
| $k = 200$ | **99 / 130** | **76.15%** | [68.14%, 82.66%] | Plafonul absolut al pool-ului actual de candidați BM25/fuziune. |

> [!TIP]
> **Plafonul Oracol la k=200 este 76.15%** (99/130). Aceasta demonstrează că generatorul existent identifică nota corectă în peste 3 sferturi din cazuri. Niciun reranker pe acest pool nu poate depăși 76.15%, dar spațiul de creștere de la 16.15% la 76.15% este uriaș (+60 pp).

---

## 5. Tabelul 4 — De Ce N-a Fost Candidat (Anatomia NEVER_CANDIDATE)
### [Punct de operare: Principal.AI_AGENT, page_size=5, prag ciclu de viață ACTIV]

Pentru cele 13 cazuri în care nota de aur nu a acces niciodată în lista de candidați, cauza a fost separată între lipsa totală de vocabular comun și scor lexical insuficient:

| Sub-Cauză | Număr Cazuri | Proporție din NEVER_CANDIDATE | Soluție Arhitecturală Necesară |
|:---|:---:|:---:|:---|
| **Nepotrivire totală de vocabular** (`lexical_overlap == 0`) | **9** | 69.23% | Regăsire semantică densă (*Dense Vector Retrieval / Embeddings*) sau expandare de query. |
| **Prezentă dar sub prag** (`lexical_overlap > 0`, BM25 prea mic) | **4** | 30.77% | Calibrare a parametrilor BM25 ($k_1, b$), lematizare sau extindere a pool-ului inițial. |
| **Total NEVER_CANDIDATE** | **13** | 100.00% | 9 cazuri cer semantic search, 4 cazuri cer tuning lexical. |

---

## 6. Tabelul 5 — Defalcări Detaliate și Corecția Holm–Bonferroni
### [Punct de operare: Principal.AI_AGENT, page_size=5, prag ciclu de viață ACTIV]

### 6.1. Defalcare pe Clasa de Interogare
#### [Punct de operare: Principal.AI_AGENT, page_size=5, prag ciclu de viață ACTIV]

| Clasă | Total | Reușite | Recall (%) | Interval Wilson 95% | `NEVER_CANDIDATE` | `PAGINATION_CUT` | `FLOOR_EXCLUDED` |
|:---|:---:|:---:|:---:|:---:|:---:|:---:|:---:|
| `direct` | 60 | **7** | **11.67%** | [5.77%, 22.18%] | 11 | 32 | 9 |
| `multi_hop` | 40 | **8** | **20.00%** | [10.50%, 34.76%] | 1 | 23 | 6 |
| `conceptual` | 30 | **6** | **20.00%** | [9.51%, 37.31%] | 1 | 23 | 0 |

### 6.2. Defalcare pe Limbă și Analiza Diagnostică a Diacriticelor
#### [Punct de operare: Principal.AI_AGENT, page_size=5, prag ciclu de viață ACTIV]

| Limbă | Total Măsurabil | Total Benchmark | Reușite | Recall (%) | Interval Wilson 95% | `NEVER_CANDIDATE` | `PAGINATION_CUT` |
|:---|:---:|:---:|:---:|:---:|:---:|:---:|:---:|
| `en` | 69 | 86 | **12** | **17.39%** | [10.24%, 27.98%] | 9 | 41 |
| `ro` | 61 | 74 | **9** | **14.75%** | [7.96%, 25.72%] | 4 | 37 |

#### Diagnosticul Diacriticelor Românești: Mărime Corpus vs Normalizare Tokenizer
Investigația empirică directă asupra stocării și pipeline-ului lexical relevă:
1. **Corpusul nu este mai mic**: În indexul activ al depozitului există 969 de note, dintre care **718 note conțin diacritice românești (74.10%)**, iar doar 251 sunt exclusiv în limba engleză (25.90%). Corpusul românesc este de aproape 3 ori mai mare decât cel englezesc.
2. **Defect mecanic de tokenizare**: Expresia regulată a tokenizatorului lexical din `hybrid_retrieval.py` este:
   ```python
   TOKEN_RE = re.compile(r"[a-z0-9][a-z0-9_\-\.]*")
   ```
   Deoarece `TOKEN_RE` filtrează strict caracterele ASCII `[a-z0-9]`, caracterele românești (`ă`, `â`, `î`, `ș`, `ț`) funcționează ca delimitatori distructivi. Spre exemplu:
   `tokenize('învățare')` produce `['nv', 'are']`.
   Dacă o notă din depozit este indexată fără diacritice (`invatare`) sau cu altă codare Unicode (sedilă `ş/ţ` vs virgulă `ș/ț`), suprapunerea de tokeni devine nulă. Deși diferența de recall (17.39% EN vs 14.75% RO) nu a atins pragul de semnificație statistică ($p_{\text{adj}} = 1.0$), mecanismul de eșec este demonstrat experimental.

### 6.3. Defalcare pe Ciclul de Viață al Notei Țintă
#### [Punct de operare: Principal.AI_AGENT, page_size=5, prag ciclu de viață ACTIV]

| Stare Ciclu de Viață | Total Note | Reușite | Recall (%) | Interval Wilson 95% | `PAGINATION_CUT` | `FLOOR_EXCLUDED` | `NEVER_CANDIDATE` |
|:---|:---:|:---:|:---:|:---:|:---:|:---:|:---:|
| `ACTIVE` | 17 | **5** | **29.41%** | [13.28%, 53.13%] | 9 | 0 | 1 |
| `NORMALIZED` | 3 | **0** | **0.00%** | [0.00%, 56.15%] | 0 | 3 | 0 |
| `REVIEW` | 88 | **16** | **18.18%** | [11.51%, 27.51%] | 69 | 0 | 3 |
| `UNKNOWN` | 21 | **0** | **0.00%** | [0.00%, 15.46%] | 0 | 12 | 9 |
| `raw` | 1 | **0** | **0.00%** | [0.00%, 79.35%] | 0 | 0 | 0 |

### 6.4. Defalcare pe Tipul Notei Țintă
#### [Punct de operare: Principal.AI_AGENT, page_size=5, prag ciclu de viață ACTIV]

| Tip Notă | Total Note | Reușite | Recall (%) | Interval Wilson 95% | `PAGINATION_CUT` | `FLOOR_EXCLUDED` | `NEVER_CANDIDATE` |
|:---|:---:|:---:|:---:|:---:|:---:|:---:|:---:|
| `UNKNOWN` | 9 | **0** | **0.00%** | [0.00%, 29.91%] | 0 | 0 | 9 |
| `core` | 12 | **0** | **0.00%** | [0.00%, 24.25%] | 0 | 12 | 0 |
| `knowledge` | 81 | **16** | **19.75%** | [12.54%, 29.70%] | 58 | 3 | 3 |
| `ontology_definition` | 1 | **0** | **0.00%** | [0.00%, 79.35%] | 0 | 0 | 0 |
| `procedure` | 16 | **4** | **25.00%** | [10.18%, 49.50%] | 11 | 0 | 1 |
| `project` | 10 | **1** | **10.00%** | [1.79%, 40.42%] | 8 | 0 | 0 |
| `standard` | 1 | **0** | **0.00%** | [0.00%, 79.35%] | 1 | 0 | 0 |

### 6.5. Corecția Holm–Bonferroni și Declarația de Non-Independență
#### [Punct de operare: Principal.AI_AGENT, page_size=5, prag ciclu de viață ACTIV]

| Test / Ipoteză | $p$ Brut | $p$ Ajustat Holm ($p_{\text{adj}}$) | Prag Semnificație | Concluzie Statistică |
|:---|:---:|:---:|:---:|:---|
| `H1_never_candidate_dominant` | `0.000000` | **`0.000000`** | $\alpha = 0.05$ | SEMNIFICATIV ($p < 0.05$) |
| `H2_multihop_never_candidate_60pct` | `0.000000` | **`0.000000`** | $\alpha = 0.05$ | SEMNIFICATIV ($p < 0.05$) |
| `H3_ro_recall_inferior_to_en` | `0.812392` | **`1.000000`** | $\alpha = 0.05$ | NESEMNIFICATIV |
| `H4_hits_in_top3_75pct` | `0.632580` | **`1.000000`** | $\alpha = 0.05$ | NESEMNIFICATIV |
| `Class_direct_vs_multihop` | `0.268274` | **`1.000000`** | $\alpha = 0.05$ | NESEMNIFICATIV |
| `Class_direct_vs_conceptual` | `0.345345` | **`1.000000`** | $\alpha = 0.05$ | NESEMNIFICATIV |
| `Class_multihop_vs_conceptual` | `1.000000` | **`1.000000`** | $\alpha = 0.05$ | NESEMNIFICATIV |

> [!WARNING]
> **Declarație de Limitare Metodologică (Non-independența H1 și H2)**:  
> Semnalăm formal limitarea preînregistrată: ipotezele H1 și H2 **nu sunt independente**. Clasa `multi_hop` (40 cazuri) reprezintă aproape o treime din întregul benchmark măsurabil (130 cazuri). Dacă H2 ar fi fost adevărată ($\ge 60\%$ NEVER_CANDIDATE pe multi_hop), H1 devenea probabilă din pură constrângere aritmetică. Procedura secvențială Holm tratează ipotezele ca pe o familie generică; în realitate, structura ierarhică le leagă intim.

---

## 7. Tabelul 6 — Costul Eșecului (Latența pe Etape: Reușite vs Ratări)
### [Punct de operare: Principal.AI_AGENT, page_size=5, prag ciclu de viață ACTIV]

| Etapă Pipeline | Reușite: Medie ± Std (ms) | Reușite: Mediană (ms) | Ratări: Medie ± Std (ms) | Ratări: Mediană (ms) | Impact Economic |
|:---|:---:|:---:|:---:|:---:|:---|
| `query_validation` | 0.012 ± 0.002 | 0.012 | 0.013 ± 0.004 | 0.012 | Etapă neglijabilă (< 0.1 ms) |
| `classification` | 0.050 ± 0.008 | 0.050 | 0.055 ± 0.048 | 0.049 | Etapă neglijabilă (< 0.1 ms) |
| `policy_and_retrieval` | 350.684 ± 91.651 | 378.106 | 354.331 ± 85.902 | 376.223 | **Domină 85% din execuție** (identică între reușite și ratări). |
| `scoring` | 62.304 ± 26.773 | 76.833 | 47.287 ± 29.174 | 35.985 | Scorarea durează cu ~15 ms mai mult la reușite din cauza densității semnalelor. |
| `pagination` | 0.180 ± 0.072 | 0.166 | 0.163 ± 0.043 | 0.166 | Etapă neglijabilă (< 0.1 ms) |
| `context_pack` | 0.695 ± 0.158 | 0.652 | 0.638 ± 0.100 | 0.612 | Asamblarea contextului final (< 1 ms). |

> [!NOTE]
> **Concluzia costului eșecului**: Etapa `policy_and_retrieval` domină masiv și cvasi-identic ambele categorii (~350.68 ms la reușite vs ~354.33 ms la ratări). **Eșecul nu este mai costisitor computațional decât succesul; este pur și simplu inutil.**

---

## 8. Tabelul 7 — Brațul de Referință Comparativ
### [Punct de operare comparat: Principal.HUMAN (page_size=10, Floor INACTIV) vs Principal.HUMAN (page_size=5, Floor INACTIV) vs Principal.AI_AGENT (page_size=5, Floor ACTIV)]

| Metrică | Brațul de Referință HUMAN (p=10) | Brațul de Control HUMAN (p=5) | Brațul de Producție AI_AGENT (p=5) |
|:---|:---:|:---:|:---:|
| **Reușite (Hits)** | **38 / 130** | **21 / 130** | **21 / 130** |
| **Context Recall (%)** | **29.23%** | **16.15%** | **16.15%** |
| **Interval Wilson 95%** | [22.10%, 37.56%] | [10.82%, 23.44%] | [10.82%, 23.44%] |
| `PAGINATION_CUT` | 60 (65.2%) | 77 (70.6%) | 78 (71.6%) |
| `CANDIDATE_LIMIT_CUT` | 17 (18.5%) | 17 (15.6%) | 2 (1.8%) |
| `FLOOR_EXCLUDED` | 0 (0.0%) | 0 (0.0%) | 15 (13.8%) |
| `NEVER_CANDIDATE` | 14 (15.2%) | 14 (12.8%) | 13 (11.9%) |
| `RAW_EXCLUDED` | 1 (1.1%) | 1 (0.9%) | 1 (0.9%) |
| `UNDETERMINED` | 0 (0.0%) | 0 (0.0%) | 0 (0.0%) |
| **Rang Median pe Ratări** | **33.0** | **30** | **20.5** |

---

## 9. Tabelul 8 — Evaluarea Formală a Ipotezelor Preînregistrate H1–H4
### [Punct de operare: Principal.AI_AGENT, page_size=5, prag ciclu de viață ACTIV (comparat cu HUMAN reference)]

| Ipoteză | Enunț Preînregistrat | Măsurătoare Empirică | $p$ Brut | $p$ Ajustat Holm | Verdict |
|:---:|:---|:---:|:---:|:---:|:---:|
| **H1** | Categoria dominantă a ratărilor este `NEVER_CANDIDATE` ($\ge 40\%$). | **13/109 (11.93%)**, CI [7.10%, 19.34%] | `0.000000` | `0.000000` | **INFIRMATĂ** |
| **H2** | Clasa `multi_hop` este dominată de `NEVER_CANDIDATE` ($\ge 60\%$). | **1/32 (3.12%)**, CI [0.55%, 15.74%] | `0.000000` | `0.000000` | **INFIRMATĂ** |
| **H3** | Interogările în limba română au recall inferior celor în engleză ($p < 0.05$). | EN: 12/69 (17.39%) vs RO: 9/61 (14.75%) (dif: +2.64 pp) | `0.812392` | `1.000000` | **INFIRMATĂ** |
| **H4** | Reușitele sunt puternic concentrate la vârful clasamentului (Top 3 $\ge 75\%$). | **16/21 (76.19%)**, CI [54.91%, 89.37%] | `0.632580` | `1.000000` | **CONFIRMATĂ** |

### Analiza Verdictelor:
- **H1 este infirmată categoric**: `NEVER_CANDIDATE` reprezintă doar 11.93% din eșecuri (13/109). Categoria masiv dominantă este `PAGINATION_CUT` (71.56%).
- **H2 este infirmată categoric**: Clasa `multi_hop` nu este dominată de lipsa candidaților, ci de clasare/paginare (75.0% `PAGINATION_CUT`, doar 3.12% `NEVER_CANDIDATE`). Sistemul lexical actual găsește nota corectă în top candidați, dar o plasează sub pragul paginii.
- **H3 este infirmată statistic**: Diferența de recall (17.39% EN vs 14.75% RO) este nesemnificativă ($p_{\text{adj}} = 1.0$), deși defectul de tokenizare pe diacritice este demonstrat mecanic.
- **H4 este confirmată**: 16 din cele 21 de reușite (76.19%) se situează pe primele 3 poziții, confirmând că atunci când sistemul reușește, succesul este categoric și stabil la vârful clasamentului.

---

## 10. Tabelul 9 — Aplicarea Pragurilor Decizionale Preînregistrate
### [Punct de operare: Principal.AI_AGENT, page_size=5, prag ciclu de viață ACTIV]

| Criteriu Decizional Preînregistrat | Condiție Formală | Valoare Măsurată Empiric | Verdict Decizional |
|:---|:---|:---:|:---:|
| **1. Adoptare Reranker (Problema de clasare)** | $\ge 40\%$ (`PAGINATION_CUT` + `CANDIDATE_LIMIT_CUT`) **ȘI** rang median ratări $\le 30$ | **73.39%** $\ge 40\%$ **ȘI** rang median = **20.5** $\le 30$ | **ADOPTAT (Problema este de clasare / reordonare)** |
| **2. Adoptare Dense Retrieval (Problema generării)** | $\ge 40\%$ `NEVER_CANDIDATE` | **11.93%** $< 40\%$ | **RESPINS ca blocaj primar (NEVER_CANDIDATE este sub 40%)** |
| **3. Inconcludență** | Ambele categorii sub 40% | Categoria de clasare întrunește 73.39% | **INFIRMAT (Decizie clară)** |

> [!IMPORTANT]
> **Verdict Final**: Datele empirice susțin **fără echivoc problema de clasare/reordonare**. Următorul pas tehnic obligatoriu în arhitectura de regăsire este implementarea și măsurarea unui model de re-clasare (**Cross-Encoder Reranker**) capabil să promoveze candidații din intervalul 6–30 în top 5.

---

## 11. Ce NU Se Poate Concluziona din Aceste Date

Pentru rigoare epistemologică și protecția integrității deciziilor viitoare, consemnăm explicit limitele interpretative ale acestor măsurători:

1. **Nu se poate concluziona că un reranker va atinge în practică plafonul de 76.15%**: Plafonul oracol presupune un judecător omniscient. Modelele reale de reranking (cum ar fi BGE-Reranker sau MiniLM) au propriile rate de eroare și deplasare negativă a candidaților corecți.
2. **Nu se poate concluziona că Dense Retrieval este lipsit de valoare**: Deși nu este blocajul majoritar în prezent, cele 9 cazuri de nepotrivire totală de vocabular (`lexical_overlap == 0`) nu pot fi rezolvate de niciun reranker pe candidați BM25. Dense Retrieval va rămâne necesar ca a doua etapă de optimizare odată ce problema de clasare este rezolvată.
3. **Nu se poate extrapola comportamentul la un corpus deschis / neindexat**: Măsurătorile reflectă exact compoziția actuală a celor 969 de note din depozit. Modificări majore în ontologie sau adăugarea de sute de note noi pot schimba dinamica densității lexicale.
4. **Nu se poate concluziona că limba română este mai dificilă pentru modelele de limbaj**: Deficitul observat este strict un artefact mecanic de tokenizare regex în codul Python (`TOKEN_RE`), nu o incapacitate cognitivă a algoritmilor.

---
*Raport generat determinist din `07_EVALUATION/loss_funnel/loss_funnel_cases.json` conform contractului din preînregistrare.*
