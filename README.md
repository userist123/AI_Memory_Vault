# AI Memory Vault

Un substrat de memorie externă persistentă pentru agenți AI: memorie cu proveniență, competențe, proceduri, regăsire, primitive cognitive de execuție, învățare controlată, dovezi și execuție multi-agent reluabilă.

<p align="center">
  <strong>Română</strong> · <a href="README.en.md">English</a>
</p>

<p align="center">
  <strong>UN SINGUR VAULT · UN SINGUR CANON · COGNIȚIE SELECTIVĂ · EVOLUȚIE VERIFICATĂ</strong>
</p>

<p align="center">
  <a href="https://github.com/userist123/AI_Memory_Vault_CODEX_READY/actions"><img alt="GitHub Actions" src="https://img.shields.io/badge/CI-GitHub%20Actions-181717?logo=githubactions&logoColor=white"></a>
  <a href="https://github.com/userist123/AI_Memory_Vault_CODEX_READY/tree/main/.claude-plugin"><img alt="Claude Code" src="https://img.shields.io/badge/Claude%20Code-Plugin-7C3AED"></a>
  <a href="https://github.com/userist123/AI_Memory_Vault_CODEX_READY/tree/main/07_EVALUATION"><img alt="Evidence Gated" src="https://img.shields.io/badge/Dovezi-Obligatorii-0F766E"></a>
  <a href="https://github.com/userist123/AI_Memory_Vault_CODEX_READY/tree/main/00_GOVERNANCE/coordination"><img alt="Persistent Agent Memory" src="https://img.shields.io/badge/Memorie%20Agent-Persistent%C4%83-2563EB"></a>
  <a href="https://obsidian.md/"><img alt="Obsidian" src="https://img.shields.io/badge/Obsidian-Sincronizat-7C3AED"></a>
</p>

> **Problema:** un sistem RAG standard poate regăsi text. Proiectul acesta încearcă să facă memoria externă *utilă operațional* — delimitată, atribuibilă, conștientă de ciclul de viață, conștientă de incertitudine și măsurabilă exact în punctul în care un agent raționează, planifică, verifică și acționează.

---

## ✦ Pe scurt

| Strat | Ce există aici | Starea reală |
|---|---|---|
| Vault canonic | memorie în Markdown, cunoștințe, competențe, agenți, proceduri, proveniență | **IMPLEMENTAT** |
| Memory V6 | extragere, propuneri, detectare de conflicte, ciclu de viață, consolidare, întreținerea regăsirii | **IMPLEMENTAT / ACTIV** |
| Nucleul cognitiv | recall, activare, memorie de lucru, spațiu global de lucru, grafuri, activare prin difuzie, primitive de planificare | **IMPLEMENTAT / PARȚIAL** |
| Memory Controller | granița de stocare, politica de citire/scriere, pachete de context, dezvăluire progresivă, control pe ciclu de viață | **IMPLEMENTAT** |
| Execuția modelelor | abstracții de furnizor fake, local/Ollama, OpenAI, rutare pe niveluri, telemetrie de consum | **IMPLEMENTAT** |
| Ingestia de competențe externe | descoperire, proveniență, clasificare, validare, promovare controlată | **IMPLEMENTAT** |
| Conversie carte → text | segmentare după cuprins / tipografie / pagini, detectare OCR, metrici per carte | **IMPLEMENTAT** |
| Extragerea conceptelor din cărți | asistată de model, cu porți de ancorare, parafrază, formă și schemă | **IMPLEMENTAT / PORȚI DEMONSTRATE** |
| *Selectivitatea* conceptelor | a distinge un concept portant de mobilierul experimental | **MĂSURAT, NEREZOLVAT** |
| Poarta de promovare în ontologie | un merge trebuie să consulte un manifest de verdicte; rândurile nejudecate sunt reținute și numărate | **IMPLEMENTAT** |
| Dispoziția rândurilor | fiecare dintre cele 213 rânduri de slot are o decizie și un temei | **DECIS, NEEXECUTAT** |
| Predicție Polymarket | instantanee, consiliu, calibrare, avantaj față de piață, abstenție, dimensionare Kelly, backtest, ablație, contract temporal | **IMPLEMENTAT / NEVALIDAT PE DATE REALE** |
| Proveniența rezoluțiilor Polymarket | momentul în care un rezultat a devenit cognoscibil | **STABILIT CA INDISPONIBIL** |
| Memoria persistentă a agenților | stare reluabilă `CURRENT.md` în `00_GOVERNANCE/coordination/agents/` | **IMPLEMENTAT** |
| Planning Influence | MVE determinist izolat, cu patru brațe și priori slabi | **EXPERIMENTAL** |
| Politica de incertitudine | contract de aplicabilitate + forța dovezii + contradicție + cost de verificare | **PROIECTAT / PREÎNREGISTRAT** |
| Influență cognitivă susținută de model | MVE cauzal pereche pe execuție reală de model | **ÎNCĂ NEDEMONSTRAT** |
| Învățare continuă complet închisă | bucla rezultat → dovadă → învățare → mutație canonică | **PARȚIAL / DESCHIS** |

<details>
<summary><strong>Prin ce diferă asta de „pur și simplu RAG"?</strong></summary>

