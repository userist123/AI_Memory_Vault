# Pre-înregistrare: Evaluare Module Nucleu Cognitiv pe Benchmark v3

- **Data pre-înregistrării**: 2026-09-20
- **Autor / Rol**: ANTIGRAVITY (Lead Systems & Cognitive Architect)
- **Branch de lucru**: `antigravity/graph-and-core-real`
- **Statut**: PRE-REGISTERED (Comis în git înaintea execuției măsurătorilor empirice)
- **Set de date de referință**: `07_EVALUATION/retrieval_benchmark_v3/retrieval_benchmark_v3.json` (160 de cazuri, SHA-256: `eeb53822f36bba78c93427f7a791a5ec40da42337d1d293231d6833446b3f7bf`)
- **Număr cazuri măsurabile**: 130 cazuri (excluzând interogările negative și cele multi-hop irelevante)

---

## 1. Obiectiv și Context

Conform cerinței din master prompt:
> *"Pentru fiecare dintre activarea prin difuzie, working_memory, global_workspace, executive și reasoning:*
> *Consumatorul de producție. Modulul se leagă în MemoryController.search(), sau în calea pe care o apelează serverul MCP vault-memory...*
> *Legătura stă în spatele unui flag, cu valoarea implicită oprit.*
> *Verifici cu `grep -rl "<modul>" --include='*.py' . | grep -v "/tests/|test_|benchmarks"`: rezultatul nu are voie să fie gol.*
> *Regula preînregistrată se scrie și se face commit înaintea măsurătorii...*
> *Măsurătoarea se face pe benchmark-ul v3...*
> *Verdictul pe modul poate fi: «câștigă conform regulii — recomand pornirea» sau «nu câștigă — rămâne oprit, cu dovada»."*

Această preînregistrare formalizează ipotezele, mecanismele de cablare și regulile stricte de decizie pentru fiecare dintre cele 5 module înainte de rularea oricărei măsurători.

---

## 2. Brațele sub Test

Fiecare modul este evaluat ca un braț izolat față de brațul de referință (`baseline_off`), la un buget de context fixat (`top_k = 5`, aliniat la `MAX_MEMORY_RESULTS = 5` din `AGENTS.md`):

1. **`baseline_off`**:
   - Toate flag-urile cognitive adiționale setate pe `False`.
   - Căutare hibridă BM25 + suprapunere entități + clasificare lifecycle/type + ranking fuzionat (standardul de producție validat în r025).

2. **`spreading_activation` (`enable_spreading_activation: bool = True`)**:
   - Propagare multi-hop de activare pe graful de sinapse (hop-2) cu factor de atenuare decay = 0.5.
   - Evaluat deja pe benchmark v3 în Partea 1 (raportat în `BENCHMARK_V3_REPORT.md`).

3. **`working_memory` (`enable_working_memory: bool = True`)**:
   - Rezultatele candidate sunt admise în instanța `WorkingMemory(capacity=5)`.
   - Modulul recalculează scorurile de atenție prin `AttentionModel` (combinând activarea, frecvența, recența și atenuarea temporală) și elimină nodurile cu atenție redusă la depășirea capacității.

4. **`global_workspace` (`enable_global_workspace: bool = True`)**:
   - Rezultatele candidate sunt înaintate drept propuneri concurente (`WorkspaceProposal`) în `GlobalWorkspace(max_slots=5)`.
   - Se rulează ciclul competitiv bazat pe scor compozit:
     $$\text{Scor} = 0.5 \times \text{Coerență} + 0.3 \times \text{Activare ACT-R} + 0.2 \times \text{Utilitate}$$
   - Se difuzează coaliția câștigătoare (`compete_and_broadcast()`), selectând contextul optim.

5. **`reasoning` (`enable_reasoning: bool = True`)**:
   - Rezultatele candidate și interogarea sunt procesate de `ReasoningEngine.synthesize()`.
   - Pentru interogări de complexitate ridicată (ex: `why`, `how`, `root cause`, `compare`), se activează Tree-of-Thought (`tot_reasoner.reason()`), validând ramurile prin `ThoughtValidator` și atașând sinteza argumentată în trace-ul rezultatului.

6. **`executive` (`enable_executive: bool = True`)**:
   - Execută pasul de control al buclei executive prin `Executive._parse_intent()` și planificarea structurală a interogării.
   - Verifică integritatea fluxului de control și atașează decizia executivă în `candidate_trace`.

---

## 3. Regula de Decizie Preînregistrată

Pentru fiecare modul $M \in \{\text{spreading\_activation}, \text{working\_memory}, \text{global\_workspace}, \text{reasoning}, \text{executive}\}$, se aplică următorul set de condiții pe cele 130 de cazuri măsurabile din benchmark v3:

### Criterii de Câștig (Toate obligatorii pentru a recomanda pornirea):
1. **Câștig Net Context Recall**:
   $$\text{Wins}_{\text{context}} - \text{Losses}_{\text{context}} \ge +2$$
   (unde un caz este Win dacă $M$ conține cel puțin o notă gold în top-5 iar `baseline_off` nu conține, respectiv Loss dacă `baseline_off` conține iar $M$ nu conține).
2. **Semnificație Statistică**:
   $$p_{\text{McNemar}} < 0.05$$
   (calculat prin testul exact bi-direcțional McNemar pe perechile discordante de context recall: $\text{Binomial}(\text{Wins} + \text{Losses}, 0.5)$).
3. **Non-Degradare Candidate Recall**:
   Candidate recall nu trebuie să sufere o degradare statistic semnificativă ($p \ge 0.05$ sau net non-negativ: $\text{Wins}_{\text{cand}} \ge \text{Losses}_{\text{cand}}$).
4. **Latență și Stabilitate Runtime**:
   Latența mediană per interogare nu depășește $2.0\times$ latența mediană a `baseline_off`, iar rata de erori necontrolate este strict 0.0%.

### Formulare Verdict:
- **DACĂ** toate cele 4 criterii sunt îndeplinite:
  $$\text{Verdict: } \textbf{«câștigă conform regulii — recomand pornirea»}$$
  (Se documentează câștigul empiric și se propune activarea flag-ului).
- **DACĂ** oricare dintre criterii eșuează (inclusiv lipsa semnificației statistice, degradare de recall sau creștere excesivă a latenței):
  $$\text{Verdict: } \textbf{«nu câștigă — rămâne oprit, cu dovada»}$$
  (Modulul rămâne cablat în producție în spatele flag-ului, dar valoarea implicită rămâne ferm `False`, cu tabelul de date și raportul atașate ca dovadă empirică).
