# DOSARUL CELOR 7 RÂNDURI ÎN AȘTEPTARE (PENDING_DECISIONS.md)

> **Dosar de decizie arhitecturală și analiză contextuală**  
> **Destinatar**: Owner  
> **Status**: Proiect de decizie — ZERO modificări în sloturi, ZERO execuții nesancționate.  
> **Precedent**: `disposition_manifest.json` (6 UNDECIDED + 1 SPLIT)  
> **Data**: 2026-09-12

---

## 0. De ce Există Acest Dosar

Din cele 213 rânduri de slot de pe disc, **206 au o dispoziție fermă și executabilă** (84 de păstrat ca promovate, 2 de păstrat ca propuse în triaj, 112 de șters mecanic, 8 de fuzionat).

Au rămas exact **7 rânduri**:
- **6 rânduri `UNDECIDED`**: Concepte unde poarta de verdicte a returnat `UNSURE` din cauza unei ambiguități structurale pe care un script sau o regulă mecanică nu o poate rezolva fără a lua o decizie arhitecturală arbitrară.
- **1 rând `SPLIT`**: `familiarity` (`10_confidence.md:30`), unde un singur nume pe disc ascunde două concepte complet diferite provenite din două surse disjuncte.

Acest document expune dosarul complet al fiecăruia: citatele exacte din staging, ambiguitatea concretă pe text, opțiunile de rezolvare cu ce câștigă și ce pierde fiecare, și propunerea exactă de rânduri pentru separarea lui `familiarity`.

---

## 1. Cele 6 Concepte `UNDECIDED`

### 1.1. `symbolic`
- **Ancoră slot**: `03_ontology.md:40`
- **Status pe disc**: `proposed`
- **Occurrences**: `4` (1 în `comparison_cognitive_architectures`, 3 în `wcs_1488`)
- **Cărți-sursă**: `comparison_cognitive_architectures; wcs_1488`
- **Citate din staging**:
  - `comparison_cognitive_architectures` (pp. 1-3):
    > *"Names Underlying theory (symbolic, connectionist, or hybrid) Achievements Estimated requirements Nonlaboratory usages Activity status..."*
  - `wcs_1488` (Cap. 2.1 & 3):
    > *"They have symbolic components that are accessible to the model and subsymbolic information that moderates behavior and accessibility"*
- **Definiție staging**: Element discret structural (chunks, producții) ce formează reprezentări cognitive explicite accesibile modelului.

#### Ambiguitatea concretă:
Termenul suferă de o confuzie de nivel de abstractizare:
1. În `comparison_cognitive_architectures`, este o **etichetă meta-clasificatoare de paradigmă** (distincția de nivel înalt între arhitecturi simbolice, conexioniste și hibride).
2. În `wcs_1488` (ACT-R), este un **descriptor de nivel de reprezentare** (modulul simbolic format din chunk-uri declarative și reguli de producție, în contrast cu activările subsimbolice).
Dacă introducem nodul `symbolic` în slotul `03_ontology` (primitivă ontologică de memorie), creăm riscul de a amesteca paradigme globale de computație cu primitive locale de stocare a cunoștințelor.

#### Opțiuni de decizie:

- **Opțiunea A — Promovare ca Primitivă Reprezentațională (`PROMOTE` în `03_ontology.md`)**:
  - *Ce câștigă*: Formalizează reprezentările discrete (fapte, reguli, grafuri) în opoziție cu greutățile subsimbolice (embeddings, rețele neuronale). Oferă o ancoră clară pentru cunoștințele structurate din Memory Vault.
  - *Ce pierde*: Numele este un adjectiv („simbolic”), nu un substantiv ontologic („reprezentare simbolică” sau „simbol”). Poate deveni un recipient prea larg pentru orice nod neneuronal.
- **Opțiunea B — Fuzionare în `chunk` (`MERGE_INTO` -> `chunk`, `03_ontology.md:27`)**:
  - *Ce câștigă*: În ACT-R (`wcs_1488`), componenta simbolică este concretizată exact prin `chunk` (promovat deja cu `note-44161a00...`). Reuniunea ar transfera cele 4 ocurențe către o structură operațională discretă existentă.
  - *Ce pierde*: `chunk` este o unitate psihologică/computațională specifică (Newell/Anderson); `symbolic` acoperă și producțiile sau regulile logice.