```text
Mentalitatea RAG
interogare → documente → prompt

Ținta Vault
experiență → dovadă → tipar → aplicabilitate → influență → decizie → rezultat → reorganizare
```

Ținta pe termen lung este **regăsire ≠ influență**. Depozitul separă explicit un substrat epistemic pasiv de interfețele active de execuție. Implementarea actuală nu pretinde că starea ascunsă a modelului, decodarea, planificarea sau execuția de unelte ar fi controlate magic de un fișier text.
</details>

---

## 🧠 Bucla cognitivă

```mermaid
flowchart LR
    T[Sarcină / Lume] --> O[Observație]
    O --> E[Experiență]
    E --> EV[Dovadă]
    EV --> P[Tipar / Tranziție]
    P --> A[Aplicabilitate]
    A --> F[Recall forjat]
    F --> PL[Plan / Căutare]
    PL --> X[Acțiune]
    X --> V[Verificare]
    V --> R[Rezultat]
    R --> RE[Reorganizare]
    RE --> EV
    A -. incertitudine .-> G[Poartă epistemică]
    G --> V
```

Arhitectura este împărțită intenționat în cinci straturi semantice:

**Experiență** — ce s-a întâmplat.  
**Model / Tipar** — ce s-ar putea generaliza.  
**Aplicabilitate** — unde ar trebui să se transfere acea memorie.  
**Influență** — cum îi este permis să afecteze computația.  
**Reorganizare** — cum modifică rezultatele verificate memoria viitoare.

Dovada, proveniența, valabilitatea temporală, incertitudinea, siguranța și economia de tokeni traversează toate cele cinci straturi.

---

## ⚙️ Arhitectura sistemului

```mermaid
flowchart TB
    U[Om / Sarcină] --> R[Execuția agentului]
    R --> S[Rutare competențe + proceduri]
    R --> M[Memory Controller]
    M --> C[Compilator de context]
    C --> Q[Regăsire selectivă]
    Q --> W[Context de lucru]
    W --> R

    M --> CC[Nucleu cognitiv]
    CC --> WM[Memorie de lucru]
    CC --> RC[Recall asociativ]
    CC --> MG[MultiGraph]
    MG --> SA[Activare prin difuzie]
    CC --> PL[Primitive de planificare]
    CC --> CO[Consolidare]

    R --> EX[Execuție / Unelte]
    EX --> VF[Verificare]
    VF --> OUT[Rezultat]
    OUT --> EVI[Dovadă]
    EVI --> CO

    RAW[Surse externe] --> ING[Ingestie + Proveniență]
    ING --> INBOX[06_INBOX / Recenzie]
    INBOX --> HG[Poartă umană / de politică]
    HG --> M

    P[00_GOVERNANCE / Memorie persistentă a agenților] <--> R
```

### Granița fundamentală

```text
            SUBSTRAT EPISTEMIC PASIV
┌──────────────────────────────────────────────────┐
│ experiență • dovadă • memorie • competențe       │
│ proveniență • ciclu de viață • stare temporală   │
└────────────────────────┬─────────────────────────┘
                         │
                         ▼
          INTERFEȚE ACTIVE DE EXECUȚIE
┌──────────────────────────────────────────────────┐
│ compilator de reprezentare / cadru               │
│ harnașament de planificare / căutare             │
│ poartă epistemică / rutare de verificare         │
│ portal determinist de execuție                   │
└──────────────────────────────────────────────────┘
```

Aceste interfețe de execuție sunt arhitectura-țintă. Unele există ca primitive izolate sau experimente; nu toate sunt încă integrate complet în calea de producție a agentului.

---

# 🗂️ Harta depozitului

| Cale | Rol |
|---|---|
| `00_GOVERNANCE/` | reguli, protocoale, invarianți, coordonarea agenților, `VAULT_STATE.md` |
| `01_ARCHITECTURE/` | note de cunoștințe, registre, matrice de competențe, fișierele de slot ale ontologiei |
| `01_KNOWLEDGE/` | cercetare externă și cunoștințe importate |
| `02_PRODUCT/` | contextul și continuitatea proiectelor |
| `03_IMPLEMENTATION/` | `03_IMPLEMENTATION/packages/` — nucleu cognitiv, memory controller, regăsire, polymarket, furnizori |
| `04_CONFIG/` · `05_DATA/` | configurație și artefacte de date |
| `04_MEMORY/` | înregistrări canonice de memorie |
| `06_INBOX/` | material brut și importat, înainte de recenzie |
| `07_EVALUATION/` | audituri, experimente, benchmark-uri, MVE, dovezi forensice, capturi de date |
| `08_OBSERVABILITY/` · `09_SECURITY/` | telemetrie și suprafețe de securitate |
| `10_DOCUMENTATION/` | proceduri, resurse, proiecția Obsidian |
| `20_TESTS/` | suita deterministă — `pytest -q` o rulează din rădăcina depozitului |
| `30_SCRIPTS/` | unelte de ingestie, cunoștințe, memorie, competențe și verificare |
| `40_EXPERIMENTS/` · `50_ARTIFACTS/` | lucru experimental și artefacte generate |
| `60_DEPLOYMENT/` · `70_INTEGRATIONS/` | suprafețe de implementare și integrare |
| `80_ARCHIVE/` · `90_RELEASE/` · `99_META/` | istoric, versiuni, meta |
| `.agents/` | profiluri de agenți, reguli, competențe operaționale |
| `.claude-plugin/` | suprafața de plugin pentru Claude Code |
| `cognitive_core/` | primitive cognitive de execuție |
| `staging/` | rânduri de extragere per carte, în `.gitignore` — niciodată comise |
| `AI_Memory_Vault_OBSIDIAN/` | stratul de navigare pentru oameni, care păstrează încă vechiul layout `00_CORE` / `99_SYSTEM` |
| `.github/workflows/` | CI, securitate, ingestie, întreținere, evaluare |

