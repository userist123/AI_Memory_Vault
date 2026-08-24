# Ghid: De la Cognitive Core la Arhitectură Brain-Like

Acest ghid explică, pas cu pas, cum treci de la sistemul actual (`AI_Memory_Vault_CODEX_READY`) la o arhitectură care se apropie mai mult de un creier real, pe baza celor 4 lipsuri identificate: decay de activare, spațiu global de conștiință (workspace competitiv), reconsolidare a memoriei, și modul motivațional/de recompensă. Include și o cale opțională (avansată) spre substrat sub-simbolic (spiking).

## 0. Ce ai deja, confirmat din repo

Repo-ul conține deja: `activation.py`, `attention.py`, `working_memory.py`, `orchestrator.py`, `learning.py`, `consolidation.py`, `synapse.py`, `executive.py`, `evaluation.py`, `planning.py`, `reflection.py`, `semantic.py`, `recall.py`, `tool_router.py`, `deduplication.py`, `version.py`, plus foldere `agents/` și `tests/`.

Existența `activation.py` sugerează că ai deja o formă incipientă de scoring pe bază de activare — pasul 1 de mai jos e despre a-l extinde la un model complet de decay tip ACT-R, nu despre a scrie de la zero.

## 1. Activation Decay (fundamentare ACT-R)

**Obiectiv:** fiecare notă/chunk din memorie primește un scor de activare care scade natural în timp dacă nu e reaccesat, și crește la fiecare acces (recency + frequency).

**Unde se integrează:** `cognitive_core/activation.py` (extindere) + hook în `recall.py` (la fiecare retrieval, actualizează activarea) + hook în `working_memory.py` (pragul de activare decide ce rămâne în memoria de lucru).

**Formula de referință (ACT-R base-level activation):**

\[ B_i = \ln\left(\sum_{j=1}^{n} t_j^{-d}\right) \]

unde \(t_j\) e timpul scurs de la accesul \(j\), iar \(d\) e rata de decădere (tipic 0.5).

**Pași de implementare:**
1. Adaugă în schema fiecărei note un câmp `access_history` (listă de timestamp-uri) sau, dacă vrei variantă simplificată, doar `last_accessed` + `access_count`.
2. Calculează scorul de activare la fiecare `recall()` folosind formula de mai sus (sau varianta simplificată \(B_i = \ln(n) - d \cdot \ln(t)\)).
3. Introdu un prag minim de activare sub care o notă e marcată `dormant` (nu dispare, dar nu mai concurează pentru workspace/atenție).
4. Expune scorul de activare ca input pentru `attention.py`, ca să influențeze direct scorurile de atenție existente, nu doar recency simplă.

**Criteriu de succes:** o notă folosită des rămâne "fierbinte" (scor mare) chiar dacă a fost creată acum mult timp; o notă creată recent dar nefolosită "se răcește" rapid.

## 2. Global Workspace (spațiu de conștiință competitiv)

**Obiectiv:** un hub central peste `orchestrator.py` unde agenții (Router, Retrieval, Consolidator, Verifier, Critic) concurează pentru acces la un "buffer" limitat, iar conținutul câștigător e difuzat (broadcast) simultan către toți, nu trecut secvențial.

**Unde se integrează:** fișier nou `cognitive_core/global_workspace.py`, cuplat cu `orchestrator.py` și `working_memory.py`.

**Pași de implementare:**
1. Definește o clasă `GlobalWorkspace` cu o coadă de propuneri (`WorkspaceProposal`) de la fiecare agent, fiecare cu un scor de "coeziune" (cât de relevantă/coerentă e propunerea).
2. Implementează o funcție `compete_and_broadcast()`: selectează propunerea/propunerile cu scor maxim (poate fi mai mult de 1, dacă scorurile sunt apropiate — simulează coaliții de procesoare, cum descrie GWT).
3. Difuzează rezultatul câștigător tuturor agenților printr-un `broadcast_channel` (poate fi simplu: fiecare agent primește update prin callback sau prin citirea unui obiect comun `current_broadcast`).
4. Limitează capacitatea bufferului (ex. maxim 3-5 "gânduri" active simultan), analog cu limitarea memoriei de lucru umane (7±2 la Miller, dar sistemele moderne GWT folosesc bufere mai mici, 1-4 sloturi).
5. Integrează cu `attention.py`: scorul de atenție + scorul de activare (pasul 1) determină cine câștigă competiția.

**Criteriu de succes:** când `RetrievalAgent` găsește o informație relevantă și `CriticAgent` o contestă simultan, workspace-ul alege un singur "câștigător" vizibil pentru toți, nu doar pentru cei doi agenți implicați.

## 3. Reconsolidare (memorie plastică, nu rigidă)

**Obiectiv:** o cunoștință deja consolidată (canonică) poate fi retrasă temporar înapoi în stare "volatilă" când e reactivată și contestată de informație nouă, evitând rigiditatea excesivă.