- **Opțiunea C — Respingere / Ștergere (`DELETE`)**:
  - *Ce câștigă*: Elimină un adjectiv generic care în lucrarea principală apare doar ca antet de coloană tabelară.
  - *Ce pierde*: Se pierde referința explicită din ACT-R la dualitatea simbolic/subsimbolic.

> **Evaluare la recitire**: Ambiguitatea este parțial rezolvabilă. Citatul din `wcs_1488` arată clar că autorii nu definesc „symbolic” ca o memorie separată, ci ca proprietate a chunk-urilor și producțiilor. **Opțiunea B (Fuziune în `chunk`) sau Opțiunea C (Ștergere)** sunt net superioare promovării de sine stătătoare.

---

### 1.2. `integrated information`
- **Ancoră slot**: `03_ontology.md:55`
- **Status pe disc**: `proposed`
- **Occurrences**: `4`
- **Carte-sursă**: `quantum_consciousness_framework`
- **Citat din staging**:
  > *"ble. References 1. Tononi, G., & Koch, C. (2012). Integrated information theory of consciousness: an updated account. Archives italiennes de biologie, 150(2-3), 56–90. 2."* (Measure consciousness metrics)
- **Definiție staging**: O măsură propusă ca bază fizică pentru conștiință ($\Phi$), implementată prin tomografie cuantică și încorporată într-un cadru de drepturi etice ca factor multiplicativ.

#### Ambiguitatea concretă:
Teoria Informației Integrate (IIT - Tononi, Koch) este o teorie matematică și neuroștiințifică recunoscută pe plan internațional pentru măsurarea integrării informaționale ($\Phi$). Cu toate acestea, în corpusul din care a fost extras (`quantum_consciousness_framework`):
1. Este amestecată cu speculații nestandard despre tomografie cuantică și particule.
2. Însuși citatul de evidență din text este doar un fragment trunchiat din secțiunea de referințe bibliografice (`"ble. References 1. Tononi, G..."`).
Proveniența conceptului este viciată de un text marcat în auditurile precedente ca având halucinații metodologice.

#### Opțiuni de decizie:

- **Opțiunea A — Respingere / Ștergere (`DELETE`)**:
  - *Ce câștigă*: Curăță ontologia de un concept extras exclusiv dintr-un preprint speculativ a cărui probă textuală este un fragment din bibliografie.
  - *Ce pierde*: Dacă Memory Vault intenționează să implementeze metrica $\Phi$ (IIT) pentru evaluarea unității memoriei de lucru, noțiunea teoretică va trebui reintrodusă ulterior dintr-o sursă curată.
- **Opțiunea B — Păstrare în carantină (`KEEP_PROPOSED` în așteptarea re-ancorării)**:
  - *Ce câștigă*: Păstrează semnalul că IIT este de interes arhitectural, dar blochează promovarea până când o lucrare canonică (Tononi & Koch 2012/2016) este adăugată în corpus.
  - *Ce pierde*: Menține un rând `proposed` fără evidență solidă în slot.
- **Opțiunea C — Promovare condiționată de rescrierea notei din surse externe**:
  - *Ce câștigă*: Furnizează un fundament formal pentru măsurarea coerenței memoriei globale.
  - *Ce pierde*: Încalcă principiul că un concept promovat trebuie să fie susținut de evidențe empirice curate din cărțile corpusului.

> **Evaluare la recitire**: Ambiguitatea este rezolvabilă clar: citatul din staging este pură bibliografie (`"References 1. Tononi..."`). Un concept a cărui unică probă este un rând bibliografic dintr-un preprint speculativ **trebuie respins (`DELETE`)**. Dacă Owner dorește IIT în sistem, acesta trebuie extras dintr-un tratat autentic de neuroștiințe.

---