> Directoarele numerotate de mai sus sunt depozitul. Layout-ul de vault pe care
> îl folosește proiecția Obsidian — `00_CORE/`, `09_COORDINATION/`, `99_SYSTEM/` —
> se află sub `AI_Memory_Vault_OBSIDIAN/` și nu este rădăcina depozitului. Acest
> README l-a descris pe al doilea ca și cum ar fi fost primul, până la 2026-09-14.

---

# 🧩 Nucleul cognitiv

Module importante de execuție:

- `recall.py` — recall cu semnale multiple: semantic, activare, temporal, memorie de lucru, autoritate și ciclu de viață.
- `ranked_search.py` — strat de reordonare conștient de graf.
- `multi_graph.py` — vederi de graf semantice, temporale, cauzale și orientate pe entități.
- `spreading_activation.py` — activare asociativă peste structura grafului.
- `working_memory.py` — starea contextului activ.
- `global_workspace.py` — primitivă de spațiu global de lucru, competitivă, cu difuzare.
- `activation.py` — comportamentul de activare și decădere.
- `consolidation.py` / `sleep_consolidation.py` — întreținere și reconsolidare.
- `planning.py` / `plan_complexity_analyzer.py` — primitive de planificare și rutare de resurse.
- `learning.py`, `reflection.py`, `reasoning.py`, `motivation.py` — componente cognitive de nivel superior.
- `semantic.py` — abstracție de furnizor semantic.

### Verificarea realității

Calea implicită de regăsire nu este, în acest moment, un sistem complet semantic, nativ vectorial. Comportamentul semantic determinist și scorul de relevanță conțin încă mecanisme lexicale, de suprapunere de tokeni; există căi opționale semantice/Qdrant/Ollama, dar ele nu echivalează cu un index semantic de producție cablat universal. Distincția este păstrată intenționat.

---

# 🗄️ Memory Controller

`03_IMPLEMENTATION/packages/memory_controller/` este granița de încredere și de context din jurul memoriei canonice.

Numele este astăzi un shim de compatibilitate: pachetul conține doar un `__init__.py` care redirecționează vechiul spațiu de import către pachete-frate descompuse pe responsabilități. Implementarea trăiește sub `retrieval/`, `memory/`, `security/`, `lifecycle/`, `interfaces/` și `observability/`.

Răspunde de lucruri precum:

```text
igienizarea interogării
      ↓
clasificare
      ↓
acces conștient de ciclul de viață
      ↓
regăsirea candidaților
      ↓
scorarea relevanței
      ↓
dezvăluire progresivă
      ↓
pachet de context delimitat
      ↓
proveniență / audit
```

Suprafețe principale:

- `memory/controller.py`
- `security/authority.py`
- `retrieval/context/retrieval.py`
- `retrieval/context/relevance_scoring.py`
- `retrieval/context/pack_builder.py`
- `retrieval/context/progressive_disclosure.py`
- motoare de stocare pe SQLite și pe fișiere

(căi relative la `03_IMPLEMENTATION/packages/`)

Citirile publice rămân controlate pe ciclu de viață. Inspecția cognitivă poate trata explicit material aflat în REVIEW fără a-l promova în tăcere la adevăr canonic.

---

# 🧱 Memory V6

Memory V6 adaugă peste Vault-ul de bază un strat operațional de întreținere a memoriei:

| Capacitate | Scop |
|---|---|
| Buffer de senzori | material tranzitoriu de sesiune/eveniment |
| Extragere atomică | fapte, decizii, proceduri, lecții |
| Adaptor Ollama | extragere opțională cu model local |
| Coadă de propuneri | candidați de memorie aflați în recenzie |
| Detectare de conflicte | contradicții și afirmații concurente |
| Promovare controlată | canonicalizare cu poartă umană/de politică |
| MultiGraph | vederi derivate de relații |
| Activare prin difuzie | activare asociativă / clasificare |
| Consolidare în somn | procesare orientată pe întreținere |
| Benchmark-uri de regăsire | unelte Precision@K / Recall@K / MRR |
| Bugete de context | transport delimitat în tokeni/octeți |
| Telemetrie de consum | consum de model estimat față de real |
| Raportare de eficiență | economia execuției, în stil B4/B5 |

Obiectivul arhitectural este **dezvăluirea progresivă**: nu încărca tot Vault-ul doar pentru că există.

```text
metadate
   ↓
reguli relevante
   ↓
memorie compactă
   ↓
dovezi detaliate doar când e nevoie
```

---

# 🧠 Influența memoriei — noul strat de cercetare

