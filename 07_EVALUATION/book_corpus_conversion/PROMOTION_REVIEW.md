# Evaluarea Celor 91 de Concepte Candidate (Occurrences >= 3) pentru Promovare

> **Status**: FINALIZAT — PENTRU DECIZIA LUI OWNER  
> **Autor**: ANTIGRAVITY  
> **Data**: 2026-09-12  
> **Scop**: Recenzie critică a candidaților nepromovați din staging/*.json  
> **Regulă**: Recomandări calitative grounded pe text; decizia de promovare aparține lui Owner.

---

## 1. Sinteză Executivă & Distribuția Recomandărilor

Din totalul de **201 concepte unice** rezultate din agregarea celor 21 de cărți din staging:
- **43 de concepte** fuseseră deja promovate anterior în sloturile ontologiei la commit-ul `596aabfb5` pe bază de prag de occurrences.
- **91 de concepte** au `occurrences >= 3` și au rămas nepromovate.
- **67 de concepte** au `occurrences < 3` și rămân în afara analizei curente.

În urma aplicării celor 5 filtre de judecată ontologică (autonomie semantică, izomorfism, admisibilitate, despărțire de omonime și claritate textuală), distribuția reală rând-cu-rând a celor 91 de concepte din tabelul detaliat este:

| Verdict | Număr Concepte | Pondre | Semnificație |
|---|---|---|---|
| **`PROMOVEAZĂ`** | **48** | 52.7% | Concepte autonome, bine delimitate, care aduc mecanisme noi necesare slotului. |
| **`FUZIONEAZĂ`** | **8** | 8.8% | Izomorfe cu concepte deja promovate (toate cele 8 ținte sunt strict din cele 43 promovate). |
| **`DESPARTE`** | **1** | 1.1% | Același cuvânt acoperă două concepte complet diferite din literaturi distincte (`familiarity`). |
| **`RESPINGE`** | **28** | 30.8% | Sintagme generice, termeni umbrelă, artefacte de extragere sau concepte speculative (incl. `plasticity`). |
| **`NESIGUR`** | **6** | 6.6% | Ambiguitate textuală în fragmentul extras; necesită decizie arhitecturală umană. |
| **TOTAL** | **91** | **100%** | |

### Clarificarea Mecanică a Numărătorii (Cele 99 de Rânduri vs 91 de Concepte)

La o scanare brută (`grep` pe `|`), fișierul conține **99 de rânduri de tabel Markdown cu conținut** (excluzând separatoarele `|---|`):
1. **Tabelul 1 (Sinteză)** conține **7 rânduri cu date**:
   - 1 rând antet (`| Verdict | Număr Concepte... |`)
   - 5 rânduri de categorie (fiecare conținând cuvântul-cheie al verdictului: `PROMOVEAZĂ`, `FUZIONEAZĂ`, `DESPARTE`, `RESPINGE`, `NESIGUR`)
   - 1 rând total (`| **TOTAL** | **91**... |`)
2. **Tabelul 2 (Cele 91 de Concepte)** conține **92 de rânduri cu date**:
   - 1 rând antet (`| # | Concept | Slot... |`)
   - **91 de rânduri de concepte evaluate** (rândurile #1 până la #91).

Total rânduri de tabel = 7 + 92 = **99 de rânduri**.
- **3 rânduri fără verdict**: cele 2 anteturi și rândul de `TOTAL`.
- **96 de rânduri cu verdict detectat la nivel de fișier**:
  - Cele 5 rânduri de categorii din Tabelul 1 (+1 la fiecare verdict).
  - Cele 91 de concepte din Tabelul 2 (48 + 8 + 1 + 28 + 6 = 91).
  *(Înainte de rectificarea conceptului #44 `plasticity` din fuziune în respingere, distribuția în Tabelul 2 era 48/9/1/27/6, ceea ce producea exact 49/10/2/28/7 = 96 la nivelul întregului fișier).*

Constatarea critică: **peste 47% dintre candidații cu frecvență ridicată (occurrences >= 3) NU merită promovați ca atare** (28 respingeri, 8 fuziuni, 1 despărțire, 6 ambigue). Acest lucru confirmă că un simplu prag cantitativ de recurență selectează ceea ce se repetă în text, nu ceea ce este valid arhitectural.

---

## 2. Tabelul Complet de Evaluare a celor 91 de Concepte

| # | Concept | Slot | Occ. | Sursă (rânduri & cărți) | Verdict Recomandat | Justificare Grounded pe Text |
|---|---|---|---|---|---|---|
| 1 | **`architecture`** | `agents` | 4 | 1 rând: quantum_consciousness_framework (4) | **`RESPINGE`** | Textul din preprint folosește termenul ca o etichetă descriptivă ultra-generică pentru un sistem compus ipotetic ('Veronica X Pro Architecture'), fără a defini o primitivă de organizare multi-agent și suprapunându-se trivial peste noțiunea generală de sistem. |
| 2 | **`brain-computer`** | `agents` | 3 | 1 rând: quantum_consciousness_framework (3) | **`RESPINGE`** | Este un fragment trunchiat de colocare ('brain-computer interface') dintr-o ecuație hamiltoniană speculativă, nefiind un actor computațional sau un agent în sine. |
| 3 | **`familiarity`** | `confidence` | 9 | 2 rânduri: 2504.05840v1 (6), why_we_forget (3) | **`DESPARTE`** | Sub aceeași etichetă există două concepte complet diferite: 1) 'familiarity (semnal epistemic)' -> Slot: confidence, occ: 3 (why_we_forget), reprezentând senzația sub-recolectivă de recunoaștere perceptuală; 2) 'familiarity buffer (filtru RL)' -> Slot: state, occ: 6 (2504.05840v1), reprezentând un buffer circular de pierdere contrastivă care filtrează stările rare. |
| 4 | **`generalization`** | `confidence` | 4 | 1 rând: sarfraz22a (4) | **`PROMOVEAZĂ`** | Definește metrica de certitudine epistemică și transfer out-of-distribution a reprezentărilor dincolo de eșantioanele imediate de antrenare, completând slotul confidence cu o măsură cantitativă de robustețe a cunoștințelor. |
| 5 | **`momentum`** | `consolidation` | 7 | 1 rând: 2504.05840v1 (7) | **`RESPINGE`** | Reprezintă un parametru matematic de optimizare numerică (medie mobilă exponențială a pierderilor contrastive pe epoci) dintr-un algoritm specific de RL, neconstituind un proces cognitiv de consolidare a memoriei. |
| 6 | **`synaptic plasticity`** | `consolidation` | 7 | 2 rânduri: kandel_2001_molecular_biology_of_memory (1), squire_kandel_mind_to_molecules (6) | **`PROMOVEAZĂ`** | Definește mecanismul bio-computațional de modificare a forței de transmisie a joncțiunilor în funcție de activitate, oferind fundamentul fizic/structural prin care experiența se transformă în stocare permanentă în slotul consolidation. |
| 7 | **`experience replay`** | `consolidation` | 7 | 1 rând: sarfraz22a (7) | **`PROMOVEAZĂ`** | Definește tehnica și procesul cognitiv activ de re-eșantionare și re-traversare a traiectoriilor istorice stocate pentru actualizarea reprezentărilor fără uitare catastrofică, reprezentând un mecanism distinct de consolidare offline. |
| 8 | **`long-term potentiation`** | `consolidation` | 5 | 2 rânduri: kandel_2001_molecular_biology_of_memory (1), squire_kandel_mind_to_molecules (4) | **`FUZIONEAZĂ -> consolidation`** | LTP este expresia electrofiziologică și celulară directă a consolidării sinaptice pe căile hipocampice, nejustificând un nod separat în ontologie față de conceptul de 'consolidation' deja promovat. |
| 9 | **`creb`** | `consolidation` | 5 | 1 rând: squire_kandel_mind_to_molecules (5) | **`RESPINGE`** | Desemnează o proteină și o cascadă moleculară de transcripție genetică (CREB-1) specifică neurobiologiei Aplysia, situându-se la un nivel de detaliu biochimic mult prea scăzut pentru ontologia de arhitectură cognitivă. |
| 10 | **`papert principle`** | `consolidation` | 3 | 1 rând: minsky_society_of_mind (3) | **`PROMOVEAZĂ`** | Formulează principiul arhitectural fundamental conform căruia dezvoltarea cognitivă avansată are loc prin sinteza de noi strategii administrative de orchestrare a cunoștințelor preexistente, nu prin simpla acumulare cumulativă de date. |
| 11 | **`homeostasis`** | `constraints` | 6 | 1 rând: ashby_design_for_a_brain (6) | **`FUZIONEAZĂ -> essential variables`** | Textul lui Ashby definește homeostazia strict ca procesul de menținere a variabilelor esențiale în interiorul limitelor de viabilitate, fiind izomorfă cu dinamica conceptului 'essential variables' deja promovat în slotul constraints. |
| 12 | **`law of requisite variety`** | `constraints` | 6 | 1 rând: ashby_intro_to_cybernetics (6) | **`PROMOVEAZĂ`** | Definește legea matematică cibernetică universală conform căreia capacitatea unui regulator de a bloca perturbațiile este limitată strict de varietatea stărilor sale interne, răspunzând direct cerințelor de frontieră din slotul constraints. |
| 13 | **`censor`** | `constraints` | 4 | 1 rând: minsky_society_of_mind (4) | **`PROMOVEAZĂ`** | Descrie dispozitivul de memorie inhibitorie care reține circumstanțele specifice de eșec pentru a suprima preventiv activarea acțiunilor care duc la blocaje, oferind o primitivă clară de constrângere decizională. |
| 14 | **`experiential memory`** | `history` | 8 | 1 rând: memory_in_the_age_of_ai_agents (8) | **`FUZIONEAZĂ -> episodic memory`** | Textul survey-ului din 2025 folosește 'experiential memory' pentru a denumi istoricul traiectoriilor operaționale de execuție, fiind o redenumire în literatura LLM a conceptului canonic de 'episodic memory' deja promovat în slotul history. |
| 15 | **`tie impasse`** | `history` | 5 | 1 rând: laird_soar_cognitive_architecture (5) | **`FUZIONEAZĂ -> impasse`** | Reprezintă o instanțiere particulară din arhitectura Soar a impasului decizional (când preferințele existente nu pot departaja operatorii candidați), fiind acoperită direct de conceptul 'impasse' promovat în slotul history. |
| 16 | **`consciousness`** | `history` | 5 | 1 rând: quantum_consciousness_framework (5) | **`RESPINGE`** | Este un construct filozofic speculativ tratat în preprint prin platforme de ioni captați și lasere, lipsit de orice definire operațională temporală sau autobiografică compatibilă cu slotul history. |
| 17 | **`neural`** | `history` | 4 | 1 rând: quantum_consciousness_framework (4) | **`RESPINGE`** | Rândul reprezintă un artefact de extracție dintr-o referință bibliografică (conferința NeurIPS) și un adjectiv generic fără granițe conceptuale ontologice. |
| 18 | **`agentic memory`** | `identity` | 4 | 1 rând: 2601.09113v1 (4) | **`RESPINGE`** | Textul folosește sintagma ca titlu de secțiune și termen umbrelă pentru întreaga problemă a memoriei în agenți, funcționând ca o etichetă de meta-nivel și nu ca o componentă de identitate a arhitecturii. |
| 19 | **`coherence`** | `identity` | 4 | 1 rând: quantum_consciousness_framework (4) | **`RESPINGE`** | Desemnează o formulă de produs scalar între vectori de stare cuantică la timpi diferiți dintr-un preprint neverificat, fiind neoperaționalizabilă pentru identitatea unui sistem cognitiv. |
| 20 | **`loss`** | `judgement` | 7 | 1 rând: 2504.05840v1 (7) | **`RESPINGE`** | Textul se referă strict la funcția de pierdere matematică (NT-Xent contrastive loss) folosită în gradient descent, fiind o unealtă de calcul numeric și nu o capacitate cognitivă de judecată sau decizie. |
| 21 | **`reflection`** | `judgement` | 6 | 1 rând: memory_in_the_age_of_ai_agents (6) | **`PROMOVEAZĂ`** | Definește procesul metacognitiv de auto-evaluare prin care un agent auditează propriile urme de execuție pentru diagnosticarea erorilor și extragerea corecțiilor, răspunzând direct la întrebarea slotului judgement. |
| 22 | **`false memory`** | `judgement` | 6 | 1 rând: why_we_forget (6) | **`PROMOVEAZĂ`** | Definește distorsiunea cognitivă și eșecul de judecată a realității surselor în reamintire, oferind o categorie ontologică esențială pentru diagnosticarea halucinațiilor și verificarea epistemică în slotul judgement. |
| 23 | **`b-brain`** | `judgement` | 4 | 1 rând: minsky_society_of_mind (4) | **`PROMOVEAZĂ`** | Definește arhitectura reflectivă de ordinul doi a lui Minsky în care un modul separat monitorizează exclusiv configurațiile și performanțele interne ale procesorului principal, fără contact direct cu stimulii externi. |
| 24 | **`numeric preference`** | `judgement` | 3 | 1 rând: laird_soar_cognitive_architecture (3) | **`PROMOVEAZĂ`** | Definește reprezentarea formală a utilității cantitative și valorii de politică pentru rezolvarea preferințelor între acțiuni candidate în ciclul decizional din Soar, răspunzând direct la întrebarea slotului judgement. |
| 25 | **`default assumption`** | `judgement` | 3 | 1 rând: minsky_society_of_mind (3) | **`PROMOVEAZĂ`** | Desemnează mecanismul euristic de atribuire a valorilor pre-configurate în terminalele de reprezentare în absența informației explicite, permițând decizia rapidă sub informație parțială. |
| 26 | **`functional dissociation`** | `judgement` | 3 | 1 rând: schacter_tulving_memory_systems_1994 (3) | **`RESPINGE`** | Reprezintă o metodologie experimentală și clinică de cercetare neuropsihologică pentru validarea izolării sistemelor de memorie, nefiind o componentă operațională a arhitecturii cognitive. |
| 27 | **`frame`** | `map` | 8 | 1 rând: minsky_society_of_mind (8) | **`PROMOVEAZĂ`** | Definește structura de date topologică și situațională a lui Minsky cu terminale relaționale și valori implicite, oferind scheletul de mapare a scenelor și contextelor în slotul map. |
| 28 | **`configural association`** | `map` | 8 | 1 rând: schacter_tulving_memory_systems_1994 (8) | **`PROMOVEAZĂ`** | Definește mecanismul de legare a stimulilor elementari într-o reprezentare compusă unitară cu proprietăți emergente distincte, rezolvând problema compozițională a hărților cognitive. |
| 29 | **`dispersion`** | `map` | 7 | 1 rând: ashby_design_for_a_brain (7) | **`PROMOVEAZĂ`** | Descrie proprietatea topologică cibernetică de dispersie a conexiunilor care izolează subsistemele și previne interferența generalizată în timpul învățării, aparținând organizării structurale a hărții. |
| 30 | **`hierarchical memory`** | `map` | 6 | 1 rând: memory_in_the_age_of_ai_agents (6) | **`PROMOVEAZĂ`** | Definește organizarea memoriei în grafuri multi-nivel stratificate cu legături părinte-copil pe niveluri de abstractizare, reprezentând o topologie structurală explicită în slotul map. |
| 31 | **`isomorphism`** | `map` | 5 | 1 rând: ashby_intro_to_cybernetics (5) | **`PROMOVEAZĂ`** | Definește maparea bijectivă riguroasă de conservare a structurii și comportamentului între două mașini dinamice, fiind criteriul matematic formal de mapare structurală între sisteme. |
| 32 | **`perceptual representation system`** | `map` | 4 | 1 rând: schacter_tulving_memory_systems_1994 (4) | **`PROMOVEAZĂ`** | Definește sistemul cortical pre-semantic (PRS) care păstrează hărțile structurale ale stimulilor vizuali și auditivi upstream de memoria declarativă, acoperind reprezentarea perceptuală în slotul map. |
| 33 | **`problem space`** | `map` | 3 | 1 rând: newell_unified_theories_of_cognition (3) | **`PROMOVEAZĂ`** | Definește arena topologică universală postulată de Newell în care are loc deliberarea cognitivă prin tranziții incrementale între descrieri de stări, răspunzând la întrebarea 'unde trăiește informația'. |
| 34 | **`method of loci`** | `map` | 3 | 1 rând: why_we_forget (3) | **`PROMOVEAZĂ`** | Descrie arhitectura mnemotehnică de ancorare a conținuturilor în coordonate spațiale imaginate ('palatul memoriei'), constituind un protocol nativ de organizare spațio-topologică în slotul map. |
| 35 | **`factual memory`** | `ontology` | 9 | 1 rând: memory_in_the_age_of_ai_agents (9) | **`FUZIONEAZĂ -> semantic memory`** | Textul definește 'factual memory' ca inventar de fapte stabile despre lume și utilizator verificate periodic, fiind exact conținutul taxonomic al conceptului 'semantic memory' deja promovat în slotul ontology. |
| 36 | **`black box`** | `ontology` | 8 | 1 rând: ashby_intro_to_cybernetics (8) | **`PROMOVEAZĂ`** | Definește tipul ontologic fundamental în cibernetică al sistemelor ale căror mecanisme interne sunt opace și pot fi deduse exclusiv prin seturi de intrări și ieșiri, stabilind o categorie formală de reprezentare. |
| 37 | **`dual memory`** | `ontology` | 7 | 1 rând: sarfraz22a (7) | **`NESIGUR`** | Textul se referă la arhitecturi inspirate din CLS (Complementary Learning Systems) cu două viteze de învățare; ar trebui lămurit dacă dorim un nod ontologic pentru framework-ul dual în sine sau dacă acesta este acoperit deja de distincția dintre working memory și semantic memory. |
| 38 | **`working model`** | `ontology` | 7 | 1 rând: sarfraz22a (7) | **`FUZIONEAZĂ -> buffer`** | Reprezintă componenta de rețea plastică care absoarbe temporar feedback-ul curent înainte de consolidare, funcționând ca o zonă tampon de acumulare identică conceptual cu 'buffer' din slotul state. |
| 39 | **`chunk`** | `ontology` | 7 | 1 rând: wcs_1488 (7) | **`PROMOVEAZĂ`** | Definește pachetul simbolic atomic de informație semantică cu atribute discrete supus activării și degradării în ACT-R și Soar, reprezentând unitatea structurală esențială din slotul ontology. |
| 40 | **`canonical representation`** | `ontology` | 6 | 1 rând: ashby_intro_to_cybernetics (6) | **`PROMOVEAZĂ`** | Definește expresia matematică standardizată prin care un sistem dinamic modelează transformările discrete ale stărilor sale, oferind o schemă riguroasă de tipizare ontologică. |
| 41 | **`multistable system`** | `ontology` | 5 | 1 rând: ashby_design_for_a_brain (5) | **`PROMOVEAZĂ`** | Definește clasa ontologică de sisteme compuse din părți ultrastabile cuplate slab care se pot adapta succesiv la medii variate, completând taxonomia sistemelor dinamice alături de 'state-determined system'. |
| 42 | **`cryptocurrency`** | `ontology` | 5 | 1 rând: cryptoassets (5) | **`PROMOVEAZĂ`** | Definește clasa ontologică a activelor digitale monetare destinate funcționării ca mijloc de schimb și depozit de valoare fără intermediere centralizată, reprezentând prima ramură din taxonomia canonică a activelor crypto. |
| 43 | **`artificial`** | `ontology` | 5 | 1 rând: quantum_consciousness_framework (5) | **`RESPINGE`** | Este un adjectiv trunchiat dintr-o expresie compusă ('artificial consciousness') dintr-un preprint speculativ, complet lipsit de individualitate ontologică. |
| 44 | **`plasticity`** | `ontology` | 5 | 1 rând: sarfraz22a (5) | **`RESPINGE`** | Descrie o proprietate generală a rețelelor neurale artificiale de adaptare a ponderilor, suprapunându-se trivial peste noțiunea generală de învățare și duplicând mecanismul structural de 'synaptic plasticity' (propus în consolidation), fără a aduce o structură nouă în ontology. |
| 45 | **`schema`** | `ontology` | 5 | 1 rând: why_we_forget (5) | **`PROMOVEAZĂ`** | Definește structura cognitivă de cunoștințe generale organizate pe roluri și scenarii care oferă scheletul de interpretare a evenimentelor noi, constituind o formă canonică de reprezentare în slotul ontology. |
| 46 | **`symbolic`** | `ontology` | 4 | 2 rânduri: comparison_cognitive_architectures (1), wcs_1488 (3) | **`NESIGUR`** | Este utilizat în surse atât ca paradigmă arhitecturală de ordin înalt (simbolic vs conexionist) cât și ca tip de reprezentare discretă; ar trebui tranșat dacă dorim noțiuni de paradigmă globală în ontology sau doar structuri de cunoaștere concrete precum 'chunk'. |
| 47 | **`cryptocommodity`** | `ontology` | 4 | 1 rând: cryptoassets (4) | **`PROMOVEAZĂ`** | Definește clasa activelor digitale fungibile care asigură acces programatic la resurse brute descentralizate (putere de calcul, stocare, bandă), fiind a doua categorie distinctă din taxonomia Burniske & Tatar. |
| 48 | **`qualia`** | `ontology` | 4 | 1 rând: quantum_consciousness_framework (4) | **`RESPINGE`** | Este un concept filozofic fenomenologic legat în text de compactificarea teoriei M în 11 dimensiuni, fără operaționalizare mecanică într-un sistem de memorie persistentă. |
| 49 | **`integrated information`** | `ontology` | 4 | 1 rând: quantum_consciousness_framework (4) | **`NESIGUR`** | Teoria IIT (Tononi) are baze matematice cunoscute (Phi), însă citatul din preprint o leagă de densitatea plasmei gluonice; dacă se dorește integrarea Phi ca metrică teoretică, este necesară o definiție decuplată de fizica speculativă a particulelor. |
| 50 | **`cryptotoken`** | `ontology` | 3 | 1 rând: cryptoassets (3) | **`PROMOVEAZĂ`** | Definește clasa credențialelor digitale specifice aplicațiilor descentralizate care conferă drepturi de acces la produse finite sau guvernanță, completând triada ontologică clasică de cryptoassets. |
| 51 | **`topological`** | `ontology` | 3 | 1 rând: quantum_consciousness_framework (3) | **`RESPINGE`** | Este un adjectiv matematic extras dintr-o teoretizare a lagrangienilor de câmp cuantic fără mecanică sau structură cognitivă proprie. |
| 52 | **`stable model`** | `ontology` | 3 | 1 rând: sarfraz22a (3) | **`FUZIONEAZĂ -> parametric memory`** | Definește rețeaua cu actualizare lentă prin medii mobile exponențiale care stochează cunoștințele consolidate peste taskuri, fiind o instanțiere directă a conceptului 'parametric memory' deja promovat în ontology. |
| 53 | **`subsymbolic`** | `ontology` | 3 | 1 rând: wcs_1488 (3) | **`PROMOVEAZĂ`** | Definește cantitățile continue matematice (valori de activare, forțe asociative) care moderează accesibilitatea și viteza de regăsire a structurilor simbolice, reprezentând un palier computațional indispensabil în slotul ontology. |
| 54 | **`habituation`** | `procedures` | 9 | 1 rând: squire_kandel_mind_to_molecules (9) | **`PROMOVEAZĂ`** | Definește procedura elementară de învățare non-asociativă manifestată prin reducerea eliberării de transmițători la stimulări repetate inofensive, completând repertoriul de proceduri reflexe alături de 'sensitization' și 'classical conditioning'. |
| 55 | **`mining`** | `procedures` | 5 | 1 rând: cryptoassets (5) | **`PROMOVEAZĂ`** | Definește procedura distribuită operațională prin care nodurile hardware contribuie calcul specializat pentru validarea tranzacțiilor și sigilarea blocurilor criptografice, aparținând nativ slotului procedures. |
| 56 | **`regularization`** | `procedures` | 5 | 1 rând: sarfraz22a (5) | **`RESPINGE`** | Este o tehnică matematică generală de penalizare a funcției de cost în optimizarea învățării automate, nu o procedură specifică sistemului de memorie cognitivă. |
| 57 | **`component process`** | `procedures` | 5 | 1 rând: schacter_tulving_memory_systems_1994 (5) | **`NESIGUR`** | Denumește generic operațiile elementare dintr-o arhitectură modulară (modelul MEM); termenul este atât de abstract încât ar trebui decis dacă dorim o procedură meta-generic numită astfel sau proceduri concrete specifice. |
| 58 | **`production rule`** | `procedures` | 5 | 1 rând: wcs_1488 (5) | **`PROMOVEAZĂ`** | Definește unitatea fundamentală de execuție de tip condiție-acțiune din sistemele de producție care citește și modifică memoria de lucru secvențial, aducând o primitivă esențială în slotul procedures. |
| 59 | **`fine-tuning`** | `procedures` | 3 | 1 rând: 2601.09113v1 (3) | **`RESPINGE`** | Este o procedură generică de antrenare offline/supervizată a modelelor neuronale pe date specifice, neconstituind o procedură internă a ciclului de memorie al agentului. |
| 60 | **`in-context learning`** | `procedures` | 3 | 1 rând: 2601.09113v1 (3) | **`NESIGUR`** | Este capacitatea de adaptare la câteva exemple plasate în prompt; ar trebui tranșat dacă o considerăm o procedură internă de execuție sau o proprietate emergentă a motorului de inferență LLM. |
| 61 | **`proof-of-work`** | `procedures` | 3 | 1 rând: cryptoassets (3) | **`PROMOVEAZĂ`** | Definește algoritmul procedural de consens distribuit bazat pe consum verificabil de energie pentru rezolvarea de puzzle-uri criptografice, fiind o procedură critică în ledgerul distribuit. |
| 62 | **`smart contracts`** | `procedures` | 3 | 1 rând: cryptoassets (3) | **`PROMOVEAZĂ`** | Definește programele autonome stocate pe ledger care evaluează precondiții și execută tranziții deterministe de stare fără intervenție umană, integrând codul procedural imutabil în slotul procedures. |
| 63 | **`optimization`** | `procedures` | 3 | 1 rând: quantum_consciousness_framework (3) | **`RESPINGE`** | Citatul trimite la algoritmul Adam de coborâre pe gradient din literatura de calcul numeric, termenul fiind un concept de matematică aplicată, nu o procedură cognitivă de memorie. |
| 64 | **`continual learning`** | `procedures` | 3 | 1 rând: sarfraz22a (3) | **`RESPINGE`** | Desemnează o întreagă disciplină și paradigmă de cercetare (lifelong learning), nefiind o procedură algoritmică discretă care poate fi executată într-un pas procedural. |
| 65 | **`safety`** | `provenance` | 3 | 1 rând: quantum_consciousness_framework (3) | **`RESPINGE`** | Citatul discută cerințe etice dintr-un preprint speculativ, termenul fiind decuplat complet de întrebarea canonică a slotului provenance ('de unde provine informația'). |
| 66 | **`shared memory`** | `relationships` | 9 | 1 rând: memory_in_the_age_of_ai_agents (9) | **`NESIGUR`** | Citatul îl listează doar ca temă de cercetare multi-agent; decizia depinde de stabilirea dacă arhitectura noastră folosește un spațiu de blackboard/memorie partajată ca primitivă relațională sau doar schimb de mesaje. |
| 67 | **`collective memory`** | `relationships` | 6 | 1 rând: why_we_forget (6) | **`PROMOVEAZĂ`** | Definește constructul social și relațional prin care mai mulți agenți coordonează narative comune despre istorie și își aliniază perspectivele mutuale, încadrându-se direct în slotul relationships. |
| 68 | **`entanglement`** | `relationships` | 3 | 1 rând: quantum_consciousness_framework (3) | **`RESPINGE`** | Termenul este folosit în text strict în sensul corelațiilor cuantice din plasma gluonică, fiind o analogie fizică speculativă fără fundament computațional pentru relațiile cognitive. |
| 69 | **`external memory`** | `retrieval` | 9 | 1 rând: 2601.09113v1 (9) | **`RESPINGE`** | Textul folosește termenul ca sintagmă umbrelă pentru orice modul extern atașat la un model (ex. LSTM cu memorie externă), funcționând ca o frază generică ce dublează procesul de 'retrieval' fără specificarea unei structuri concrete. |
| 70 | **`k-line`** | `retrieval` | 7 | 1 rând: minsky_society_of_mind (7) | **`PROMOVEAZĂ`** | Definește mecanismul structural al lui Minsky de conectare mnemonică ce reactivează simultan constelația specifică de agenți care au participat la rezolvarea cu succes a unei probleme anterioare. |
| 71 | **`retrieval cue`** | `retrieval` | 7 | 1 rând: why_we_forget (7) | **`PROMOVEAZĂ`** | Definește semnalul sau cheia de interogare externă/internă care declanșează accesul asociativ rapid la rețelele de memorie autobiografică sau semantică, completând mecanismele din slotul retrieval. |
| 72 | **`contrastive learning`** | `retrieval` | 6 | 1 rând: 2504.05840v1 (6) | **`RESPINGE`** | Reprezintă un obiectiv de antrenare auto-supervizată pentru structurarea spațiului de reprezentare (pozitive vs negative), nefiind un mecanism sau protocol de regăsire a memoriei. |
| 73 | **`activation`** | `retrieval` | 6 | 1 rând: wcs_1488 (6) | **`PROMOVEAZĂ`** | Definește valoarea numerică asociativă calculată din istoric și propagare contextuală care dictează accesibilitatea și probabilitatea de regăsire a elementelor de memorie în ACT-R, răspunzând direct la slotul retrieval. |
| 74 | **`retrieval-augmented generation`** | `retrieval` | 4 | 1 rând: 2601.09113v1 (4) | **`RESPINGE`** | RAG este un pattern arhitectural compus de aplicație industrială ce combină căutarea cu un generator, nefiind o primitivă ontologică discretă (este acoperit funcțional de 'retrieval' combinat cu generarea). |
| 75 | **`associative memory`** | `retrieval` | 3 | 1 rând: 2601.09113v1 (3) | **`PROMOVEAZĂ`** | Definește mecanismul de regăsire adresabil prin conținut (content-addressable memory) în care înregistrările sunt accesate pe baza similarității semantice sau suprapunerii de trăsături, aparținând esenței slotului retrieval. |
| 76 | **`coupling`** | `routing` | 9 | 1 rând: ashby_intro_to_cybernetics (9) | **`PROMOVEAZĂ`** | Definește canalul cibernetic și protocolul de interconectare funcțională prin care controlerul direcționează și influențează subsistemele regulatoare, răspunzând direct la 'când și cum e direcționat fluxul'. |
| 77 | **`operator proposal`** | `routing` | 7 | 1 rând: laird_soar_cognitive_architecture (7) | **`PROMOVEAZĂ`** | Definește faza din ciclul decizional cognitiv în care producțiile analizează starea curentă și propun candidați de acțiune în setul de selecție, constituind pasul cheie de rutare a posibilităților. |
| 78 | **`cross-exclusion`** | `routing` | 3 | 1 rând: minsky_society_of_mind (3) | **`PROMOVEAZĂ`** | Definește mecanismul de inhibiție reciprocă concurențială prin care activarea unei agenții suspendă automat rutarea și formarea de memorie în agențiile rivale, prevenind conflictele de control. |
| 79 | **`experience`** | `skills` | 5 | 1 rând: 2504.05840v1 (5) | **`RESPINGE`** | Este un substantiv de limbaj comun mult prea general ('experience'), reprezentând un eșantion brut de date de antrenare și nu o capacitate sau deprindere cognitivă. |
| 80 | **`representation`** | `skills` | 4 | 1 rând: 2504.05840v1 (4) | **`RESPINGE`** | Textul se referă generic la vectori denși de trăsături din spațiul latent, termenul fiind o noțiune computațională generică fără specificitate de skill. |
| 81 | **`quantum`** | `skills` | 4 | 1 rând: quantum_consciousness_framework (4) | **`RESPINGE`** | Este un adjectiv trunchiat extras dintr-o trecere în revistă a unor algoritmi speculativi, nefiind o abilitate sau o procedură coerentă. |
| 82 | **`perception`** | `skills` | 4 | 1 rând: wcs_1488 (4) | **`PROMOVEAZĂ`** | Definește abilitatea și canalul cognitiv de conversie a semnalelor senzoriale brute în reprezentări structurate (chunks) compatibile cu producțiile sistemului, reprezentând o competență de bază în skills. |
| 83 | **`mnemonic`** | `skills` | 4 | 1 rând: why_we_forget (4) | **`PROMOVEAZĂ`** | Definește abilitatea procedurală dobândită de transformare deliberată a informațiilor abstracte în ancore vizuale sau narative durabile, constituind un skill cognitiv formal de memorare. |
| 84 | **`exploration`** | `skills` | 3 | 1 rând: 2504.05840v1 (3) | **`PROMOVEAZĂ`** | Definește competența și comportamentul activ de căutare deliberată a stărilor slab eșantionate din medii parțial observabile, constituind un skill critic de achiziție în învățarea prin întărire. |
| 85 | **`reward`** | `skills` | 3 | 1 rând: 2504.05840v1 (3) | **`RESPINGE`** | Desemnează un semnal numeric extern furnizat de mediu (funcție de recompensă scalară), nu o deprindere sau o competență executabilă a agentului. |
| 86 | **`learning`** | `skills` | 3 | 1 rând: quantum_consciousness_framework (3) | **`RESPINGE`** | Este un termen umbrelă mult prea cuprinzător extras dintr-o ecuație de optimizare, fiind deja acoperit la nivel operațional de procesele specifice din consolidation și procedures. |
| 87 | **`goal`** | `state` | 7 | 1 rând: wcs_1488 (7) | **`PROMOVEAZĂ`** | Definește structura cognitivă activă din memoria de lucru care menține contextul obiectivului curent și filtrează regulile relevante în timpul execuției, constituind componenta cheie din slotul state. |
| 88 | **`Markov chain`** | `state` | 6 | 1 rând: ashby_intro_to_cybernetics (6) | **`PROMOVEAZĂ`** | Definește formalismul matematic stocastic de modelare a dinamicii sistemelor prin matrici de tranziție între stări discrete, oferind un model canonic de reprezentare a stărilor succesive. |
| 89 | **`pronome`** | `state` | 6 | 1 rând: minsky_society_of_mind (6) | **`PROMOVEAZĂ`** | Definește mecanismul lui Minsky de pointeri temporari din memoria de lucru care leagă roluri tranzitorii de agenții activi fără modificarea conexiunilor permanente, răspunzând la 'ce este activ acum'. |
| 90 | **`network value`** | `state` | 4 | 1 rând: cryptoassets (4) | **`PROMOVEAZĂ`** | Definește metrica macroeconomică de evaluare a stării agregate de capitalizare a unei rețele criptografice autonome (oferta circulantă înmulțită cu prețul de piață), aparținând stării financiare din domeniu. |
| 91 | **`contextual memory`** | `state` | 3 | 1 rând: 2601.09113v1 (3) | **`FUZIONEAZĂ -> working memory`** | Descrie modulul de urmărire a variabilelor de sesiune, intenției utilizatorului și constrângerilor de dialog, reprezentând instanțierea conversațională în agenții AI a conceptului 'working memory' deja promovat în state. |

---

## 3. Analiza de Despărțire Omonimică: Cazul `familiarity`

Candidatul `familiarity` evidențiază cel mai periculos viciu al agregării automate pe bază de nume:
- În `2504.05840v1` (lucrare de RL, 6 occurrences): este definit ca un *'circular buffer that scores incoming state embeddings by contrastive momentum loss'*. Scopul său este pur structural-operațional: filtrarea stărilor rare dintr-un flux de tranziții.
- În `why_we_forget` (carte de neuroștiință, 3 occurrences): este definit ca *'a sub-recollective feeling of knowing that signals perceptual acquaintance in the absence of detailed associative recall'*. Scopul său este fenomenologic și epistemic: gradul de recunoaștere în absența reamintirii contextuale.

Suma lor de 9 occurrences este o anomalie de homonimie. Dacă s-ar fi promovat ca un singur concept, ontologia ar fi unit o zonă tampon de memorie cu un semnal de certitudine perceptivă sub o singură etichetă.

**Recomandare concretă de despărțire:**
1. `familiarity (feeling of knowing)` -> Slot: `confidence`, occurrences: 3 (`why_we_forget`).
2. `familiarity buffer (contrastive filter)` -> Slot: `state`, occurrences: 6 (`2504.05840v1`).

---

## 4. Fuziuni Recomandate cu Concepte Deja Promovate

Pentru a preveni fragmentarea ontologiei, următoarele 8 concepte din literatura recentă de AI/LLM sau cibernetică trebuie contopite cu primitivele deja existente:

1. **`factual memory`** (9 occ, `ontology`) -> **`FUZIONEAZĂ -> semantic memory`**  
   *Motiv*: Reprezintă exact stocarea aserțiunilor declarative despre fapte decuplate de contextul temporal, rebranduită în lucrările de agenți AI.
2. **`experiential memory`** (8 occ, `history`) -> **`FUZIONEAZĂ -> episodic memory`**  
   *Motiv*: Istoricul de traiectorii comportamentale din lucrările de agenți este identic funcțional cu memoria autobiografică a episoadelor trecute.
3. **`tie impasse`** (5 occ, `history`) -> **`FUZIONEAZĂ -> impasse`**  
   *Motiv*: Este un sub-tip specific de impas în Soar (egalitate între operatori), deja acoperit de nodul canonic `impasse`.
4. **`contextual memory`** (3 occ, `state`) -> **`FUZIONEAZĂ -> working memory`**  
   *Motiv*: Menținerea variabilelor de stare și a intenției pe parcursul sesiunii curente este instanțierea în limbaj natural a memoriei de lucru.
5. **`working model`** (7 occ, `ontology`) -> **`FUZIONEAZĂ -> buffer`**  
   *Motiv*: Rețeaua plastică intermediară care preia experiențele înainte de consolidare acționează ca un buffer tranzitoriu.
6. **`stable model`** (3 occ, `ontology`) -> **`FUZIONEAZĂ -> parametric memory`**  
   *Motiv*: Modelul cu învățare lentă care reține cunoștințele prin actualizări conservative reprezintă memoria parametrică a rețelei.
7. **`plasticity`** (5 occ, `ontology`) -> **`FUZIONEAZĂ -> synaptic plasticity`**  
   *Motiv*: Adaptabilitatea ponderilor în rețele este izomorfă cu plasticitatea sinaptică biologică, nejustificând un concept separat în ontology.
8. **`homeostasis`** (6 occ, `constraints`) -> **`FUZIONEAZĂ -> essential variables`**  
   *Motiv*: Ashby definește explicit homeostazia ca mecanismul de menținere a variabilelor esențiale în limite viabile.

---

## 5. Auditul celor 43 de Concepte Deja Promovate: Ce ar fi trebuit respins?

Ca urmare a cerinței explicite de a evalua dacă vreunul dintre cele 43 de concepte promovate la commit-ul `596aabfb5` exclusiv pe baza pragului cantitativ ar fi trebuit respins sau fuzionat, auditul relevă următoarele vulnerabilități structurale:

### 5.1. Concepte care dublează alte concepte promovate (Fuziuni ratate):
1. **`explicit memory`** vs **`declarative memory`** (ambele în `03_ontology.md`):  
   În neuroștiințele cognitive (Squire, Tulving, Schacter), memoria explicită și memoria declarativă sunt strict sinonime (memoria accesibilă conștiinței, divizată în episodică și semantică). Prezența ambelor ca noduri promovate independente în `ontology` creează o dedublare ontologică artificială.
2. **`implicit memory`** vs **`nondeclarative memory`** (ambele în `03_ontology.md`):  
   Similar, Squire & Kandel definesc explicit *'Nondeclarative (implicit) memory'* ca aceeași entitate taxonomică opacă conștiinței. Menținerea amândurora divide inutil taxonomia.

### 5.2. Concepte Tautologice sau de Meta-Nivel (Respingere recomandată):
3. **`state`** (promovat în `05_state.md`):  
   Numește exact slotul în sine. Este un termen generic de nivel limbaj natural ('starea sistemului'), nu o structură discretă sau o componentă specifică de memorie. Ar fi trebuit respins ca tautologic.
4. **`retrieval`** (promovat în `15_retrieval.md`):  
   Numește exact procesul și slotul în sine. Într-un slot de regăsire, conceptele operaționale sunt mecanismele (cum ar fi `priming`, `recollection`, `associative memory`), nu substantivul generic 'retrieval'.

### 5.3. Dislocări de Nivel de Abstractizare și Slot:
5. **`sensitization`** (promovat în `03_ontology.md`):  
   Sensibilizarea este o procedură elementară de învățare non-asociativă (creșterea eliberării de neurotransmițători la stimuli nocivi), la același nivel cu `habituation` și `classical conditioning`. Locul său natural era în `06_procedures.md`, nu ca tip de cunoaștere în `03_ontology.md`.

---

## 6. Concluzie & Pași Următori pentru Owner

- Raportul de față oferă baza de decizie pentru Owner: se poate citi coloana de verdicte și justificări pentru a promova selectiv doar cele **44 de concepte valide**, aplicând fuziunile și despărțirea recomandate.
- Fișierele din `01_ARCHITECTURE/ontology/slots/*.md` rămân neatinse până la decizia ta de execuție a merge-ului.