### 1.3. `dual memory`
- **Ancoră slot**: `03_ontology.md:60`
- **Status pe disc**: `proposed`
- **Occurrences**: `7`
- **Carte-sursă**: `sarfraz22a`
- **Citat din staging**:
  > *"from the results. Both synaptic consolidation and dual memory experience replay play a critical role in reducing forgetting. Mean-ER provides a significant performance improvement"* (Conclusion, Formulation, Model Characteristics)
- **Definiție staging**: Cadru neuro-inspirat care modelează interacțiunile hipocamp-neocortex pentru învățare continuă prin două viteze de adaptare.

#### Ambiguitatea concretă:
Termenul `dual memory` este utilizat în Sarfraz et al. ca adjectiv compus pentru „dual memory experience replay” (sistemul de replay care eșantionează atât din buffer-ul episodic cât și din modelul stabil). Întrebarea arhitecturală este:
- Reprezintă `dual memory` un **tip distinct de memorie** (care să stea alături de `episodic memory` și `semantic memory` în `03_ontology`)?
- Sau este doar o **etichetă pentru arhitectura CLS (Complementary Learning Systems)**, care operează deja prin cuplarea a două memorii existente?

#### Opțiuni de decizie:

- **Opțiunea A — Fuzionare în `Complementary Learning Systems` (`MERGE_INTO` -> `CLS`)**:
  - *Ce câștigă*: În triajul unverified, `Complementary Learning Systems` a fost recunoscut ca teoria autentică din lucrare (4 occ). `dual memory` este denumirea computațională a aceluiași principiu McClelland. Unirea celor două unifică teoria biologică cu modelul ingineresc.
  - *Ce pierde*: `Complementary Learning Systems` este în prezent reținut ca `KEEP_PROPOSED` la linia 47 în `16_consolidation.md`, deci fuziunea ar transfera un concept proposed într-un alt concept proposed (necesită promovarea prealabilă a CLS).
- **Opțiunea B — Promovare ca Meta-Arhitectură în `01_identity.md` sau `03_ontology.md`**:
  - *Ce câștigă*: Oferă un nod ontologic pentru principiul fundamental al AI Memory Vault: sistem dual (memorie rapidă de lucru + memorie lentă consolidată).
  - *Ce pierde*: Duplică conceptual slotul `00_CORE` sau descrierea globală a sistemului de memorie.
- **Opțiunea C — Respingere (`DELETE`)**:
  - *Ce câștigă*: Evită redundanța. Sistemul conține deja explicit componentele duale (`working memory` și `semantic memory`).
  - *Ce pierde*: Nimic funcțional.

> **Evaluare la recitire**: În textul lui Sarfraz, `dual memory` este întotdeauna un calificativ pentru CLS sau pentru protocolul de replay. **Cea mai elegantă rezolvare este Opțiunea A (fuzionare în `Complementary Learning Systems`) la momentul când CLS este promovat**, sau **Opțiunea C (ștergere)** dacă se consideră că cele două sloturi individuale sunt suficiente.

---

### 1.4. `shared memory`
- **Ancoră slot**: `04_relationships.md:32`
- **Status pe disc**: `proposed`
- **Occurrences**: `9`
- **Carte-sursă**: `memory_in_the_age_of_ai_agents`
- **Citat din staging**:
  > *"learning with memory systems, multimodal memory, shared memory for multi-agent systems, and trustworthiness issues. We hope this survey serves not only as a reference for existing work, but also as"* (pp. 1-3)
- **Definiție staging**: Substrat informațional comun accesibil mai multor agenți interacționați pentru sincronizarea stărilor colaborative și evitarea interogărilor redundante.

#### Ambiguitatea concretă:
În survey-ul *Memory in the Age of AI Agents*, autorii listează `shared memory for multi-agent systems` în introducere ca una dintre cele 4 direcții deschise de cercetare („open research challenges”), fără a formaliza o structură de date, o ecuație sau un algoritm.
- Este o temă de cercetare de perspectivă într-un survey de sinteză.
- În arhitectura AI Memory Vault, relația dintre agenți și memorie este un subiect central de guvernanță (Council, agenți specialiști, spațiu comun de lucru).

#### Opțiuni de decizie:

