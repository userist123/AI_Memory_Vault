---
id: a80a19a9-02f4-518b-94e6-0db81cf20a09
type: knowledge
lifecycle: REVIEW
category: ai/agent_memory_consolidation
tags:
- agents
- cognitive-architecture
- memory-consolidation
- ebbinghaus-decay
- sleep-cycles
- working-memory
- episodic-memory
- semantic-memory
created: '2026-09-04'
updated: '2026-09-04'
provenance:
  source_type: ai
  source_ref: 06_INBOX/RAW_IMPORTS/BOOKS/Generative-Agents-Park-Cognitive-Memory
confidence: high
verification: unverified
relations:
- relation: references
  target: 01_KNOWLEDGE/BOOKS/Agent_Architecture_and_Tool_Orchestration.md
- relation: references
  target: 01_KNOWLEDGE/BOOKS/ADVANCED_Agent_Tool_Protocols_and_FastMCP.md
- relation: references
  target: 01_KNOWLEDGE/BOOKS/SPECIALIZED_Agent_Reflexion_and_MultiAgent_Debate.md
- relation: references
  target: 01_KNOWLEDGE/BOOKS/Caiet_Teme_Aplicatii_Practice_Carti.md
---

# Arhitectura Agen?ilor: Cicluri de Somn, Consolidare Episodic? & Dec?derea Ebbinghaus

**Surs?**: Park et al. (*Generative Agents*), Harrison Chase & Arhitecturi Moderne de Memorie Cognitiv?  
**Domeniu**: Consolidarea Memoriei pe Termen Lung, Cur??are Autonom? & Ierarhii Cognitive

---

## 1. Dinamica Memoriei Agen?ilor: De la Flux Episodic la ?n?elegere Semantic?

Un agent AI autonom care execut? sarcini zilnice genereaz? mii de evenimente de execu?ie, mesaje ?i interac?iuni de unelte. Dac? toate sunt stocate brut, spa?iul de context este rapid saturat cu zgomot irelevant.

Arhitectura pe 3 niveluri a memoriei cognitive:
1. **Working Memory (Memoria de Lucru)**: Contextul activ al conversa?iei curente ($\le 2500$ tokens), stocat? volatil ?n `wm.json` sau context window.
2. **Episodic Memory (Memoria Episodic?)**: Jurnalul temporal al ac?iunilor, erorilor ?i concluziilor specifice fiec?rei sarcini (salvate ca `lifecycle: REVIEW` sau ?n `04_MEMORY/`).
3. **Semantic / Canonical Memory (Memoria Semantic?)**: Cuno?tin?e generale, principii abstracte ?i proceduri atestate (salvate ?n `01_KNOWLEDGE/` sau `02_PROCEDURES/` ca `lifecycle: ACTIVE`).

---

## 2. Modelul Matematic de Accesibilitate (Park et al. & Ebbinghaus)

Scorul de relevan?? al unei amintiri $m$ la momentul interog?rii $q$ este compus din trei factori pondera?i:
$$S(m, q) = \alpha \cdot \text{Recency}(m) + \beta \cdot \text{Importance}(m) + \gamma \cdot \text{Relevance}(m, q)$$

### A. Dec?derea Temporal? (Curba Uit?rii Ebbinghaus)
$$\text{Recency}(m) = e^{-\lambda \cdot \Delta t}$$
Unde $\Delta t = t_{\text{current}} - t_{\text{access}}$, iar factorul de stabilitate $\lambda$ determin? viteza cu care evenimentele neaccesate ??i pierd proeminen?a.

### B. Importan?a Intrinsec?
Un scor de la $1$ la $10$ atribuit la crearea noti?ei (ex: o decizie arhitectural? sau o violare de securitate are importan?? $9-10$, ?n timp ce un pas intermediar are $2-3$).

### C. Relevan?a Semantic?
Similaritatea cosinus ?ntre embedding-ul interog?rii $q$ ?i embedding-ul noti?ei $m$:
$$\text{Relevance}(m, q) = \frac{\mathbf{e}_q \cdot \mathbf{e}_m}{\|\mathbf{e}_q\| \cdot \|\mathbf{e}_m\|}$$

---

## 3. Ciclul Autonom de Somn ?i Consolidare (Sleep Cycle)

C?nd sistemul intr? ?n perioade de inactivitate (*idle phase*), agentul de consolidare (*Consolidator Agent*) declan?eaz? rutina de noapte:
1. **Faza 1: Recoltare & Filtrare**: Extrage noti?ele episodice din ultimele 24 de ore cu scoruri de dec?dere avansat?.
2. **Faza 2: Clustering Semantic**: Grupeaz? evenimentele conexe (ex: 5 erori de timeout la aceea?i baz? de date) folosind proximitatea grafic? ?i semantic?.
3. **Faza 3: Sintez? Abstractiv? & Generalizare**: Genereaz? o singur? noti?? canonic? de tip `lesson` sau `procedure` care sintetizeaz? concluzia esen?ial?.
4. **Faza 4: ?nregistrare Succesiune & Arhivare**:
   - Noti?a canonic? nou? indic? predecesoarele: `relations: [relation: supersedes, target: ...]`
   - Vechile noti?e tranzitorii sunt arhivate sau marcate ca `lifecycle: DEPRECATED`.
   - Modific?rile sunt ?nregistrate ?n lan?ul SHA-256 (`audit_log.jsonl`).

---

## 4. Garan?iile de Securitate ?n Memory Vault

- Conform invariantelor **`I-001..I-005`**, agentul de consolidare poate doar **propune** sinteza ?n starea `REVIEW`.
- Promovarea la `ACTIVE` ?i statutul de `verified` necesit? atestarea unui `Principal.HUMAN` sau `Principal.ADMIN` (`I-004`).

