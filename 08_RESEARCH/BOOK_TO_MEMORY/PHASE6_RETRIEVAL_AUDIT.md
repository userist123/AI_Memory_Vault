# Audit Tehnic de Intrare — Faza 6: Retrieval & Working Memory Validation

**Document Version**: 1.0.0  
**Phase**: Phase 6 — Retrieval / Working Memory Validation  
**Branch**: `research/book-to-memory-phase6-retrieval`  
**Parent HEAD**: `fe4480130` (`feat(book-to-memory): implement Phase 5 With-Note vs Without-Note Ablation Framework`)  
**Audit Timestamp**: 2026-10-04T00:44:00+03:00  

---

## 1. Context și Obiective

Faza 6 din pipeline-ul de cercetare Book-to-Memory are rolul de a valida că notele de memorie extrase din cărți (validate prin Fazele 1–5: scheme atomice, porți de lifecycle, registru de conflicte, teste de utilizare și ablație comparativă):
1. Pot fi **indexate** corect pe baza câmpurilor lor semantice;
2. Răspund la principiul **„QUERY MUST MATTER”** (interogarea determină în mod real selecția și ordonarea candidaților);
3. Respectă **filtrarea negativă** (un query complet irelevant nu produce scoruri sau selecție nejustificată);
4. Respectă **porțile de lifecycle** (notele `RAW` și `REJECTED` sunt excluse necondiționat de la căutare);
5. Integrează **avertismentele de conflict** din Registrul de Conflicte (conflictele `OPEN` `HIGH`/`CRITICAL` sunt semnalate în contextul activ);
6. Păstrează **proveniența completă** (`source_title`, `chapter`, `page_range`, `exact_page`) în pachetul de memorie de lucru;
7. Asigură **pasivitatea conținutului din cărți** (protecție împotriva atacurilor de tip prompt injection sau comenzi executabile camuflate);
8. Se conformează **bugetului strict de Working Memory** (capacitate maximă, plafoane de tokeni, mecanism de evicțiune deterministă bazat pe scor de atenție).

---

## 2. Inspectarea Arhitecturii Existente de Căutare și Memorie de Lucru

### 2.1. `MemoryController.search()` (`03_IMPLEMENTATION/packages/memory/controller.py`)
- **Sanitizare și Clasificare**:
  - `sanitized = self._sanitize_query(query)` curăță spațiile albe și caracterele de control.
  - `self.query_classifier.classify(sanitized)` identifică intenția și filtrele deduse sau explicite.
  - Verifică `AGENT_LIFECYCLE_FLOOR = (Lifecycle.ACTIVE, Lifecycle.REVIEW)` pentru agenți AI (`Principal.AI_AGENT`).
- **Filtrare la Nivel de Storage Policy**:
  - Excludere necondiționată a notelor `RAW`.
  - Excludere a notelor sub palierul de autorizare al principalului.
- **Generare și Fuziune de Candidați** (`candidate_generation.py`):
  - RRF (Reciprocal Rank Fusion) combinând semnale BM25 și suprapunere de entități.
  - `DEFAULT_CANDIDATE_LIMIT = 200`.
- **Scorare de Relevanță** (`relevance_scoring.py`):
  - `RelevanceScorer.score()` evaluează suprapunerea lexicală și calitatea structurală.
  - Sortare deterministă pe baza scorului combinat și a ID-ului notei.
- **Integrarea cu Memoria de Lucru (`WorkingMemory`)**:
  - `active_enable_working_memory`: dacă este activat, instanțiază `WorkingMemory(capacity=page_size)`.
  - Înscrie candidații prin `wm.admit([(node, score), ...])`.
  - Contextul returnat este `wm.get_active_context()`.

### 2.2. `WorkingMemory` & `AttentionModel` (`cognitive_core/working_memory.py`, `attention.py`)
- **Modelul de Atenție**:
  $$\text{Score} = (0.5 \times \text{activation}) + (0.3 \times \text{confidence}) + (0.2 \times \text{recency})$$
  unde recența descrește pe măsură ce `current_tick` înaintează față de `recency_tick`.
- **Evicțiune Deterministă**:
  - Când `len(self.buffer) > self.capacity`, se sortează bufferul crescător după `(attention, id)` și se elimină nodurile cu scorul cel mai mic.
  - La egalitate de atenție, ordonarea după ID garantează repetabilitatea 100%.
- **Deduplicare și Actualizare**:
  - Dacă un nod există deja în buffer, admiterea lui îi actualizează activarea cu `max(existent, nou)` și actualizează `tick`-ul de recență la cel curent.

---

## 3. Identificarea Cerințelor Specifice pentru Book-to-Memory (Gap Analysis)

1. **Adaptor Textual pentru Indexare**:
   - Notele Book-to-Memory stochează informația în câmpuri structurate precum `atomic_concept`, `definition`, `ordered_steps`, `evidence`, `condition`, `recurring_structure`.
   - Căutarea lexicală (BM25 și suprapunerea de tokeni) necesită ca aceste câmpuri să fie compuse într-o reprezentare textuală unificată (`content`), astfel încât interogările relevante să poată regăsi conceptele.
2. **Conservarea Metadatelor de Proveniență în Working Memory**:
   - În working memory, agentul trebuie să aibă acces garantat la proveniența cărții (`source_title`, `chapter`, `page_range`, `exact_page`), nu doar la textul extras.
3. **Imunitate la Prompt Injection**:
   - Orice tentativă de manipulare prin textul extras (directive mascate ca instrucțiuni de sistem) trebuie să fie inertă și izolată de logica de execuție a agentului.
4. **Semnalizarea Conflictelor Deschise**:
   - Când o notă are conflicte deschise în `ConflictRegistry`, metadatele de conflict trebuie atașate nodului în Working Memory pentru ca agentul să fie conștient de disputa epistemică.
5. **Bugetare Strictă de Tokeni**:
   - Pe lângă capacitatea numerică de sloturi (`capacity`), este necesar un control al volumului de tokeni/caractere pentru a respecta constrângerile de context ale LLM-ului.

---

## 4. Concluzie Audit

Infrastructura de bază (`MemoryController`, `RetrievalEngine`, `WorkingMemory`, `AttentionModel`) este robustă și deterministă. Implementarea Fazei 6 va construi modulul `book_to_memory_retrieval.py` care integrează complet notele Book-to-Memory cu aceste motoare, validat printr-o suită exhaustivă de teste.