- **Opțiunea A — Promovare în `04_relationships.md` (`PROMOTE`)**:
  - *Ce câștigă*: Ancorează în ontologie conceptul de memorie multi-agent partajată (blackboard pattern / shared vault), definind modul în care Antigravity, Claude Code și agenții subordonați colaborează pe aceleași note.
  - *Ce pierde*: Conceptul din lucrare este extrem de sumar (doar o trecere în revistă a literaturii). Definiția operațională va trebui construită intern, nu preluată din carte.
- **Opțiunea B — Fuzionare în `global workspace` (`01_identity.md:28`)**:
  - *Ce câștigă*: În teoria cognitivă, spațiul partajat prin care procese autonome distribuite fac schimb de mesaje și reprezentări este formalizat exact prin `global workspace`.
  - *Ce pierde*: Global workspace are conotații de conștiință / atenție centrală, în timp ce `shared memory` este un termen clasic de inginerie software concurentă.
- **Opțiunea C — Respingere (`DELETE`)**:
  - *Ce câștigă*: Curăță un termen care în carte reprezintă doar o schiță bibliografică.
  - *Ce pierde*: Oportunitatea de a avea o primitivă ontologică pentru cooperarea multi-agent.

> **Evaluare la recitire**: Dacă AI Memory Vault este explicit un sistem multi-agent (după cum atestă `AGENTS.md` și cele 21 de roluri din Consiliu), `shared memory` este exact ceea ce este acest Vault. Dacă Owner dorește un nod ontologic pentru acest mecanism, **Opțiunea A este legitimă ca decizie de proiectare**. Dacă se dorește puritate pe textul cărții, citatul fiind slab, **Opțiunea C sau B este indicată**.

---

### 1.5. `in-context learning`
- **Ancoră slot**: `06_procedures.md:32`
- **Status pe disc**: `proposed`
- **Occurrences**: `3`
- **Carte-sursă**: `2601.09113v1`
- **Citat din staging**:
  > *"akraborty. Language models can exploit cross-task in-context learning for data-scarce novel tasks. arXiv preprint arXiv:2405.10548, 2024. Subhajit Chaudhury, Payel Das, Sarathkrishna Swaminathan, Geor"* (pp. 43-45)
- **Definiție staging**: Capacitatea modelelor neurale de a generaliza din câteva demonstrații exemplare plasate în secvența de intrare (prompt).

#### Ambiguitatea concretă:
Ambiguitate categorică severă:
- `in-context learning` (ICL) este o **proprietate computațională nativă a transformatoarelor mari de limbaj (LLMs)** prin care modelul își ajustează predicția pe baza atenției din context window, fără a altera parametrii stocați.
- Slotul `06_procedures.md` este rezervat pentru **procedurile cognitive executabile ale sistemului de memorie** (cum se consolidează, cum se recuperează, cum se filtrează).
Este ICL o procedură pe care o „execută” sistemul de memorie, sau este doar mecanismul de inferență al motorului extern de execuție? În plus, citatul din lucrare este un titlu dintr-o referință bibliografică.

#### Opțiuni de decizie:

- **Opțiunea A — Respingere / Ștergere (`DELETE`)**:
  - *Ce câștigă*: Păstrează slotul `06_procedures` strict pentru proceduri algoritmice de memorie (replay, indexare, recall). ICL este comportamentul LLM-ului, nu o procedură de vault. În plus, evidența din staging este un fragment dintr-o bibliografie arXiv.
  - *Ce pierde*: Nu recunoaște formal rolul promptului ca spațiu tranzitoriu de învățare.
- **Opțiunea B — Mutare / Fuzionare în `retrieval-augmented generation` (`15_retrieval.md:41`)**:
  - *Ce câștigă*: În arhitecturile de memorie AI, ICL este mecanismul final prin care contextul extras prin RAG devine acțiune. Fuziunea consolidează întregul flux de consum al memoriei.
  - *Ce pierde*: RAG este o procedură de căutare; ICL este o procedură de inferență.
- **Opțiunea C — Promovare ca Procedură de Interfață în `06_procedures.md`**:
  - *Ce câștigă*: Definește procedura prin care context pack-ul minim asamblat de Consiliu este injectat în model pentru execuție.
  - *Ce pierde*: Amestecă literatura internă a modelelor cu ingineria memoriei.