Proiectul testează acum dacă memoria externă poate influența computația dincolo de a adăuga text într-un prompt.

### Patru canale de influență vizate

| Canal | Efect urmărit | Ținta măsurării |
|---|---|---|
| Recall / Reprezentare | schimbă cadrul explicit sau setul de ipoteze | reprezentare cu memorie ≠ fără memorie |
| Planificare | schimbă preferința de ramificare/căutare | traiectoria căutării / alocarea de noduri |
| Incertitudine | schimbă comportamentul de a acționa / verifica / explora / se abține | rutarea verificării și abstenția |
| Execuție | constrângeri deterministe de acțiune la granița uneltelor | acțiuni permise față de respinse |

Distincția importantă de siguranță este:

> **Influența memoriei trebuie să fie explicită și observabilă. Influența asupra stării ascunse nu este revendicată.**

### Model de memorie legat de dovezi

Unitatea persistentă vizată este un tipar de transfer legat de dovezi:

```text
Situație
Scop
Constrângeri
Acțiune
Tranziție de stare
Rezultat
Dovadă
Limite temporale
Aplicabilitate
Contraexemple
```

Un artefact compact de influență poate fi apoi forjat la cerere, în loc să fie trimis repetat prin contextul modelului întregul istoric.

---

# 🧪 Planning Influence MVE

MVE-ul izolat se află sub:

```text
07_EVALUATION/luna/
├── PLANNING_INFLUENCE_PREREGISTRATION_V2.md
├── PLANNING_INFLUENCE_PREREGISTRATION_V2_1.md
├── PLANNING_INFLUENCE_RESULTS_V3.md
├── PLANNING_INFLUENCE_UNCERTAINTY_POLICY_V1.md
├── planning_influence_mve_v3.py
├── simulate_puct_benchmark.py
├── run_experiments_v3.py
├── generate_planning_influence_report.py
├── run_all_v3.py
├── tables/ (table_luna_1..5.csv)
├── planning_influence_mve.py (pilot inițial retras)
└── test_planning_influence_mve.py (retras)
```

### Brațele experimentului

```text
Brațul 1 — bază / planificator uniform (izolat, fără scurgere de oracol)
Brațul 2 — memorie consultativă / planificator uniform
Brațul 3 — tratament cognitiv / prior de planificare derivat din memorie (v1_uncertainty, v2_verification_action)
Brațul 4 — control cu memorie învechită / contrazisă (CONFIRMED_CONTRADICTION)
```

### Rezultate V3 validate (`PLANNING_INFLUENCE_RESULTS_V3.md`)

Harnașamentul V3 rezolvă defectul structural de scurgere al pilotului inițial prin ordonare complet aleatoare și departajare PUCT ortogonală prin SHA-256 (`test_planning_influence_isolation.py` trece 100%, cu test strict `xfail` pe pilotul vechi).

Rezultatele experimentale pe grila preînregistrată ($N=200$ scenarii/celulă, 17.600 evaluări totale în `run_experiments_v3.py` reproduse octet cu octet prin `run_all_v3.py`):

1. **Izolarea bazei și reperul PUCT:** Căutarea neghidată consumă în medie $\approx 2.921$ noduri PUCT ($SE = 0.0032$, derivat Monte Carlo pe 200.000 de rulări în `simulate_puct_benchmark.py`), iar poziția ramurii optime are corelație nulă cu costul ($r^2 < 0.01$).
2. **Praguri de rentabilitate ($p^*$):** Memoria devine profitabilă economic la o acuratețe de cel puțin $p^* = 0.40$ în regim calibrat ($\Delta_{\text{nodes}} = +0.310$, 95% CI $[0.080, 0.565]$) și $p^* = 0.50$ în regim neinformativ ($\Delta_{\text{nodes}} = +0.565$, 95% CI $[0.300, 0.835]$) pentru politica `v1_uncertainty`.
3. **Prioritatea absolută a vetoului de contradicție:** Când memoria este marcată `CONFIRMED_CONTRADICTION`, priorii sunt forțați strict uniformi ($0.25$) pentru toate politicile, garantând $\Delta_{\text{fatals}} = 0.0000$ pe brațul stale și neutralizând complet riscul decizional.
4. **Economia verificării ca acțiune (`v2_verification_action`):** În spații restrânse ($K=4$), taxa de $1.0$ nod per verificare produce o economie netă negativă ($-0.235$ până la $-0.305$ noduri) față de `v1_uncertainty`; verificarea activă se justifică exclusiv în spații mari ($K \ge 6$) sau în regimuri de risc critic asimetric.

> **Limitare metodologică:** Aceasta este o **simulare deterministă cu oracol de scenariu**. Ea demonstrează matematic mecanica de fuziune a memoriei în planificare (izolarea oracolului, ponderarea Bayesiană a priorităților, vetoul absolut de contradicție), dar **nu constituie o dovadă că un agent real cu LLM planifică mai bine**.

### Pilotul determinist inițial — ⚠️ RETRAS (măsurătoare invalidă prin scurgere de oracol, PR #165)