**Unde se integrează:** `cognitive_core/consolidation.py` (extindere directă a clasei `Consolidator`).

**Pași de implementare:**
1. Adaugă stări explicite pentru fiecare notă: `VOLATILE → CONSOLIDATING → CANONICAL`, plus o tranziție nouă `CANONICAL → RECONSOLIDATING` declanșată de conflict.
2. Când `VerifierAgent` (deja existent) detectează o contradicție între o notă `CANONICAL` și o informație nouă, marchează nota ca `RECONSOLIDATING` în loc s-o suprascrie direct.
3. În stare `RECONSOLIDATING`, nota devine editabilă din nou de `ConsolidatorAgent`, cu istoricul păstrat (ambele versiuni, plus proveniența conflictului) — nu se pierde varianta veche.
4. După o fereastră de timp sau după un nou ciclu de reflecție (`FormalReflexion`), nota revine la `CANONICAL` cu conținutul actualizat, sau rămâne `VOLATILE` dacă conflictul nu se rezolvă.

**Criteriu de succes:** o "regulă" veche din memorie nu e ștearsă brutal la prima contrazicere, ci trece printr-un ciclu de reevaluare, la fel cum reconsolidarea biologică permite actualizarea amintirilor fără pierderea completă a variantei anterioare.

## 4. Modul motivațional / de recompensă

**Obiectiv:** un sistem de valoare (analog utilității producțiilor din ACT-R sau dopaminei din creier) care ajustează dinamic scorurile de atenție și prioritate, nu doar pe bază de reguli statice.

**Unde se integrează:** fișier nou `cognitive_core/motivation.py`, cuplat cu `attention.py`, `evaluation.py` și `learning.py`.

**Pași de implementare:**
1. Definește un `RewardSignal` — un scor numeric asociat fiecărui rezultat de acțiune (succes/eșec al unui task, validare pozitivă/negativă de la `VerifierAgent`, feedback explicit al userului).
2. Implementează o funcție de actualizare a "utilității" fiecărui tip de acțiune/agent, similar cu utilitatea producțiilor ACT-R: \(U = P \cdot G - C\) (probabilitate de succes × valoarea scopului − cost).
3. Folosește acest scor de utilitate ca input suplimentar în competiția din Global Workspace (pasul 2): propunerile venite din tipuri de acțiuni cu utilitate istorică mare primesc un bonus la scorul de competiție.
4. Opțional: introdu un scor de "emoție" simplu (valență pozitivă/negativă + intensitate) atașat notelor din `04_MEMORY/Experiences`, care influențează prioritatea de reactivare (analog cu memoria emoțională umană, care e reamintită mai ușor).

**Criteriu de succes:** sistemul începe să prioritizeze automat task-urile/tipurile de informație care istoric au dus la rezultate bune, nu doar pe bază de recency sau reguli fixe.

## 5. Cale opțională avansată: substrat sub-simbolic

Dacă vrei să mergi până la capăt spre "neuron real", nu doar comportament cognitiv:

1. Explorează o bibliotecă de spiking neural networks (ex. `snnTorch`, `Brian2`, `Nengo`) ca modul separat de experimentare, NU ca înlocuitor al `cognitive_core` simbolic.
2. Folosește neuronul Leaky Integrate-and-Fire (LIF) ca unitate de bază, cu învățare STDP pentru conexiuni.
3. Tratează asta ca un modul de cercetare paralel (`cognitive_core/neuromorphic/`, experimental, izolat de pipeline-ul de producție), pentru că integrarea completă cu restul sistemului simbolic e un proiect separat, de cercetare pe termen lung, nu un upgrade incremental.

## Ordinea recomandată de implementare

1. Activation Decay (cel mai simplu, impact imediat, extinde ce ai deja).
2. Reconsolidare (extinde `consolidation.py` existent, risc mic).
3. Modul motivațional (fișier nou, cuplare moderată).
4. Global Workspace (cea mai mare schimbare arhitecturală, atinge `orchestrator.py`).
5. Substrat sub-simbolic (opțional, pe termen lung, izolat).

## Cum testezi fiecare pas

Pentru fiecare modul nou, adaugă teste în `cognitive_core/tests/` care verifică:
- Activation decay: o notă neaccesată 30 de zile are scor mai mic decât una accesată azi, la frecvență egală.
- Reconsolidare: o notă `CANONICAL` contrazisă trece prin `RECONSOLIDATING` și păstrează istoricul, nu se suprascrie silențios.
- Motivație: un tip de acțiune cu 10 succese consecutive are utilitate calculabilă mai mare decât unul cu 10 eșecuri.
- Global Workspace: cu 2 propuneri concurente, doar cea cu scor combinat (atenție + activare + utilitate) mai mare e difuzată agenților.