> **Evaluare la recitire**: Ambiguitatea este rezolvabilă imediat. Citatul este o intrare bibliografică (`"arXiv preprint arXiv:2405.10548..."`). Procedurile din `06_procedures` trebuie să fie acțiuni ale vault-ului. **Opțiunea A (Ștergere)** este cea mai curată și disciplinată decizie.

---

### 1.6. `component process`
- **Ancoră slot**: `06_procedures.md:49`
- **Status pe disc**: `proposed`
- **Occurrences**: `5`
- **Carte-sursă**: `schacter_tulving_memory_systems_1994`
- **Citat din staging**:
  > *"1 Although not all component processes of MEM are represented in all species (indeed, MEM provides some preliminary hypotheses about the evolution of subsystems; see Johnson & Hirst, 1993), we assume that reactivation is normally operative in rodents as well as humans and other primates."* (pp. 205-207)
- **Definiție staging**: Operații funcționale elementare în cadrul arhitecturii modulare cu intrări multiple (MEM), responsabile de sarcini discrete precum comutarea atenției sau reactivarea.

#### Ambiguitatea concretă:
În capitolul semnat de Johnson & Hirst în Schacter & Tulving (1994), autorii descriu modelul MEM (*Multiple-Entry, Modular memory system*), unde memoria nu este un bloc monolit, ci un ansamblu de procese componente sub-divizate în procese de reflectare (reflection) și procese de notare (noticing).
- `component process` este un **hiperonim extrem de abstract**: denumește noțiunea de „sub-proces elementar”.
- În ontologie, a avea o procedură numită „proces component” este tautologic (orice procedură este compusă din procese componente).

#### Opțiuni de decizie:

- **Opțiunea A — Respingere / Ștergere (`DELETE`)**:
  - *Ce câștigă*: Elimină un termen generic care nu descrie o procedură anume, ci faptul că procedurile au componente.
  - *Ce pierde*: Nicio funcționalitate sau cunoștință aplicată.
- **Opțiunea B — Promovare ca Meta-Principiu Arhitectural (`PROMOTE`)**:
  - *Ce câștigă*: Ancorează principiul modularității cognitive (Johnson & Hirst MEM framework): nicio funcție de memorie nu este indivizibilă, ci este o compoziție de reactivări și comutări.
  - *Ce pierde*: Creează un precedent pentru termeni teoretici non-operaționali în sloturile procedurale.
- **Opțiunea C — Fuzionare în `procedural memory` (`06_procedures.md:30`)**:
  - *Ce câștigă*: Aliniază procesele componente cu memoria procedurală ca mediu de execuție.
  - *Ce pierde*: Distorsionează semnificația din lucrare (unde procesele componente se aplicau și percepției și memoriei de lucru).