> **Pilotul inițial este retras și păstrat exclusiv pentru trasabilitate istorică.** Comparația bază ↔ tratament din `planning_influence_mve.py` a fost viciată de o scurgere de oracol: linia 153 punea ramura optimă pe prima poziție (`optimal=order[0]`), iar selecția PUCT (linia 252) departaja egalitățile după poziția ramurii (`-branches.index(candidate)`). Baza alegea astfel optimul din pasul 1 în toate cele 30 de scenarii (cost artificial de 1.0 nod/scenariu). Cifrele raportate inițial (bază 30 noduri / tratament 54 noduri / 12 fatale) sunt invalide și au fost retrase formal prin PR #165.

### Politica de incertitudine

Politica preînregistrată separă:

```text
aplicabilitate
+ forța dovezii
+ starea de contradicție
+ costul verificării
+ influența asupra planificatorului
+ rezultatul execuției
```

Forțe fixe de aplicabilitate pentru următoarea rulare izolată:

```text
APPLICABLE                   = 1.00
APPLICABLE_WITH_VERIFICATION = 0.35
INSUFFICIENTLY_KNOWN         = 0.15
NOT_APPLICABLE               = 0.00
```

Politica este dovadă de proiectare. Reușita ei nu a fost încă stabilită.

---

# 🧬 Direcția învățării continue

Bucla de învățare vizată este conservatoare prin construcție:

```text
EXECUȚIE REALĂ
      ↓
REZULTAT
      ↓
DOVADĂ
      ↓
EVALUARE
      ↓
TIPAR / PROCEDURĂ / COMPETENȚĂ CANDIDAT
      ↓
SANDBOX / RECENZIE
      ↓
REGRESIE + SET REȚINUT
      ↓
POARTĂ UMANĂ / DE POLITICĂ
      ↓
MEMORIE CANONICĂ
```

Uneltele de rezultat și componentele de învățare există, dar depozitul **nu** revendică în acest moment o buclă de învățare continuă autonomă complet închisă, în care fiecare rezultat mutează automat memoria canonică.

Reținerea aceasta este deliberată: un rezultat care s-a întâmplat o dată este o dovadă despre un eveniment, nu automat o capacitate reutilizabilă.

---

# 🤖 Modelul de operare multi-agent

Starea de execuție persistentă se află sub:

```text
00_GOVERNANCE/coordination/
├── README.md
├── UNIVERSAL_AGENT_MEMORY_PROTOCOL_V1.md
├── BOOTSTRAP_ALL_AGENTS_V1.md
├── agents/
│   ├── CODEX/
│   ├── ANTIGRAVITY/
│   ├── PERPLEXITY/
│   └── LUNA/
└── projects/
    └── AI_MEMORY_VAULT/
        └── CURRENT.md
```

Fiecare sesiune substanțială este așteptată să lase în urmă:

```text
CE AM FĂCUT
UNDE
DOVADA
CE A EȘUAT / CE RĂMÂNE
URMĂTOAREA ACȚIUNE EXACTĂ
```

### Disciplina de execuție actuală

```text
DOAR PE MAIN
PREDARE SECVENȚIALĂ
FĂRĂ LUCRU ÎN PARALEL PE ACEEAȘI SARCINĂ
FĂRĂ DEZVOLTARE PE RAMURI DE FUNCȚIONALITATE PENTRU ACEST LANȚ DE CERCETARE
```

Acesta este mecanismul care face munca reluabilă între agenți, calculatoare, IDE-uri și sesiuni, fără a trata istoricul de conversație drept stare canonică.

---

# 📦 Competențe și cunoștințe externe

Competențele sunt tratate ca aptitudini reutilizabile, nu ca simple fragmente de prompt.

```text
sursă
  ↓
descoperire
  ↓
proveniență
  ↓
clasificare
  ↓
deduplicare / validare
  ↓
RAW_EXTERNAL
  ↓
recenzie umană / de politică
  ↓
competență operațională
```

Suprafețe relevante:

- `.agents/skills/`
- `.agents/agents/`
- `01_ARCHITECTURE/knowledge/Agents_Skill_Matrix.md`
- `01_ARCHITECTURE/knowledge/Master_Skills_Catalog_251.md`
- `30_SCRIPTS/skills/skill_ingestion.py`
- `06_INBOX/RAW_IMPORTS/`

Depozitul păstrează deliberat atribuirea sursei, metadatele de commit/cale, amprentele hash și starea de ciclu de viață pentru materialul importat.

---

# 🚪 Poarta de promovare în ontologie

Un script de merge a scris **201 concepte** în cele șaisprezece fișiere de slot ale ontologiei, într-un singur pas, toate cu statusul `proposed`. Exact asta fusese construit să facă. **160 erau încă acolo cinci zile mai târziu, judecate de nimeni** — iar când cineva le-a citit în sfârșit, 28 erau sintagme, nu concepte, 8 dublau concepte deja promovate, 1 era două concepte diferite sub același cuvânt, iar 6 cereau o decizie pe care n-o luase nimeni.

Fișierele de slot erau zonă de lucru pentru scriptul de merge și ontologie canonică pentru tot restul. Nimeni nu scrisese o poartă pentru că nimeni nu credea că există o graniță.

```text
rânduri din staging → manifest de verdicte → poartă → fișiere de slot
                              ↑
                 o decizie, cu un temei, pentru fiecare concept
```

