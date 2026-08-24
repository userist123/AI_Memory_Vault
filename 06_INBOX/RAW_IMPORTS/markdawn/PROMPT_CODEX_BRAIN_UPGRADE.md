# Prompt pentru agent de coding (Codex / Claude Code / Cursor)

Copiază tot conținutul de mai jos și rulează-l ca prompt într-un agent care are acces read/write la repo-ul `userist123/AI_Memory_Vault_CODEX_READY` (Codex CLI, Claude Code, sau orice agent cu tool GitHub/filesystem).

---

## PROMPT

Ești un inginer software senior specializat în arhitecturi cognitive și sisteme de memorie AI. Lucrezi pe repo-ul `AI_Memory_Vault_CODEX_READY`, care are deja un `cognitive_core/` funcțional cu modulele: `activation.py`, `attention.py`, `working_memory.py`, `orchestrator.py`, `learning.py`, `consolidation.py`, `synapse.py`, `executive.py`, `evaluation.py`, `planning.py`, `reflection.py`, `semantic.py`, `recall.py`, `tool_router.py`, `deduplication.py`, `version.py`, și un folder `agents/` cu `RouterAgent`, `RetrievalAgent`, `ConsolidatorAgent`, `VerifierAgent`, `CriticAgent`.

Respectă strict `AGENTS.md` din rădăcina repo-ului ca operating contract înainte de orice modificare.

### Context

Sistemul acoperă deja: atenție, raționament tip Tree-of-Thought, reflecție formală (Reflexion + SelfRefine), consolidare, un sistem multi-agent cu guvernanță (audit log, control de acces, buget de context). Lipsesc patru componente cheie ca sistemul să se apropie de o arhitectură cognitivă completă tip creier: decay de activare (ACT-R), spațiu global de conștiință competitiv (Global Workspace Theory), reconsolidare a memoriei (neuroștiință computațională), și un modul motivațional/de recompensă.

### Sarcina ta, în ordine, câte un modul complet per commit

**Pasul 1 — Activation Decay**
1. Citește conținutul curent al `cognitive_core/activation.py`, `recall.py`, `working_memory.py`.
2. Extinde `activation.py` cu o funcție `base_level_activation(access_times: list[float], decay: float = 0.5) -> float` care implementează formula ACT-R: \(B_i = \ln(\sum_j t_j^{-d})\).
3. Adaugă o clasă/structură `ActivationRecord` care ține `access_history` per notă (timestamp-uri).
4. Hook în `recall.py`: la fiecare retrieval reușit, apelează `record_access()` pe nota respectivă.
5. Hook în `attention.py`: scorul de atenție existent trebuie combinat (sumă ponderată sau produs) cu scorul de activare calculat.
6. Adaugă un prag `DORMANT_THRESHOLD` — notele sub prag nu mai concurează activ pentru workspace/atenție, dar rămân recuperabile explicit.
7. Scrie teste în `cognitive_core/tests/test_activation.py` care verifică monotonia decay-ului și efectul frecvenței de acces.
8. Nu modifica formatul de stocare al notelor din vault (frontmatter Markdown) decât adăugând câmpuri noi opționale (`last_accessed`, `access_count`), niciodată eliminând câmpuri existente.

**Pasul 2 — Reconsolidare**
1. Citește `consolidation.py` complet, identifică enumul/stările curente ale unei note (probabil ceva de tipul DRAFT/REVIEW/CANONICAL).
2. Adaugă o stare nouă `RECONSOLIDATING` în ciclul de viață.
3. Adaugă o metodă `challenge(note_id, conflicting_evidence)` pe clasa `Consolidator`, apelabilă de `VerifierAgent` când detectează o contradicție cu o notă `CANONICAL`.
4. `challenge()` mută nota în `RECONSOLIDATING`, păstrează versiunea veche într-un istoric (`superseded_by` / `previous_version`), și o face din nou editabilă de `ConsolidatorAgent`.
5. După rezolvare (nou ciclu de `FormalReflexion` sau timeout configurabil), nota revine la `CANONICAL` actualizată sau la `VOLATILE` dacă rămâne nerezolvată.
6. Loghează fiecare tranziție prin `AuditLogger` existent din `memory_controller/audit/logger.py`.
7. Scrie teste în `cognitive_core/tests/test_reconsolidation.py`.