> **Evaluare la recitire**: Ambiguitatea este rezolvabilă. Termenul este un hiperonim generic de descriere modulară. **Opțiunea A (Ștergere)** este recomandată de principiul parcimoniei ontologice (Occam's razor).

---

## 2. Cazul Special `SPLIT`: `familiarity` (`10_confidence.md:30`)

### 2.1. Diagnosticul Homonimiei pe Disc
Pe discul actual, la `10_confidence.md:30`, există un singur rând:
```markdown
| familiarity | 2504.05840v1; why_we_forget | 0.95 | proposed | 2026-09-12 | | el input embedding p. We introduce an additional ‘familiarity’ buffer (light cyan buffer in Figure 4) that employs boosted contrastive learning to prioritize and | 9 |
```

Acest rând combină două concepte complet diferite, derivate din două paradigme opuse:

| Proprietate | Conceptul 1: Metacognitiv / Psihologic | Conceptul 2: Ingineresc / Arhitectural |
|---|---|---|
| **Denumire propusă** | **`familiarity (feeling of knowing)`** | **`familiarity buffer`** |
| **Carte-sursă** | `why_we_forget` | `2504.05840v1` |
| **Slot natural** | **`10_confidence.md`** | **`05_state.md`** |
| **Occurrences reale** | **`3`** | **`6`** |
| **Citat staging** | *"Answer:This often happens because recognition is based on familiarity, which is an instinctive acknowledgment that doesn't necessarily include detailed recollection."* | *"We introduce an additional ‘familiarity’ buffer (light cyan buffer in Figure 4) that employs boosted contrastive learning to prioritize and..."* |
| **Semnificație** | Semnal de încredere / sentiment metacognitiv subiectiv că un stimul a mai fost întâlnit, fără recuperare asociativă de detalii. | Structură de date concretă (buffer circular) ce stochează stările recente și calculează pierderea contrastivă pentru a depista stările rare ce merită consolidate. |

### 2.2. Specificația Exactă a Celor Două Rânduri Rezultate

Pentru momentul în care Owner va decide aplicarea despărțirii, specificăm bit-cu-bit cele două rânduri care vor rezulta în sloturi:

#### Rândul 1 (Înlocuiește rândul vechi din `10_confidence.md`):
```markdown
| familiarity (feeling of knowing) | why_we_forget | 0.95 | proposed | 2026-09-12 | | Answer:This often happens because recognition is based on familiarity, which is an instinctive acknowledgment that doesn't necessarily include detailed recollection. | 3 |
```

#### Rândul 2 (Rând nou adăugat în tabela `05_state.md`):
```markdown
| familiarity buffer | 2504.05840v1 | 0.95 | proposed | 2026-09-12 | | We introduce an additional ‘familiarity’ buffer (light cyan buffer in Figure 4) that employs boosted contrastive learning to prioritize and | 6 |
```

### 2.3. Proprietăți de integritate ale despărțirii propuse:
1. **Conservarea ocurențelor**: $3 + 6 = 9$ (suma celor două componente reface exact cele 9 apariții existente anterior pe discul unificat).
2. **Separarea sloturilor**: Sentimentul de cunoaștere rămâne în `10_confidence.md` (slotul dedicat calibrării încrederii și metacogniției). Structura de memorie circulară trece în `05_state.md` (slotul dedicat bufferelor și registrelor de stare).
3. **Zero coliziuni lexicale**: Căutarea după `familiarity` în unelte va găsi ambele fațete fără ambiguitate lexicală.

---

## 3. Matricea Sintetică a Opțiunilor pentru Owner

| Concept | Slot Curent | Opțiunea Recomandată | Alternativă Plauzibilă | Justificare Sintetică |
|---|---|:---:|:---:|---|
| `symbolic` | `03_ontology.md:40` | **`MERGE_INTO` -> `chunk`** | `DELETE` | În ACT-R componenta simbolică este concretizată prin chunk-uri. |
| `integrated information` | `03_ontology.md:55` | **`DELETE`** | `KEEP_PROPOSED` | Citatul este o simplă intrare bibliografică într-un preprint speculativ. |
| `dual memory` | `03_ontology.md:60` | **`MERGE_INTO` -> `Complementary Learning Systems`** | `DELETE` | Este implementarea computațională a teoriei CLS McClelland. |
| `shared memory` | `04_relationships.md:32` | **`PROMOTE` (în 04_relationships)** | `MERGE_INTO` -> `global workspace` | Definește mecanismul de coordonare a agenților pe același Vault. |
| `in-context learning` | `06_procedures.md:32` | **`DELETE`** | `MERGE_INTO` -> `retrieval-augmented gen.` | Este o capacitate de inferență a LLM-ului, nu o procedură a bazei de memorie. |
| `component process` | `06_procedures.md:49` | **`DELETE`** | `KEEP_PROPOSED` | Hiperonim generic gol de specificitate operațională. |
| `familiarity` | `10_confidence.md:30` | **`SPLIT`** | — | Separare curată: 3 occ în `10_confidence` și 6 occ în `05_state`. |

---

## 4. Concluzie

Cele 7 rânduri nu sunt blocate tehnologic; ele sunt blocate pe delimitări conceptuale. Cu acest dosar complet, Owner deține toate elementele (citate, măsurători de recurență, argumente pro și contra) pentru a decide destinația fiecărui termen dintr-o singură privire.