`30_SCRIPTS/ingestion/promotion_verdicts.py` este acea graniță.

- **Doar `PROMOTE` admite.** `UNSURE` deliberat nu: o decizie pe care nimeni n-a putut s-o ia nu este o decizie de a continua.
- **Un concept absent din manifest este reținut și numărat**, niciodată aruncat în tăcere. O rulare care i-ar fi sărit fără urmă ar arăta identic cu una în care fuseseră judecați — exact mecanismul prin care cele 160 au devenit invizibile.
- **Un `reason` este obligatoriu pe fiecare intrare, inclusiv pe aprobări.** Un verdict fără motiv este un vot.
- **Poarta nu judecă calitatea.** Un manifest care marchează o sintagmă cu `PROMOTE` lasă sintagma să treacă și și-a făcut treaba. Ce elimină ea este rândul *neexaminat*.

`30_SCRIPTS/ingestion/slot_rows.py` este singurul cititor al acelor fișiere și **aruncă o eroare la un status pe care niciun tool nu îl declară**. Zece rânduri cu `unverified_source` au stat nevăzute cinci zile pentru că fiecare audit filtra pe cele două statusuri pe care le știa toată lumea, iar mai multe instrumente independente au dat 203 față de 213 reale — nu dintr-un bug comun, ci dintr-o presupunere comună.

Dispoziția tuturor celor 213 rânduri se află în
[`07_EVALUATION/book_corpus_conversion/`](07_EVALUATION/book_corpus_conversion/).
Executarea ei este o decizie umană și nu a fost luată.

---

# 📈 Predicție Polymarket

`03_IMPLEMENTATION/packages/polymarket/` — instantanee de piață, un consiliu de predicție, calibrare, avantajul modelului față de piață, abstenție de risc, dimensionare Kelly aplicată ultima și necondiționat, backtest, benchmark, ablație cu corecție Holm-Bonferroni și un contract de proveniență temporală.

### Ce a stabilit prima captură reală

Pachetul a fost construit de-a lungul a douăzeci și una de faze, împotriva a două fixture-uri scrise de noi. Primele date reale ([`fixtures/real_gamma_markets_*.json`](07_EVALUATION/polymarket/fixtures/)) au răspuns la întrebarea pe care se sprijinea proiectarea:

> **Niciun endpoint public Polymarket nu poartă o marcă temporală a decontării.**

Verificat pe toate cele 143 de chei dintr-o captură de 100 de piețe rezolvate. Singurele câmpuri apropiate de decontare sunt `resolvedBy`, o adresă, și `automaticallyResolved`, un boolean. `endDate` este momentul programat al închiderii, `closedTime` este momentul în care tranzacționarea s-a oprit și a început perioada de contestație UMA, iar `updatedAt` este momentul în care un worker a scris un rând în baza de date. Niciunul nu este momentul în care rezultatul a devenit cognoscibil.

Prin urmare, `SOURCE_ONCHAIN_SETTLEMENT` este inaccesibil fără un nod Polygon RPC, fiecare backtest scorează pe `SOURCE_MANUAL_ATTESTED`, iar `ResolutionSet.by_source()` există tocmai ca să raporteze asta. `resolutions.py` **nu** are, deliberat, o constantă `SOURCE_DERIVED_CLOSE_TIME`, iar un test cere ca numele să rămână absent: folosirea momentului de închidere a unei piețe drept moment al rezoluției este exact greșeala pe care modulul o previne.

**Niciun rezultat de predicție de aici nu a fost validat împotriva unor rezultate reale de piață.** Capturile sunt ștanțate cu proveniență — URL, marcă temporală UTC, cod HTTP, număr de înregistrări și SHA-256 — și nimic nu este curățat sau reordonat.

---

# 🔐 Securitate și siguranță epistemică

Sistemul tratează informația externă ca neîncrezută până când trece granițe explicite.

Principii fundamentale:

- un AI nu își poate promova propria afirmație la verificare autoritară doar scriind metadata `verified`.
- afirmațiile de proveniență privilegiată sunt controlate.
- conținutul din REVIEW poate fi inspectat fără a deveni automat memorie ACTIVĂ.
- tranzițiile de ciclu de viață ale propunerilor sunt controlate.
- traseele de audit sunt păstrate.
- proveniența supraviețuiește ingestiei.
- memoria contradictorie nu trebuie să câștige mai multă influență doar pentru că este contradictorie.
- controalele de benchmark nu trebuie să depindă în tăcere de cunoașterea oracolului.

Depozitul conține și material de securitate și forensic despre granițele de încredere ale memoriei, igiena corpusurilor externe, constatările Defender și liniile de bază de curățare la nivel de depozit.

---

# 🛡️ CI / automatizare

Suprafețele actuale de workflow includ:

```text
.github/workflows/
├── memory-v6-tests.yml
├── planning-influence-mve.yml
├── memory-consolidation.yml
├── regenerate-skill-catalog.yml
├── import-external-skills.yml
├── process-raw-books.yml
├── codeql.yml
├── fortify.yml
├── apisec-scan.yml
└── jarvis-command-center.yml
```

Proiectul distinge **verificarea în CI** de execuția locală. Un workflow în așteptare nu este o trecere. O rulare locală nu este promovată în tăcere la dovadă din CI.