**Pasul 3 — Modul motivațional**
1. Creează fișier nou `cognitive_core/motivation.py`.
2. Implementează clasa `RewardSignal` (scor float, sursă, timestamp, tip acțiune).
3. Implementează clasa `UtilityTracker` cu o funcție `update_utility(action_type, reward)` bazată pe formula ACT-R \(U = P \cdot G - C\) sau o variantă simplificată de medie mobilă exponențială a recompenselor per tip de acțiune.
4. Expune `get_utility(action_type) -> float` folosit ca bonus de scor în `attention.py` și `evaluation.py`.
5. Conectează sursele de recompensă existente: rezultat de la `VerifierAgent` (validare pozitivă/negativă), feedback din `evaluation.py`.
6. Scrie teste în `cognitive_core/tests/test_motivation.py`.

**Pasul 4 — Global Workspace**
1. Creează fișier nou `cognitive_core/global_workspace.py`.
2. Implementează clasa `WorkspaceProposal(agent_id, content, coherence_score)`.
3. Implementează clasa `GlobalWorkspace` cu:
   - o coadă de propuneri primite de la agenți în cadrul unui ciclu al `orchestrator.py`;
   - metoda `compete_and_broadcast()` care calculează scorul final per propunere = combinație din: `coherence_score` (dat de agent) + scor de activare (Pasul 1) + utilitate (Pasul 3);
   - selectează câștigătorul (sau coaliția, dacă scorurile sunt în interval de toleranță configurabil) și îl scrie într-un `current_broadcast` accesibil tuturor agenților;
   - limitează numărul de sloturi active simultan (parametru configurabil, implicit 3).
4. Modifică `orchestrator.py` astfel încât, în loc de pipeline strict secvențial Router → Retrieval → Consolidator → Verifier → Critic, fiecare agent să trimită propuneri către `GlobalWorkspace` și să citească `current_broadcast` înainte de a-și finaliza acțiunea.
5. Păstrează compatibilitate retro: dacă `GlobalWorkspace` nu are propuneri concurente (un singur agent activ), comportamentul trebuie să fie identic cu pipeline-ul actual.
6. Scrie teste în `cognitive_core/tests/test_global_workspace.py`, inclusiv un test cu 2+ propuneri concurente unde verifici că doar câștigătorul apare în `current_broadcast`.

### Reguli obligatorii pentru toate pașii

- Fiecare pas e un commit separat, cu mesaj clar (`feat(cognitive_core): add activation decay per ACT-R model`, etc.).
- Nu șterge sau redenumești fișiere/clase existente fără a verifica toate locurile unde sunt importate.
- Rulează suita de teste existentă înainte și după fiecare pas; nu trece la pasul următor dacă testele existente au căzut din cauza modificării tale.
- Documentează fiecare modul nou cu docstring la nivel de fișier care explică analogia neuroștiințifică (ACT-R / GWT / reconsolidare / utilitate) și trimite către sursele teoretice relevante în comentarii.
- Actualizează `README.md` din `cognitive_core/` (sau creează unul dacă nu există) cu un tabel care mapează fiecare modul nou la conceptul din neuroștiință/arhitecturi cognitive pe care îl implementează.
- Nu introduce dependențe externe grele (fără PyTorch/TensorFlow) pentru pașii 1-4 — totul trebuie să fie Python standard + eventual `numpy`, pentru consistență cu restul `cognitive_core`.
- La final, generează un pull request cu descriere care rezumă cele 4 module adăugate și cum se conectează între ele.

---

## Cum îl folosești

1. Deschide Codex CLI, Claude Code sau Cursor cu acces la repo-ul clonat local (sau conectat prin GitHub App).
2. Lipește promptul de mai sus ca instrucțiune inițială.
3. Lasă agentul să lucreze pas cu pas (Pasul 1 → 4), revizuind fiecare commit înainte de a trece mai departe.
4. Verifică manual PR-ul final înainte de merge.

---

## 🔗 Legături de Memorie & Graf Obsidian
- [[Knowledge Graph Home]]
- [[00 Core Map]]
- [[Knowledge Graph Home]]
