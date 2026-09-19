# Pre-înregistrare: Evaluare Nucleu Cognitiv pe Date Reale (Benchmark Held-Out v2)

- **Data pre-înregistrării**: 2026-09-20  
- **Autor / Rol**: ANTIGRAVITY (Lead Systems & Cognitive Architect)  
- **Branch de lucru**: `antigravity/graph-and-core-real`  
- **Statut**: PRE-REGISTERED (Înainte de rularea măsurătorilor pe date reale)  
- **Set de date**: `07_EVALUATION/heldout_retrieval_benchmark_v2/heldout.json` (27 de cazuri înghețate, verificat SHA-256)  

---

## 1. Context și Motivație

Până în prezent, modulele nucleului cognitiv (`PlanComplexityAnalyzer`, `CouncilBudgetController`, `ContextPackBuilder`) au existat ca artefacte implementate parțial sau testate izolat pe scenarii sintetice, fără a fi integrate în calea principală de producție a motorului de căutare (`MemoryController.search()`).

Conform contractului din `AGENTS.md`:
> *"Better memory beats more memory. Better routing beats more agents. Capability is cheap; loaded context is expensive."*  
> *Routing obligatoriu: CLASSIFY -> ROUTE -> RETRIEVE -> ASSEMBLE MINIMAL CONTEXT*  
> *Plafoane de runtime: MAX_COUNCIL_AGENTS = 3, MAX_MEMORY_RESULTS = 5, MAX_SYNTHESIS_INPUT = 2500 tokens.*

Această evaluare măsoară efectul empiric al activării nucleului cognitiv (`enable_cognitive_core: bool = True`) față de linia de bază de producție (`enable_cognitive_core: bool = False`).

---

## 2. Brațe Experimentale sub Test

1. **Brațul A (Baseline — Producție Curentă)**:
   - `enable_cognitive_core = False`
   - Calea standard de căutare: fără planificare intermediară, fără clasificare de risc a fluxului, bugetare generică a context pack-ului (`ContextBudget` implicit al agentului).

2. **Brațul B (Treatment — Nucleu Cognitiv Activat)**:
   - `enable_cognitive_core = True`
   - Calea cognitivă completă:
     - `Planner.create_plan()` generează secvența de pași a sarcinii.
     - `PlanComplexityAnalyzer.analyze()` determină modul de execuție (`SIMPLE`, `MODERATE`, `COMPLEX`, `HIGH_RISK`) și complexitatea de council (1 sau 2).
     - `CouncilBudgetController.decide()` atribuie tier-ul de resurse (`NONE`, `LIGHT`, `STANDARD`, `HIGH_RISK`).
     - Modularea bugetului în `ContextPackBuilder`:
       - `NONE`: Interogare simplă, fără risc -> `max_notes = min(page_size, 3)`, `token_budget = 600`.
       - `LIGHT`: Complexitate moderată -> `max_notes = min(page_size, 5)`, `token_budget = 1200`.
       - `STANDARD` / `HIGH_RISK`: Sarcină critică sau complexă -> `max_notes = min(page_size, 5)`, `token_budget = 2500` (aliniat la `MAX_SYNTHESIS_INPUT`).
     - Telemetrie completă înregistrată în `candidate_trace['cognitive_core']`.

---

## 3. Ipoteze Formale Pre-înregistrate

### H1 — Latență (Overhead de Execuție Redus)
- **Ipoteză**: Overhead-ul median introdus de planificare, analiza de complexitate și gating-ul de buget nu va depăși 25% față de Brațul A, iar latența medie per interogare va rămâne sub 100 ms.
- **Metrică**: Timp de execuție per interogare ($ms$), măsurat cu `time.perf_counter_ns()`.

### H2 — Consum de Tokeni (Context Minimization)
- **Ipoteză**: Brațul B va reduce numărul mediu de tokeni conținuți în context pack-ul final cu cel puțin 15% comparativ cu Brațul A, prin eliminarea notelor distractor și respectarea plafonului `MAX_MEMORY_RESULTS = 5`.
- **Metrică**: Număr mediu de tokeni estimați per context pack (`ContextBudget.estimate_tokens(pack)`).

### H3 — Relevanță (Păstrarea Calității Regăsirii)
- **Ipoteză**: Relevanța contextului final (`context_recall`) nu va suferi o degradare statistic semnificativă ($p > 0.05$ la testul exact McNemar pe perechi discordante, sau $\Delta \ge 0.0000$).
- **Metrică**: Rata de cazuri măsurabile în care cel puțin o notă din `gold_relevant_notes` este prezentă în contextul livrat (`context_recall`).

### H4 — Rata de Încălcare a Bugetului (Zero Budget Violations)
- **Ipoteză**: Ambele brațe vor înregistra o rată de 0.0% a excepțiilor `BudgetExceededError`, iar Brațul B nu va depăși niciodată plafoanele specifice tier-ului de council atribuit.
- **Metrică**: Număr de încălcări de buget raportate / număr total de interogări.

---

## 4. Regula de Decizie

Dacă toate cele 4 ipoteze (H1, H2, H3, H4) sunt confirmate pe setul de date înghețat:
- Nucleul cognitiv este validat ca funcțional și benefic pe date reale.
- Componentele trec în `00_GOVERNANCE/VAULT_STATE.md` de la `not wired` la `wired, OFF by default`.
- Flag-ul rămâne `False` implicit pentru a respecta politica seifului (orice schimbare a valorii implicite necesită decizie separată de arhitectură).