---

# 🧪 Modelul de verificare

Depozitul folosește niveluri de dovadă pentru a preveni inflația de capacități:

| Nivel | Semnificație |
|---|---|
| `DOCUMENT_VERIFIED` | susținut de documentație canonică |
| `CODE_VERIFIED` | confirmat din implementarea din depozit |
| `TEST_VERIFIED` | observat într-o ieșire reală de test automat |
| `RUNTIME_VERIFIED` | observat într-o execuție reală |
| `CI_VERIFIED` | observat în dovezi din GitHub Actions |
| `CLAIMED_ONLY` | afirmat, dar insuficient susținut cu dovezi |
| `UNVERIFIED` | doar proiectare/speculație |

**Sursa de adevăr:** `main` + sursa comisă + ieșire reală de test/execuție + dovezi din CI.

Rapoartele, capturile de ecran, textul din README și rezumatele agenților nu au întâietate față de dovada executabilă din depozit.

---

# 🚧 Lacune cunoscute — vizibile intenționat

Secțiunea aceasta nu este o slăbiciune a README-ului. Face parte din contractul proiectului.

1. Regăsirea implicită se sprijină încă substanțial pe comportament determinist lexical, de suprapunere de tokeni; generarea semantică de candidați nu este cablată universal în calea implicită `MemoryController.search()`.
2. Clasificarea conștientă de graf există, dar integrarea în producție a avut istoric căi de eșec care au cerut reparații și diagnostice explicite; comportamentul grafului nu este tratat ca automat autoritar.
3. Telemetria de rezultat nu constituie încă o buclă de învățare autonomă complet închisă.
4. Planning Influence este un harnașament experimental izolat; nu este încă o integrare de planificator în producție.
5. Cel mai recent pilot de tratament arată în continuare eficiență negativă față de controlul consultativ pereche.
6. Execuția în CI observată în lanțul curent de lucru poate rămâne în așteptare; în așteptare înseamnă **neverificat**.
7. Unele artefacte de cercetare sunt ținte de proiectare, nu garanții de implementare.
8. Ingestia de cărți extrage și filtrează corect, dar nu poate clasifica. Încrederea modelului este `1.00` pe fiecare candidat, inclusiv pe cei a căror dovadă a fost fabricată; `claim_type` este constant; `occurrences` este 1 pentru 47 din 48 de concepte dintr-o lucrare întreagă; o listă de excludere la nivel de prompt este ignorată de model. Aproximativ jumătate din ieșire este mobilier experimental — `validation set`, `SGD optimizer`, `Rot-MNIST`. Măsurat, nu estimat: [`07_EVALUATION/book_corpus_conversion/FINDINGS.md`](07_EVALUATION/book_corpus_conversion/FINDINGS.md).
9. Două cărți din corpus sunt scanări fără strat de text și nu pot fi ingerate fără OCR, pe care acest depozit nu îl are.

Expunerea acestor lacune este intenționată. Proiectul este întărit prin falsificare, nu prin lustruirea afirmațiilor lui.

Una dintre ele a fost cât pe ce să fie lustruită și merită spusă pe șleau. O modificare de prompt a părut să taie zgomotul de extragere cu 85%; patru din cele cinci concepte rămase erau exact cele patru pe care promptul însuși le dădea ca exemple bune, returnate indiferent de ce spunea pasajul. Cifra arăta ca o reparație și era modelul repetându-și instrucțiunile. Versiunea păstrată în depozit este cea cu cifra mai proastă.

---

# 🧭 Foaie de parcurs

```text
ACUM
 │
 ├─ verifică în CI cel mai recent MVE conștient de aplicabilitate
 ├─ implementează ruta explicită de verificare în harnașamentul izolat
 ├─ rulează politica de incertitudine înghețată, fără ajustări ulterioare
 │
 ▼
APOI
 │
 ├─ acceptă / falsifică / reproiectează politica deterministă de influență
 ├─ adaugă pereche susținută de model, cu set reținut și memorie învechită/adversă
 │
 ▼
MAI TÂRZIU
 │
 ├─ măsurarea influenței asupra reprezentării
 ├─ poartă epistemică de acțiune/verificare/abstenție
 ├─ experimente de portal determinist de execuție
 ├─ compilarea tiparelor legate de dovezi
 └─ buclă de învățare închisă, protejată de regresie
```

Un MVE susținut de model **nu este autorizat doar pentru că trec testele unitare deterministe**.

---

# ⚡ Pornire rapidă

### Rulează testele deterministe

```bash
pytest -q
```

### Rulează testele de izolare ale Planning Influence V3

```bash
pytest -q 20_TESTS/test_planning_influence_isolation.py
```

### Rulează suita completă de reproducere Planning Influence V3

```bash
python 07_EVALUATION/luna/run_all_v3.py
```

### Exemple de CLI pentru Memory V6

```bash
python -m cognitive_core.memory_v6_cli extract --text "Am decis: folosim SQLite WAL." --enqueue
python -m cognitive_core.memory_v6_cli review --show-conflicts
python -m cognitive_core.memory_v6_cli approve <candidate_id> --reviewer human
python -m cognitive_core.memory_v6_cli promote-approved --principal ai_agent
python -m cognitive_core.memory_v6_cli consolidate --render
```

Pentru dependențele exacte de execuție într-un checkout local, folosește fișierele de mediu / requirements ale depozitului.

---

# 🧭 Navigare canonică

### Arhitectură și contracte

- [`00_GOVERNANCE/VAULT_STATE.md`](00_GOVERNANCE/VAULT_STATE.md)
- [`07_EVALUATION/luna/COGNITIVE_MEMORY_TARGET_MODEL_V2.md`](07_EVALUATION/luna/COGNITIVE_MEMORY_TARGET_MODEL_V2.md)
- [`07_EVALUATION/luna/COGNITIVE_MEMORY_V2_REPOSITORY_REALITY_MAP_V1.md`](07_EVALUATION/luna/COGNITIVE_MEMORY_V2_REPOSITORY_REALITY_MAP_V1.md)
- [`00_GOVERNANCE/rules/Rules.md`](00_GOVERNANCE/rules/Rules.md)
- [`00_GOVERNANCE/protocols/Memory_Protocol.md`](00_GOVERNANCE/protocols/Memory_Protocol.md)

### MVE / cercetare

- [`07_EVALUATION/luna/PLANNING_INFLUENCE_MVE_V2_VALIDATED.md`](07_EVALUATION/luna/PLANNING_INFLUENCE_MVE_V2_VALIDATED.md)
- [`07_EVALUATION/luna/PLANNING_INFLUENCE_UNCERTAINTY_POLICY_V1.md`](07_EVALUATION/luna/PLANNING_INFLUENCE_UNCERTAINTY_POLICY_V1.md)
- [`07_EVALUATION/luna/PLANNING_INFLUENCE_APPLICABILITY_PILOT_LOCAL_20260904.md`](07_EVALUATION/luna/PLANNING_INFLUENCE_APPLICABILITY_PILOT_LOCAL_20260904.md)
- [`07_EVALUATION/luna/LUNA_INDEPENDENT_MEMORY_ENGINE_AUDIT_V2.md`](07_EVALUATION/luna/LUNA_INDEPENDENT_MEMORY_ENGINE_AUDIT_V2.md)
- [`07_EVALUATION/luna/PERPLEXITY_COGNITIVE_MEMORY_V2_ADVERSARIAL_VALIDATION.md`](07_EVALUATION/luna/PERPLEXITY_COGNITIVE_MEMORY_V2_ADVERSARIAL_VALIDATION.md)

### Continuitatea agenților

- [`00_GOVERNANCE/coordination/UNIVERSAL_AGENT_MEMORY_PROTOCOL_V1.md`](00_GOVERNANCE/coordination/UNIVERSAL_AGENT_MEMORY_PROTOCOL_V1.md)
- [`00_GOVERNANCE/coordination/BOOTSTRAP_ALL_AGENTS_V1.md`](00_GOVERNANCE/coordination/BOOTSTRAP_ALL_AGENTS_V1.md)
- [`00_GOVERNANCE/coordination/projects/AI_MEMORY_VAULT/CURRENT.md`](00_GOVERNANCE/coordination/projects/AI_MEMORY_VAULT/CURRENT.md)

### Competențe / ingestie

- [`01_ARCHITECTURE/knowledge/Agents_Skill_Matrix.md`](01_ARCHITECTURE/knowledge/Agents_Skill_Matrix.md)
- [`01_ARCHITECTURE/knowledge/Master_Skills_Catalog_251.md`](01_ARCHITECTURE/knowledge/Master_Skills_Catalog_251.md)
- [`.agents/skills/`](.agents/skills/)
- [`30_SCRIPTS/skills/skill_ingestion.py`](30_SCRIPTS/skills/skill_ingestion.py)

### Execuție

- [`cognitive_core/`](cognitive_core/)
- [`03_IMPLEMENTATION/packages/memory_controller/`](03_IMPLEMENTATION/packages/memory_controller/)
- [`cognitive_core/recall_cli.py`](cognitive_core/recall_cli.py)
- [GitHub Actions](../../actions)

---

## Principii de proiectare

```text
UN SINGUR CANON
DOVADA ÎNAINTEA ÎNCREDERII
REGĂSIRE ÎNAINTEA INFLAȚIEI DE CONTEXT
INFLUENȚĂ EXPLICITĂ ÎN LOC DE MAGIE
PROMOVARE CU POARTĂ UMANĂ
PROVENIENȚA SUPRAVIEȚUIEȘTE INGESTIEI
EȘECUL ESTE DATĂ
MĂSOARĂ ÎNAINTE SĂ AUTOMATIZEZI
REȚINE CE TREBUIE REȚINUT
```

> **Ambiția nu este de a construi cel mai mare depozit de memorie. Este de a construi un sistem de memorie care poate să își amintească selectiv, să expună de ce ar trebui să conteze o amintire, să știe când nu ar trebui să conteze, să influențeze computația în moduri măsurabile, să verifice ce s-a întâmplat și să se reorganizeze doar atunci când dovada câștigă dreptul de a schimba comportamentul viitor.**

<p align="center">
  <sub>AI Memory Vault · CODEX Ready · Cercetare și inginerie de memorie cognitivă</sub>
</p>
