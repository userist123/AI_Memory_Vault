# B06 labelling items (read-only view; enter labels in the CSV)

## H1G|H1-DIR-001|vault-architecture-map-0001  (priority 2, h1_gold_relevance)

* **Context**: [direct_lexical, development] What is the primary information dataflow in the Vault Architecture Map?
* **Evidence**: Vault Architecture & Information Dataflow Map (vault-architecture-map-0001)

> Vault Architecture & Information Dataflow Map 1. Information Flow Pipeline The I Memory Vault strictly separates research, evidence, evaluation, canonical knowledge, dynamic memory, and runtime telemetry: --- 2. Semantic Responsibility Boundaries Layer 1: Governance & Core Invariants ( 00 CORE/ ) Role : Foundational identity, security trust boundaries P0-P18, and confidence models. Mutability : Strict human/admin attestation required. Layer 2: Canonical Knowledge ( 01 KNOWLEDGE/ ) Role : Verified domain architectures, research findings, and protocols. Mutability : AI proposed into REVIEW; human promotes to ACTIVE. Layer 3: System Blueprints & Projects ( 02 PROJECTS/ ) Role : Engineering proj

* **Question**: Reading only the note text below, does this note answer the query? yes / partial / no. In 'other_relevant_ids' list the id of any OTHER note in the vault that answers it better or equally well (optional).
* **Allowed labels**: `yes|partial|no`

## H1G|H1-DIR-002|0c4c8b76-85c4-4fde-a14a-4bde0b840009  (priority 2, h1_gold_relevance)

* **Context**: [direct_lexical, development] What procedure governs Git backup restore and rollback operations in the vault?
* **Evidence**: Git, Backup, Restore, and Rollback (0c4c8b76-85c4-4fde-a14a-4bde0b840009)

> Git, Backup, Restore, and Rollback Purpose Preserve recoverable history while protecting raw provenance. Procedure 1. Inspect the target files and run the Integrity Check before a material change. 2. Review the change set; never include credentials or other secrets. 3. Commit coherent, validated changes with a descriptive message when Git is available. 4. Keep an independent backup of the vault, including 06 INBOX/RAW IMPORTS/ , before a migration or mass modification. 5. Restore by copying from a verified backup or by reverting a reviewed commit; verify raw evidence paths and canonical links after restore. 6. Roll back by creating a corrective commit or restoring selected files. Do not dele

* **Question**: Reading only the note text below, does this note answer the query? yes / partial / no. In 'other_relevant_ids' list the id of any OTHER note in the vault that answers it better or equally well (optional).
* **Allowed labels**: `yes|partial|no`

## H1G|H1-DIR-003|0c4c8b76-85c4-4fde-a14a-4bde0b840010  (priority 2, h1_gold_relevance)

* **Context**: [direct_lexical, development] What are the rules and procedure for import sanitization?
* **Evidence**: Import Sanitization (0c4c8b76-85c4-4fde-a14a-4bde0b840010)

> Import Sanitization Purpose Prepare an external-memory derivative without changing the original evidence. Procedure 1. Preserve the unmodified export permanently in 06 INBOX/RAW IMPORTS/ . 2. Create a derivative outside RAW IMPORTS/ ; record its source in provenance.source ref and provenance.original path . 3. Remove conversational noise, separate atomic concepts, classify the candidate, and assign provisional confidence and verification. 4. Redact credentials, secrets, and unnecessary personal data from the derivative only; record provenance.redaction: applied when used. 5. Check duplicates, contradictions, frontmatter, and links using Integrity Check. 6. Move only the derivative through RA

* **Question**: Reading only the note text below, does this note answer the query? yes / partial / no. In 'other_relevant_ids' list the id of any OTHER note in the vault that answers it better or equally well (optional).
* **Allowed labels**: `yes|partial|no`

## H1G|H1-DIR-004|knw-retrieval-bottleneck-p0-0001  (priority 2, h1_gold_relevance)

* **Context**: [direct_lexical, development] What are the P0 empirical findings regarding the retrieval bottleneck?
* **Evidence**: Retrieval Bottleneck — P0 Empirical Findings & Architectural Specification (knw-retrieval-bottleneck-p0-0001)

> Retrieval Bottleneck — P0 Empirical Findings & Architectural Specification This knowledge document formalizes the empirical findings, mathematical measurements, failure modes, and architectural implications derived from the P0 Real Pipeline Diagnostic Experiment executed against the AI Memory Vault. --- 1. Executive Summary Empirical measurement of the real production pipeline ( MemoryController.search $\rightarrow$ QueryClassifier $\rightarrow$ RetrievalEngine $\rightarrow$ RelevanceScorer $\rightarrow$ ProgressiveDisclosure $\rightarrow$ ContextPackBuilder ) revealed that the primary accuracy bottleneck in sparse-context agentic execution is Candidate Discovery / Retrieval Composition , ra

* **Question**: Reading only the note text below, does this note answer the query? yes / partial / no. In 'other_relevant_ids' list the id of any OTHER note in the vault that answers it better or equally well (optional).
* **Allowed labels**: `yes|partial|no`

## H1G|H1-DIR-005|knw-context-packing-p1-0001  (priority 2, h1_gold_relevance)

* **Context**: [direct_lexical, calibration] What is the P1 architectural specification for context packing?
* **Evidence**: Context Packing P1 Empirical Findings & Architectural Specification (knw-context-packing-p1-0001)

> Context Packing P1 Empirical Findings & Architectural Specification This knowledge document formalizes the empirical findings, measurements, failure modes, and architectural implications derived from the P1 Context Packing Laboratory executed over real AI Memory Vault data. --- 1. Executive Summary Empirical measurement of the retrieval-packing pipeline isolated PACKING FAILURE as the primary bottleneck preventing high candidate recall ($76.7\%$) from reaching generative LLM accuracy. Under the production baseline ( P0 ): - Packing Loss Rate : 76.1% of all discovered factual keywords were discarded or corrupted before reaching the LLM context. - Root causes in production ContextPackBuilder :

* **Question**: Reading only the note text below, does this note answer the query? yes / partial / no. In 'other_relevant_ids' list the id of any OTHER note in the vault that answers it better or equally well (optional).
* **Allowed labels**: `yes|partial|no`

## H1G|H1-DIR-006|proc-brain-arch-0001  (priority 2, h1_gold_relevance)

* **Context**: [direct_lexical, calibration] What is the specification for the bio-inspired brain core cognitive architecture?
* **Evidence**: 🧠 Specificație Canonică: Arhitectura Cognitivă Bio-Inspirată (v4.5.0 Brain Core) (proc-brain-arch-0001)

> 🧠 Specificație Canonică: Arhitectura Cognitivă Bio-Inspirată (v4.5.0 Brain Core) Acest document specifică arhitectura cognitivă bio-inspirată integrată în AI Memory Vault CODEX READY . --- 🏛️ 1. Cele 5 Module Cognitive ale Creierului AI A. Motorul de Decădere a Activării ACT-R ( cognitive core/activation.py ) - Model Matematic : $B i = \ln\left(\sum {j=1}^n (t - t j)^{-d}\right)$ cu rata de decădere $d = 0.5$. - Mecanism : Notele neaccesate pierd din activare și trec în stare DORMANT THRESHOLD (-2.0), eliberând memoria de lucru. Notele reaccesate își cresc activarea în timp real. B. Reconsolidarea Plastice a Memoriei ( cognitive core/consolidation.py ) - Mecanism : Când VerifierAgent sau Cri

* **Question**: Reading only the note text below, does this note answer the query? yes / partial / no. In 'other_relevant_ids' list the id of any OTHER note in the vault that answers it better or equally well (optional).
* **Allowed labels**: `yes|partial|no`

## H1G|H1-DIR-007|slot-01-identity  (priority 1, h1_gold_relevance)

* **Context**: [direct_lexical, held_out] What role does the identity slot serve in the vault ontology?
* **Evidence**: Ontology Slot: Identity (slot-01-identity)

> Ontology Slot: Identity Question What is this system and what is it not? Theoretical sources - None (design decision, not literature) Validates module none Candidate concepts concept source book confidence status date added promoted note id evidence occurrences --- --- --- --- --- --- --- --- cognitive architecture 7688 jkt au; comparison cognitive architectures; laird soar cognitive architecture; newell unified theories of cognition; wcs 1488 0.90 promoted 2026-09-12 5ed5f5b1-a936-4b51-a4ef-c9742d14730c A secondary goal is to provide an example of how we think a cognitive architecture should be described and evaluated, which includes proposing requirements for general cognitive architecture

* **Question**: Reading only the note text below, does this note answer the query? yes / partial / no. In 'other_relevant_ids' list the id of any OTHER note in the vault that answers it better or equally well (optional).
* **Allowed labels**: `yes|partial|no`

## H1G|H1-DIR-008|slot-15-retrieval  (priority 1, h1_gold_relevance)

* **Context**: [direct_lexical, held_out] What are the retrieval slot definitions and specifications?
* **Evidence**: Ontology Slot: Retrieval (slot-15-retrieval)

> Ontology Slot: Retrieval Question How is relevant memory found? Theoretical sources - None (grounded in real code, not primarily literature) Validates module - 03 IMPLEMENTATION/packages/memory/controller.py (verified definition of MemoryController.search() at line 376; distinct from the 03 IMPLEMENTATION/packages/memory controller/ compatibility shim) Candidate concepts concept source book confidence status date added promoted note id evidence occurrences --- --- --- --- --- --- --- --- retrieval 2601.09113v1; memory in the age of ai agents; wcs 1488 0.95 promoted 2026-09-12 9e6da9df-7fca-4cf5-99bf-03fbdee15170 related concepts such as LLM memory, retrieval-augmented generation (RAG), and c

* **Question**: Reading only the note text below, does this note answer the query? yes / partial / no. In 'other_relevant_ids' list the id of any OTHER note in the vault that answers it better or equally well (optional).
* **Allowed labels**: `yes|partial|no`

## H1G|H1-DIR-009|idx-leg-eu-aiact-2024-1689  (priority 1, h1_gold_relevance)

* **Context**: [direct_lexical, held_out] Where is the structured index for the EU AI Act 2024/1689?
* **Evidence**: Index Structurat: Regulamentul (UE) 2024/1689 (AI Act) (idx-leg-eu-aiact-2024-1689)

> Index Structurat: Regulamentul (UE) 2024/1689 (AI Act) [!IMPORTANT] REGIM DE GUVERNANȚĂ & DISCLAIMER JURIDIC : Acest index este un instrument analitic structurat derivat din date normative externe ( instruction trust: NONE ). Nu reprezintă asistență juridică, consultanță sau politică activă. Statut: lifecycle: REVIEW / verification: verified source . Nu este promovat în ACTIVE fără aprobare umană prealabilă. 1. Definiții Cheie (Art. 3) - Sistem de IA (Art. 3 pct. 1) : Sistem bazat pe mașină conceput să funcționeze cu diferite niveluri de autonomie și care poate genera rezultate precum previziuni, recomandări sau decizii. - Model de IA de uz general — GPAI (Art. 3 pct. 63) : Model de IA cu ca

* **Question**: Reading only the note text below, does this note answer the query? yes / partial / no. In 'other_relevant_ids' list the id of any OTHER note in the vault that answers it better or equally well (optional).
* **Allowed labels**: `yes|partial|no`

## H1G|H1-DIR-010|f82194d2-31a8-4c91-a83d-e42109ab7d12  (priority 1, h1_gold_relevance)

* **Context**: [direct_lexical, held_out] What are the clean architecture principles for the XAU_Kinetic trading bot?
* **Evidence**: XAU_Kinetic Clean Architecture Principles (f82194d2-31a8-4c91-a83d-e42109ab7d12)

> XAU Kinetic Clean Architecture Principles Overview XAU Kinetic is a institutional quantitative trading engine built with Clean Architecture in Python 3.12+. It separates concerns strictly between core domain entities, application orchestration, infrastructure drivers, risk management, and trading strategy decisions. Core Architectural Invariants 1. Zero-Trust Data Flow (Pydantic V2) All inputs and outputs crossing architectural boundaries use strict Pydantic V2 schemas ( SignalObject , Position , TickData , AccountInfo , OrderResult , AuditEvent ). No raw dictionaries or unvalidated floating point values are passed between strategy and risk modules. 2. Isolated Risk Veto Power RiskManager ac

* **Question**: Reading only the note text below, does this note answer the query? yes / partial / no. In 'other_relevant_ids' list the id of any OTHER note in the vault that answers it better or equally well (optional).
* **Allowed labels**: `yes|partial|no`

## H1G|H1-PAR-001|vault-master-index-0001  (priority 2, h1_gold_relevance)

* **Context**: [paraphrase, development] Where can I locate the central catalog and navigation overview of all vault notes?
* **Evidence**: Master Vault Navigational Index (vault-master-index-0001)

> Master Vault Navigational Index This index provides canonical, structured navigation across all verified knowledge domains, project architectures, standard procedures, dynamic memory logs, system contracts, evaluation labs, and runtime telemetry in the AI Memory Vault. --- 1. Core Architecture & Governance ( 00 CORE/ ) Identity & Invariants : 00 CORE/Identity — AI Assistant identity, core mandates, and operating principles. Operational Rules : 00 CORE/Rules — Inviolable system operating rules and constraints. Strategic Goals : 00 CORE/Goals — Long-term operational objectives. System Architecture : 00 CORE/System Architecture — 5-layer system architecture overview. Memory Protocol : 00 CORE/M

* **Question**: Reading only the note text below, does this note answer the query? yes / partial / no. In 'other_relevant_ids' list the id of any OTHER note in the vault that answers it better or equally well (optional).
* **Allowed labels**: `yes|partial|no`

## H1G|H1-PAR-002|11e9fe55-6924-4231-9635-c5629bff6ae3  (priority 2, h1_gold_relevance)

* **Context**: [paraphrase, development] How does permanent knowledge retention operate after synaptic consolidation?
* **Evidence**: long-term memory (11e9fe55-6924-4231-9635-c5629bff6ae3)

> long-term memory Canonical Definition The externalized store containing factual archives and operational manuals that guide agent execution without consuming continuous attention. Judgment & Evaluation Tier 2 cognitive concept with recurrence occurrences 10-19 across memory in the age of ai agents. Promoted to REVIEW status for ontology scaffolding. Code Cross-References Potentially relevant to implementation modules in 03 IMPLEMENTATION/ or procedures in 10 DOCUMENTATION/procedures/ , not independently verified this package.

* **Question**: Reading only the note text below, does this note answer the query? yes / partial / no. In 'other_relevant_ids' list the id of any OTHER note in the vault that answers it better or equally well (optional).
* **Allowed labels**: `yes|partial|no`

## H1G|H1-PAR-003|e045ded5-01fd-4a28-8f85-203ebae670de  (priority 2, h1_gold_relevance)

* **Context**: [paraphrase, development] Which memory subsystem explicitly stores facts and verbalizable episodic occurrences?
* **Evidence**: declarative memory (e045ded5-01fd-4a28-8f85-203ebae670de)

> declarative memory Canonical Definition A flexible cognitive retention system characterized by enabling novel inferential derivations and contextual problem-solving beyond rigid associative routines. Judgment & Evaluation Core cognitive architecture concept recurrent across schacter tulving memory systems 1994 and canonical literature. Promoted to REVIEW status for ontology grounding. Code Cross-References Potentially relevant to implementation modules in 03 IMPLEMENTATION/ or procedures in 10 DOCUMENTATION/procedures/ , not independently verified this package.

* **Question**: Reading only the note text below, does this note answer the query? yes / partial / no. In 'other_relevant_ids' list the id of any OTHER note in the vault that answers it better or equally well (optional).
* **Allowed labels**: `yes|partial|no`

## H1G|H1-PAR-004|e66a94e4-28b6-47d6-90b3-358cd6dcd743  (priority 2, h1_gold_relevance)

* **Context**: [paraphrase, development] What memory storage remains inactive and unexpressed until triggered by retrieval cues?
* **Evidence**: latent memory (e66a94e4-28b6-47d6-90b3-358cd6dcd743)

> latent memory Canonical Definition An intermediate representation modality storing compressed historical context within transformer activation spaces rather than plaintext. Judgment & Evaluation Tier 2 cognitive concept with recurrence occurrences 10-19 across memory in the age of ai agents. Promoted to REVIEW status for ontology scaffolding. Code Cross-References Potentially relevant to implementation modules in 03 IMPLEMENTATION/ or procedures in 10 DOCUMENTATION/procedures/ , not independently verified this package.

* **Question**: Reading only the note text below, does this note answer the query? yes / partial / no. In 'other_relevant_ids' list the id of any OTHER note in the vault that answers it better or equally well (optional).
* **Allowed labels**: `yes|partial|no`

## H1G|H1-PAR-005|knw-temporal-memory-p2-0001  (priority 2, h1_gold_relevance)

* **Context**: [paraphrase, calibration] What guidelines govern tracking changes over time and temporal decay of memories?
* **Evidence**: Temporal Memory P2 Empirical Findings & Architectural Specification (knw-temporal-memory-p2-0001)

> Temporal Memory P2 Empirical Findings & Architectural Specification This knowledge note documents the empirical findings, metadata audits, and experimental evaluations from the P2 Temporal Memory Laboratory executed over real AI Memory Vault data. --- 1. Executive Summary & Temporal Model Audit A comprehensive audit across all 832 Markdown notes in the AI Memory Vault determined: Metadata Field Present Notes Percentage Availability Status --- ---: ---: :---: created 832 100.0% AVAILABLE (RFC 3339 / ISO Date) updated 832 100.0% AVAILABLE (ISO Date) lifecycle 832 100.0% AVAILABLE ( ACTIVE , REVIEW , RAW , etc.) valid from 0 0.0% MISSING in static disk notes valid until 0 0.0% MISSING in static

* **Question**: Reading only the note text below, does this note answer the query? yes / partial / no. In 'other_relevant_ids' list the id of any OTHER note in the vault that answers it better or equally well (optional).
* **Allowed labels**: `yes|partial|no`

## H1G|H1-PAR-006|proc-reflexion-0001  (priority 2, h1_gold_relevance)

* **Context**: [paraphrase, calibration] How do agents perform iterative critique and self-correction across closed loops?
* **Evidence**: 🔄 Procedură Canonică: Pipeline de Reflecție Închisă (Closed-Loop Reflexion) (proc-reflexion-0001)

> 🔄 Procedură Canonică: Pipeline de Reflecție Închisă (Closed-Loop Reflexion) Această procedură definește ciclul automat de reflecție închisă ( Generate → Critique → Revise → Consolidate ) pentru conversia erorilor runtime în lecții și proceduri reutilizabile. --- 🔁 1. Ciclul de Reflecție în 4 Etape Etapa A: Captura Erorii - Când un task întâmpină un eșec sau o aserțiune picată, se creează o notă în 04 MEMORY/Errors/ cu type: error , lifecycle: REVIEW , log-ul complet nealterat și atribuirea cauzei rădăcină. Etapa B: Critica SelfRefine ( cognitive core/reflection.py ) - CriticAgent și VerifierAgent analizează traiectul eșecului prin masca de reguli SelfRefine : 1. Identificarea cauzei rădăcină

* **Question**: Reading only the note text below, does this note answer the query? yes / partial / no. In 'other_relevant_ids' list the id of any OTHER note in the vault that answers it better or equally well (optional).
* **Allowed labels**: `yes|partial|no`

## H1G|H1-PAR-007|proc-stats-reporting-0001  (priority 1, h1_gold_relevance)

* **Context**: [paraphrase, held_out] What methodology is required when reporting proportions from limited observation samples in evaluations?
* **Evidence**: 📊 Procedură Canonică: Raportarea Proporțiilor pe Eșantioane Mici în Evaluări și Benchmark-uri (proc-stats-reporting-0001)

> 📊 Procedură Canonică: Raportarea Proporțiilor pe Eșantioane Mici în Evaluări și Benchmark-uri Această procedură stabilește standardul canonic obligatoriu pentru raportarea proporțiilor, ratelor de succes și transferului cognitiv în rapoartele de evaluare și benchmark ale AI Memory Vault. Standardul derivă direct din principiile matematice de inferență statistică predate în OpenStax Introductory Statistics 2e , Capitolul 8 ( Confidence Intervals ). --- 🎯 1. Principiu de Bază O proporție empirică obținută pe un eșantion finit (ex: $\hat{p} = 6/12 = 50\%$) este o estimare punctuală , nu un parametru fix. Fără precizarea dimensiunii eșantionului $n$ și a intervalului de încredere asociat, orice

* **Question**: Reading only the note text below, does this note answer the query? yes / partial / no. In 'other_relevant_ids' list the id of any OTHER note in the vault that answers it better or equally well (optional).
* **Allowed labels**: `yes|partial|no`

## H1G|H1-PAR-008|slot-06-procedures  (priority 1, h1_gold_relevance)

* **Context**: [paraphrase, held_out] Which ontology section codifies executable operational runbooks and workflows?
* **Evidence**: Ontology Slot: Procedures (slot-06-procedures)

> Ontology Slot: Procedures Question How are operations executed? Theoretical sources - Anderson, How Can the Human Mind Occur in the Physical Universe (ACT-R) Validates module - 10 DOCUMENTATION/procedures/Compiling A Request Into A Brief.md - 10 DOCUMENTATION/procedures/Recording A Solved Problem.md Candidate concepts concept source book confidence status date added promoted note id evidence occurrences --- --- --- --- --- --- --- --- in-context learning 2601.09113v1 0.95 proposed 2026-09-12 akraborty. Language models can exploit cross-task in-context learning for data-scarce novel tasks. arXiv preprint arXiv:2405.10548, 2024. Subhajit Chaudhury, Payel Das, Sarathkrishna Swaminathan, Geor 3

* **Question**: Reading only the note text below, does this note answer the query? yes / partial / no. In 'other_relevant_ids' list the id of any OTHER note in the vault that answers it better or equally well (optional).
* **Allowed labels**: `yes|partial|no`

## H1G|H1-PAR-009|idx-leg-eu-gdpr-2016-679  (priority 1, h1_gold_relevance)

* **Context**: [paraphrase, held_out] Where is the comprehensive index for European data privacy regulation 2016/679?
* **Evidence**: Index Structurat: Regulamentul (UE) 2016/679 (GDPR) (idx-leg-eu-gdpr-2016-679)

> Index Structurat: Regulamentul (UE) 2016/679 (GDPR) [!IMPORTANT] REGIM DE GUVERNANȚĂ & DISCLAIMER JURIDIC : Acest index este un instrument analitic structurat derivat din date normative externe ( instruction trust: NONE ). Nu reprezintă asistență juridică, consultanță sau politică activă. Statut: lifecycle: REVIEW / verification: verified source . Nu este promovat în ACTIVE fără aprobare umană prealabilă. 1. Definiții Cheie (Art. 4) - Date cu caracter personal (Art. 4 pct. 1) : Orice informații privind o persoană fizică identificată sau identificabilă. - Prelucrare (Art. 4 pct. 2) : Orice operațiune efectuată asupra datelor (colectare, înregistrare, stocare, modificare, consultare, ștergere)

* **Question**: Reading only the note text below, does this note answer the query? yes / partial / no. In 'other_relevant_ids' list the id of any OTHER note in the vault that answers it better or equally well (optional).
* **Allowed labels**: `yes|partial|no`

## H1G|H1-PAR-010|0e95a2f1-b9f4-4f0a-81b4-17625f8034a6  (priority 1, h1_gold_relevance)

* **Context**: [paraphrase, held_out] Why was the MetaTrader platform paired with Python and desktop GUI selected for live execution?
* **Evidence**: Use MetaTrader 5 + Python + Tkinter for the Live Trading Application (0e95a2f1-b9f4-4f0a-81b4-17625f8034a6)

> Use MetaTrader 5 + Python + Tkinter for the Live Trading Application Decision Aplicatia de tranzactionare live va fi construita in Python 3.12 cu MetaTrader5 pentru executie si Tkinter pentru UI, nu PySide6, pentru compatibilitate maxima cu setup-ul existent. Context Utilizatorul avea deja un bot de backtest MT5 in Python si a cerut trecerea la o aplicatie completa de tranzactionare live, multi-entry, BUY si SELL, cu SL/TP automat si filtru de stiri. Problem Alegerea framework-ului UI (Tkinter vs PySide6) si a arhitecturii pentru trecerea de la backtest la executie live. Options Option A: Tkinter Pros: - Compatibilitate maxima cu codul si mediul existent al utilizatorului. - Fara dependinte

* **Question**: Reading only the note text below, does this note answer the query? yes / partial / no. In 'other_relevant_ids' list the id of any OTHER note in the vault that answers it better or equally well (optional).
* **Allowed labels**: `yes|partial|no`

## H1G|H1-IND-001|fd63129c-091c-4171-9eb4-b7c28190b1a4  (priority 2, h1_gold_relevance)

* **Context**: [indirect_cue, development] Why might two valid bug fixes cancel each other out while all CI checks remain green?
* **Evidence**: Two independently correct changes can cancel each other and report success (fd63129c-091c-4171-9eb4-b7c28190b1a4)

> Two independently correct changes can cancel each other and report success Problem Graph expansion ran on every query, reported status ok , and added exactly zero nodes. No test failed, no error appeared, and the graph-on arm was silently identical to the baseline. How it was found The expansion budget was min(2 len(notes), 20) - len(notes) . A separate, correct change had raised the lexical candidate limit from a small number to 200, which makes that expression negative. Every test still passed because the tests use small mock corpora where the formula behaves. The contradiction that exposed it was a metric that cannot happen: context recall above candidate recall, meaning a note reached th

* **Question**: Reading only the note text below, does this note answer the query? yes / partial / no. In 'other_relevant_ids' list the id of any OTHER note in the vault that answers it better or equally well (optional).
* **Allowed labels**: `yes|partial|no`

## H1G|H1-IND-002|123fe8df-2490-44f0-b695-143c3d5a997f  (priority 2, h1_gold_relevance)

* **Context**: [indirect_cue, development] What happens when an automated safeguard triggers due to a false premise?
* **Evidence**: A guard that fires for the wrong reason hides the defect it was protecting (123fe8df-2490-44f0-b695-143c3d5a997f)

> A guard that fires for the wrong reason hides the defect it was protecting Problem The held-out benchmark refused to run: FROZEN SET HASH MISMATCH . The obvious readings were tampering or a bad freeze. Both were wrong, and the real defect was far larger. How it was found The recorded hash matched the file's content once CRLF was normalised to LF, and git reported the file unchanged. So the set was intact and the guard was hashing raw bytes on a Windows checkout — a platform-dependent false alarm that would pass on Linux CI. Because the guard aborted at the second line, nobody had ever run the benchmark to completion. Doing so revealed that all 48 cases referenced two gold notes that do not e

* **Question**: Reading only the note text below, does this note answer the query? yes / partial / no. In 'other_relevant_ids' list the id of any OTHER note in the vault that answers it better or equally well (optional).
* **Allowed labels**: `yes|partial|no`

## H1G|H1-IND-003|f10029dc-e28e-4710-bf3a-b2b48bfb84dc  (priority 2, h1_gold_relevance)

* **Context**: [indirect_cue, development] What language convention must be enforced across all prompt instructions sent to LLMs?
* **Evidence**: Every AI-facing prompt is written in English, in full (f10029dc-e28e-4710-bf3a-b2b48bfb84dc)

> Every AI-facing prompt is written in English, in full Problem Requests arrive as one informal line in Romanian. What reached other agents was sometimes that same line, so the receiving agent began by reconstructing intent, context and constraints that the sender already knew. How it was found Stated directly by the user, generalising an existing narrower preference: Trading Bot Prompt Language English recorded the same rule but scoped only to trading-bot prompts. The rule is not domain-specific. What fixed it Replies to the user stay in Romanian. Everything transmitted to another agent is English and complete: verified context, task, requirements, what is forbidden, the traps already paid fo

* **Question**: Reading only the note text below, does this note answer the query? yes / partial / no. In 'other_relevant_ids' list the id of any OTHER note in the vault that answers it better or equally well (optional).
* **Allowed labels**: `yes|partial|no`

## H1G|H1-IND-004|0d0d9f30-fa32-416f-95e3-0149006dae79  (priority 2, h1_gold_relevance)

* **Context**: [indirect_cue, development] Why should repository developers avoid packing everything into a single monolithic script?
* **Evidence**: Prefer Multi-File Project Structure Over Single-File Scripts (0d0d9f30-fa32-416f-95e3-0149006dae79)

> Prefer Multi-File Project Structure Over Single-File Scripts Preference Utilizatorul prefera aplicatii Python structurate in mai multe fisiere/module (ex. main.py, config.py, core/, ui/) in locul unui singur script monolitic. Context Aplicabil la proiectele de trading bots (MT5, Tkinter) si alte aplicatii desktop/backend Python. Why Structura multi-file este mai usor de mentinut, testat si extins pentru aplicatii de productie complexe (risc, executie, UI separate). What To Prefer - Separare clara: config, core logic, UI, backtest. - Module reutilizabile (broker.py, indicators.py, strategy.py, risk.py). What To Avoid - Livrarea unui singur fisier gigant "Everything I Know About X.py". Flexibi

* **Question**: Reading only the note text below, does this note answer the query? yes / partial / no. In 'other_relevant_ids' list the id of any OTHER note in the vault that answers it better or equally well (optional).
* **Allowed labels**: `yes|partial|no`

## H1G|H1-IND-005|vault-memory-mesh-architecture-0001  (priority 2, h1_gold_relevance)

* **Context**: [indirect_cue, calibration] How does the distributed associative interconnect link disparate cognitive modules across nodes?
* **Evidence**: Vault Memory Mesh Architecture (vault-memory-mesh-architecture-0001)

> Vault Memory Mesh Architecture 1. Purpose The Cognitive Memory Mesh establishes a formal, machine-readable semantic mesh across all canonical knowledge objects, episodic memories, skills, procedures, agents, experiments, empirical evidences, runtime telemetry traces, and audit logs in the AI Memory Vault . It bridges unstructured Markdown notes with deterministic, graph-theoretic discovery without altering production runtime retrieval or packing algorithms. --- 2. Canonical Object Taxonomy The mesh defines a strict, non-overlapping 11-type taxonomy: Object Type Description Primary Location Allowed Outgoing Relations --- --- --- --- KNOWLEDGE Canonical, verified domain facts and architectural

* **Question**: Reading only the note text below, does this note answer the query? yes / partial / no. In 'other_relevant_ids' list the id of any OTHER note in the vault that answers it better or equally well (optional).
* **Allowed labels**: `yes|partial|no`

## H1G|H1-IND-006|knw-agent-memory-trace-protocol-0001  (priority 2, h1_gold_relevance)

* **Context**: [indirect_cue, calibration] How should audit events during cognitive retrieval be emitted in structured logs?
* **Evidence**: Agent Memory Trace Emitter Protocol Specification (knw-agent-memory-trace-protocol-0001)

> Agent Memory Trace Emitter Protocol Specification 1. Purpose & Scope This protocol establishes the canonical, machine-readable standard for emitting and validating Agent Memory Traces across all AI development sessions and autonomous subagents. The protocol ensures that claimed memory utilization is backed by verifiable execution evidence, enforcing the fundamental axiom: $$\mathbf{DECLARED \neq OBSERVED}$$ --- 2. Declared vs Observed Model 1. Declared State ( declared ) : What the agent asserts in natural language, summaries, or self-reports. 2. Observed State ( observed ) : Concrete events recorded by tool call hooks, filesystem access logs, database queries, subagent dispatches, pytest ou

* **Question**: Reading only the note text below, does this note answer the query? yes / partial / no. In 'other_relevant_ids' list the id of any OTHER note in the vault that answers it better or equally well (optional).
* **Allowed labels**: `yes|partial|no`

## H1G|H1-IND-007|slot-16-consolidation  (priority 1, h1_gold_relevance)

* **Context**: [indirect_cue, held_out] Where are memories stabilized, compacted, and merged from short-term buffers?
* **Evidence**: Ontology Slot: Consolidation (slot-16-consolidation)

> Ontology Slot: Consolidation Question How does experience become knowledge? Theoretical sources - McClelland, McNaughton & O'Reilly (1995), Complementary Learning Systems - Kumaran & Hassabis (2016), Weight-based plasticity and memory consolidation in artificial agents Validates module - 03 IMPLEMENTATION/packages/graph/plasticity.py (attribution-aware plasticity engine, bounded asymptotic weight updates, and telemetry journal rollback) - 03 IMPLEMENTATION/packages/graph/synapse store.py (verified location defining decay unused() at line 414 and prune() at line 426) Candidate concepts concept source book confidence status date added promoted note id evidence occurrences --- --- --- --- --- -

* **Question**: Reading only the note text below, does this note answer the query? yes / partial / no. In 'other_relevant_ids' list the id of any OTHER note in the vault that answers it better or equally well (optional).
* **Allowed labels**: `yes|partial|no`

## H1G|H1-IND-008|slot-08-constraints  (priority 1, h1_gold_relevance)

* **Context**: [indirect_cue, held_out] Which ontology category enforces boundary limits, invariants, and negative requirements?
* **Evidence**: Ontology Slot: Constraints (slot-08-constraints)

> Ontology Slot: Constraints Question What is forbidden or risky? Theoretical sources - None (derived from real incidents and security invariants, not literature) Validates module - 03 IMPLEMENTATION/packages/security/mutation gate.py Candidate concepts concept source book confidence status date added promoted note id evidence occurrences --- --- --- --- --- --- --- --- essential variables ashby design for a brain; ashby intro to cybernetics 0.95 promoted 2026-09-12 df11fec2-d5b4-4b8f-b79e-d4dbb18d3d57 We can now define ' survival ' objectively and in terms of a field : it occurs when a line of behaviour takes no essential variable outside given limits. / The distinction may best be illustrate

* **Question**: Reading only the note text below, does this note answer the query? yes / partial / no. In 'other_relevant_ids' list the id of any OTHER note in the vault that answers it better or equally well (optional).
* **Allowed labels**: `yes|partial|no`

## H1G|H1-IND-009|idx-leg-eu-dora-2022-2554  (priority 1, h1_gold_relevance)

* **Context**: [indirect_cue, held_out] What compliance index covers digital operational resilience for financial institutions?
* **Evidence**: Index Structurat: Regulamentul (UE) 2022/2554 (DORA) (idx-leg-eu-dora-2022-2554)

> Index Structurat: Regulamentul (UE) 2022/2554 (DORA) [!IMPORTANT] REGIM DE GUVERNANȚĂ & DISCLAIMER JURIDIC : Acest index este un instrument analitic structurat derivat din date normative externe ( instruction trust: NONE ). Nu reprezintă asistență juridică, consultanță sau politică activă. Statut: lifecycle: REVIEW / verification: verified source . Nu este promovat în ACTIVE fără aprobare umană prealabilă. 1. Definiții Cheie (Art. 3) - Reziliență operațională digitală (Art. 3 pct. 1) : Capacitatea unei entități financiare de a-și construi, asigura și revizui integritatea operațională și fiabilitatea prin asigurarea securității rețelelor și a sistemelor informatice. - Risc legat de TIC (Art.

* **Question**: Reading only the note text below, does this note answer the query? yes / partial / no. In 'other_relevant_ids' list the id of any OTHER note in the vault that answers it better or equally well (optional).
* **Allowed labels**: `yes|partial|no`

## H1G|H1-IND-010|d94d32d6-701e-43b7-b031-761b2c6c8e43  (priority 1, h1_gold_relevance)

* **Context**: [indirect_cue, held_out] Where is the automated bot configuration handling multi-strategy execution across forex sessions?
* **Evidence**: Monn — MetaTrader5 Auto-Trading Bot with Multiple Strategies and Timeframes (d94d32d6-701e-43b7-b031-761b2c6c8e43)

> Monn — MetaTrader5 Auto-Trading Bot with Multiple Strategies and Timeframes Summary Monn este un bot de trading care folosește MetaTrader5 API și poate rula simultan mai multe strategii și analiza multiple timeframes, oferind backtesting și tuning de parametri. Core Concept Botul separă configurarea contului MT5, fișiere de config pentru simboluri și strategii, modul de live trading și modul de test/backtest, permițând orchestrarea mai multor strategii pe diferite timeframes. Key Points - Multiple strategies: configurabile în fișiere JSON, fiecare analizată independent. - Multiple timeframes: colectează date pentru timeframes diferite pentru decizii mai informate. - Backtesting: modul de tes

* **Question**: Reading only the note text below, does this note answer the query? yes / partial / no. In 'other_relevant_ids' list the id of any OTHER note in the vault that answers it better or equally well (optional).
* **Allowed labels**: `yes|partial|no`

## H1G|H1-ENT-001|idx-leg-eu-mica-2023-1114  (priority 2, h1_gold_relevance)

* **Context**: [entity_context, development] What framework regulates crypto-asset issuers and trading service providers under EU law?
* **Evidence**: Index Structurat: Regulamentul (UE) 2023/1114 (MiCA) (idx-leg-eu-mica-2023-1114)

> Index Structurat: Regulamentul (UE) 2023/1114 (MiCA) [!IMPORTANT] REGIM DE GUVERNANȚĂ & DISCLAIMER JURIDIC : Acest index este un instrument analitic structurat derivat din date normative externe ( instruction trust: NONE ). Nu reprezintă asistență juridică, consultanță sau politică activă. Statut: lifecycle: REVIEW / verification: verified source . Nu este promovat în ACTIVE fără aprobare umană prealabilă. 1. Definiții Cheie (Art. 3) - Criptoactiv (Art. 3 pct. 5) : O reprezentare digitală a unei valori sau a unui drept care poate fi transferată și stocată electronic, utilizând tehnologia registrelor distribuite (DLT) sau o tehnologie similară. - Token raportat la active - ART (Art. 3 pct. 6)

* **Question**: Reading only the note text below, does this note answer the query? yes / partial / no. In 'other_relevant_ids' list the id of any OTHER note in the vault that answers it better or equally well (optional).
* **Allowed labels**: `yes|partial|no`

## H1G|H1-ENT-002|knw-leg-hg585-2002-0001  (priority 2, h1_gold_relevance)

* **Context**: [entity_context, development] What Romanian legislation governs state secrets and national protection standards for classified data?
* **Evidence**: Hotărârea Guvernului nr. 585/2002 — Standardele Naționale de Protecție a Informațiilor Clasificate în România (knw-leg-hg585-2002-0001)

> Hotărârea Guvernului nr. 585/2002 — Standardele Naționale de Protecție a Informațiilor Clasificate în România Preambul și Cadrul Juridic Hotărârea Guvernului nr. 585/2002 aprobă Standardele naționale de protecție a informațiilor clasificate în România , adoptate în temeiul Legii nr. 182/2002 privind protecția informațiilor clasificate. Actul normativ reprezintă cadrul fundamental de procedură și implementare tehnică a securității informațiilor clasificate pe teritoriul României, obligatoriu pentru toate autoritățile și instituțiile publice, operatorii economici cu capital public sau privat, precum și persoanele fizice care gestionează secrete de stat sau de serviciu. Autoritatea națională de

* **Question**: Reading only the note text below, does this note answer the query? yes / partial / no. In 'other_relevant_ids' list the id of any OTHER note in the vault that answers it better or equally well (optional).
* **Allowed labels**: `yes|partial|no`

## H1G|H1-ENT-003|0923cd4f-b2bd-4285-b11f-63be9d9977e8  (priority 2, h1_gold_relevance)

* **Context**: [entity_context, development] Where is the technical postmortem on creating a dual-language Romanian-English trading journal without capital expenditure?
* **Evidence**: Designing a Zero-Dollar Bilingual (RO/EN) AI Trading Journal SaaS (0923cd4f-b2bd-4285-b11f-63be9d9977e8)

> Designing a Zero-Dollar Bilingual (RO/EN) AI Trading Journal SaaS Summary Utilizatorul a explorat construirea primului trading journal SaaS bilingv (romana-engleza) din Romania, pe un stack cu cost zero, vizand un gol de piata neocupat. Context Traderii romani foloseau in prezent template-uri Excel, Google Sheets de la Binance Academy sau unelte scumpe doar in engleza; nu exista o solutie SaaS locala bilingva. What Happened S-a construit un research asupra oportunitatii si apoi un prompt tehnic pentru Gemma, pentru implementarea aplicatiei conform research-ului, cu stack React, FastAPI, Python, MongoDB si integrari AI. My Actions - Am definit un fallback chain de modele LLM (ex. groq llama3.

* **Question**: Reading only the note text below, does this note answer the query? yes / partial / no. In 'other_relevant_ids' list the id of any OTHER note in the vault that answers it better or equally well (optional).
* **Allowed labels**: `yes|partial|no`

## H1G|H1-ENT-004|finscope-project-core  (priority 2, h1_gold_relevance)

* **Context**: [entity_context, development] Where are the architecture design and core specifications for the FinScope application?
* **Evidence**: Project: FinScope (finscope-project-core)

> Project: FinScope 1. Description FinScope is a local-first, privacy-focused personal financial intelligence web application built with React 19, TypeScript, Vite, Dexie (IndexedDB), Zustand, TailwindCSS, and Recharts . 2. Core Capabilities - Local-First Storage : IndexedDB via Dexie for offline zero-cloud latency and total data privacy. - Multi-Format Ingestion : Ingest bank statements via PDF ( pdfjs-dist ), Excel ( xlsx ), CSV ( papaparse ), and OCR scanned images ( tesseract.js ). - Rule-Based Engine : Smart auto-categorization and transaction rule mapping. - Analytics & Forecasting : Recharts visualization for cashflow, recurring subscriptions, and budget tracking. 3. Location & Environm

* **Question**: Reading only the note text below, does this note answer the query? yes / partial / no. In 'other_relevant_ids' list the id of any OTHER note in the vault that answers it better or equally well (optional).
* **Allowed labels**: `yes|partial|no`

## H1G|H1-ENT-005|slot-05-state  (priority 2, h1_gold_relevance)

* **Context**: [entity_context, calibration] Which ontology slot tracks dynamic working variables and operational registers?
* **Evidence**: Ontology Slot: State (slot-05-state)

> Ontology Slot: State Question What is implemented/real/active right now? Theoretical sources - None (measured empirically from execution, never from literature) Validates module - 00 GOVERNANCE/VAULT STATE.md Candidate concepts concept source book confidence status date added promoted note id evidence occurrences --- --- --- --- --- --- --- --- state 2504.05840v1; laird soar cognitive architecture 0.90 promoted 2026-09-12 be1863da-7db4-48b7-9486-ad13b1a74152 However, rendering and then extracting general visual information is computationally burdensome and in general is beyond the current state of the art in machine vision. 17 short-term memory 2601.09113v1; memory in the age of ai agents; s

* **Question**: Reading only the note text below, does this note answer the query? yes / partial / no. In 'other_relevant_ids' list the id of any OTHER note in the vault that answers it better or equally well (optional).
* **Allowed labels**: `yes|partial|no`

## H1G|H1-ENT-006|slot-14-routing  (priority 2, h1_gold_relevance)

* **Context**: [entity_context, calibration] Where does the cognitive core look up task routing matrices and agent capability allocations?
* **Evidence**: Ontology Slot: Routing (slot-14-routing)

> Ontology Slot: Routing Question When should each agent be invoked? Theoretical sources - Newell & Laird (Soar production matching) - Minsky, Society of Mind Validates module none (not yet built — explicitly recorded as unbuilt architecture) Candidate concepts concept source book confidence status date added promoted note id evidence occurrences --- --- --- --- --- --- --- --- feedback ashby design for a brain; ashby intro to cybernetics; wiener cybernetics 0.95 promoted 2026-09-12 99280cf1-6eb2-449c-a778-e800c2868e07 The organism affects the environment, and the environment affects the organism : such a system is said to have i feedback ' (S. 32 step-mechanism ashby design for a brain 0.95 p

* **Question**: Reading only the note text below, does this note answer the query? yes / partial / no. In 'other_relevant_ids' list the id of any OTHER note in the vault that answers it better or equally well (optional).
* **Allowed labels**: `yes|partial|no`

## H1G|H1-ENT-007|d2c10cab-0028-44c7-8f9e-a1d3d963c526  (priority 1, h1_gold_relevance)

* **Context**: [entity_context, held_out] What system manages media transfers and hardware device custody on air-gapped workstations?
* **Evidence**: Registru de Transferuri (Media Transfer Register & Device Control, Air-Gapped) (d2c10cab-0028-44c7-8f9e-a1d3d963c526)

> Registru de Transferuri (Media Transfer Register & Device Control, Air-Gapped) Objective Aplicație desktop WPF .NET 10 pentru evidența transferurilor pe medii de stocare și controlul dispozitivelor, conformă cu HG 585/2002, NATO AC/35-D/1022, EUCI 2013/488/UE, NIST SP 800-88r2 și legislația conexă (MS 111/2024, MS 172/191), destinată unui mediu air-gapped/militar. Success Criteria - [x] Autentificare locala sigura (PIN hash Argon2id + salt) fara dependinte de retea. - [x] Criptare completa a bazei de date (SQLite WAL mode + DPAPI). - [x] Tema vizuală ObsidianTactical.xaml complet aplicată pe cele 7 module (WCAG AA conform, GPU frozen brushes). - [x] Punte cognitivă sidecar securizată cu AI M

* **Question**: Reading only the note text below, does this note answer the query? yes / partial / no. In 'other_relevant_ids' list the id of any OTHER note in the vault that answers it better or equally well (optional).
* **Allowed labels**: `yes|partial|no`

## H1G|H1-ENT-008|ed51e423-3078-4b0a-a38b-2f9d2bb62522  (priority 1, h1_gold_relevance)

* **Context**: [entity_context, held_out] Where are the PowerShell scripts for Windows 11 system debloating and OS security hardening?
* **Evidence**: Windows Hardening & Debloat Scripts Ecosystem (PowerShell) (ed51e423-3078-4b0a-a38b-2f9d2bb62522)

> Windows Hardening & Debloat Scripts Ecosystem (PowerShell) Summary GitHub topic powershell-script include multiple proiecte pentru optimizare, hardening și debloat Windows 10/11, cum ar fi Win11Debloat și diverse scripturi de hardening/privațiate. Core Concept Ecosistemul de scripturi PowerShell oferă soluții pentru debloating (înlăturare bloatware), privacy hardening (telemetry off, tracking off), security hardening (protocol lockdown, Defender tuning) și imaging/sysprep deployment pentru flote de PC-uri. Key Points - Win11Debloat: script popular pentru debloat + privacy tweaks, cu mod „Lite” și opțiuni Sysprep/user-target. - Alte scripturi combină hardening, debloat și privacy, uneori cu f

* **Question**: Reading only the note text below, does this note answer the query? yes / partial / no. In 'other_relevant_ids' list the id of any OTHER note in the vault that answers it better or equally well (optional).
* **Allowed labels**: `yes|partial|no`

## H1G|H1-ENT-009|ef084612-2d02-481e-a7d3-6cbe4608e10d  (priority 1, h1_gold_relevance)

* **Context**: [entity_context, held_out] What reference document details the Xerox EIP and PaperCut printing infrastructure configuration?
* **Evidence**: PaperCut MF + Xerox EIP 3.7 Setup Reference (ef084612-2d02-481e-a7d3-6cbe4608e10d)

> PaperCut MF + Xerox EIP 3.7 Setup Reference Resource Documentatia oficiala PaperCut MF pentru Secure Access pe dispozitive Xerox EIP (seria VersaLink), inclusiv configurarea CWIS, Job Log, si tipurile de porturi de imprimanta (Standard TCP/IP vs PaperCut TCP/IP Port). Relevance Folosita direct pentru configurarea si depanarea instalatiei PaperCut + Xerox VersaLink C7120 a utilizatorului (autentificare pe cartela, secure print release). Source Details Referentiata indirect prin conversatii AI care citeaza comportamentul si terminologia oficiala PaperCut (Logs Job Log, App Log, Validate page counts after printing). Access / License Notes Documentatia PaperCut este publica, disponibila pe site-

* **Question**: Reading only the note text below, does this note answer the query? yes / partial / no. In 'other_relevant_ids' list the id of any OTHER note in the vault that answers it better or equally well (optional).
* **Allowed labels**: `yes|partial|no`

## H1G|H1-ENT-010|standard-neural-naming-taxonomy  (priority 1, h1_gold_relevance)

* **Context**: [entity_context, held_out] What canonical standard defines taxonomy rules for neural module identifiers and node names?
* **Evidence**: Neural naming taxonomy (standard-neural-naming-taxonomy)

> Neural naming taxonomy The canonical vocabulary for this vault. One name per role, every role mapped to code that exists. Why this is a standard and not a rename A brain is not a set of modules with cognitive names. This vault already contains attention.py , executive.py , global workspace.py and reasoning.py — and every one of them has zero production consumers . Renaming decoratively would add vocabulary without adding function, which is the failure this standard exists to prevent. So: the taxonomy fixes ambiguity , not aesthetics. It applies to new files and to names that are genuinely ambiguous today (449 files named readme collapse into a single node in the graph). It does not authorize

* **Question**: Reading only the note text below, does this note answer the query? yes / partial / no. In 'other_relevant_ids' list the id of any OTHER note in the vault that answers it better or equally well (optional).
* **Allowed labels**: `yes|partial|no`

## H1G|H1-HOP-001|slot-05-state  (priority 2, h1_gold_relevance)

* **Context**: [multi_hop_associative, development] Following the relationship from state-determined system through variety, what ontology slot defines system state?
* **Evidence**: Ontology Slot: State (slot-05-state)

> Ontology Slot: State Question What is implemented/real/active right now? Theoretical sources - None (measured empirically from execution, never from literature) Validates module - 00 GOVERNANCE/VAULT STATE.md Candidate concepts concept source book confidence status date added promoted note id evidence occurrences --- --- --- --- --- --- --- --- state 2504.05840v1; laird soar cognitive architecture 0.90 promoted 2026-09-12 be1863da-7db4-48b7-9486-ad13b1a74152 However, rendering and then extracting general visual information is computationally burdensome and in general is beyond the current state of the art in machine vision. 17 short-term memory 2601.09113v1; memory in the age of ai agents; s

* **Question**: Reading only the note text below, does this note answer the query? yes / partial / no. In 'other_relevant_ids' list the id of any OTHER note in the vault that answers it better or equally well (optional).
* **Allowed labels**: `yes|partial|no`

## H1G|H1-HOP-002|df11fec2-d5b4-4b8f-b79e-d4dbb18d3d57  (priority 2, h1_gold_relevance)

* **Context**: [multi_hop_associative, development] Starting from state-determined systems and navigating through constraint, what essential variables must remain bounded?
* **Evidence**: essential variables (df11fec2-d5b4-4b8f-b79e-d4dbb18d3d57)

> essential variables Canonical Definition Crucial somatic quantities such as blood pressure, body temperature, and biochemical concentrations whose values must remain within viability boundaries. Judgment & Evaluation Core cognitive architecture concept recurrent across ashby design for a brain and canonical literature. Promoted to REVIEW status for ontology grounding. Code Cross-References Potentially relevant to implementation modules in 03 IMPLEMENTATION/ or procedures in 10 DOCUMENTATION/procedures/ , not independently verified this package.

* **Question**: Reading only the note text below, does this note answer the query? yes / partial / no. In 'other_relevant_ids' list the id of any OTHER note in the vault that answers it better or equally well (optional).
* **Allowed labels**: `yes|partial|no`

## H1G|H1-HOP-003|slot-08-constraints  (priority 2, h1_gold_relevance)

* **Context**: [multi_hop_associative, development] From state-determined systems through constraint relationships, which master ontology slot records system constraints?
* **Evidence**: Ontology Slot: Constraints (slot-08-constraints)

> Ontology Slot: Constraints Question What is forbidden or risky? Theoretical sources - None (derived from real incidents and security invariants, not literature) Validates module - 03 IMPLEMENTATION/packages/security/mutation gate.py Candidate concepts concept source book confidence status date added promoted note id evidence occurrences --- --- --- --- --- --- --- --- essential variables ashby design for a brain; ashby intro to cybernetics 0.95 promoted 2026-09-12 df11fec2-d5b4-4b8f-b79e-d4dbb18d3d57 We can now define ' survival ' objectively and in terms of a field : it occurs when a line of behaviour takes no essential variable outside given limits. / The distinction may best be illustrate

* **Question**: Reading only the note text below, does this note answer the query? yes / partial / no. In 'other_relevant_ids' list the id of any OTHER note in the vault that answers it better or equally well (optional).
* **Allowed labels**: `yes|partial|no`

## H1G|H1-HOP-004|6e014224-af01-4011-8050-082d3f9ea35e  (priority 2, h1_gold_relevance)

* **Context**: [multi_hop_associative, development] Navigating from security gaps through the procedures slot, what cognitive concept governs procedural memory?
* **Evidence**: procedural memory (6e014224-af01-4011-8050-082d3f9ea35e)

> procedural memory Canonical Definition A broad behavioral memory category encompassing motor coordination, perceptual skills, and cognitive algorithms that operate automatically without conscious inspection. Judgment & Evaluation Core cognitive architecture concept recurrent across schacter tulving memory systems 1994 and canonical literature. Promoted to REVIEW status for ontology grounding. Code Cross-References Potentially relevant to implementation modules in 03 IMPLEMENTATION/ or procedures in 10 DOCUMENTATION/procedures/ , not independently verified this package.

* **Question**: Reading only the note text below, does this note answer the query? yes / partial / no. In 'other_relevant_ids' list the id of any OTHER note in the vault that answers it better or equally well (optional).
* **Allowed labels**: `yes|partial|no`

## H1G|H1-HOP-005|3f2a91c4-7d18-4e5b-9a06-52c8b41d7e39  (priority 2, h1_gold_relevance)

* **Context**: [multi_hop_associative, calibration] Tracing from the security gap register through the procedures slot, what runbook defines ingesting a book into the ontology?
* **Evidence**: Ingesting a book into the ontology (3f2a91c4-7d18-4e5b-9a06-52c8b41d7e39)

> Ingesting a book into the ontology The recipe below is what survived being measured. Most of what was tried did not, and the parts that failed are listed too — a procedure that only records its successes sends the next person down the same dead ends. Every number here was measured in this repository against 06 INBOX/Carti . The full evidence is in [ 07 EVALUATION/book corpus conversion/FINDINGS.md ](../../07 EVALUATION/book corpus conversion/FINDINGS.md). The recipe Convert, then have an agent read the chunks: The local-provider fallback Same gates, a model provider instead of an agent. --provider has no default: it used to be local , and a runner that called the extractor without thinking a

* **Question**: Reading only the note text below, does this note answer the query? yes / partial / no. In 'other_relevant_ids' list the id of any OTHER note in the vault that answers it better or equally well (optional).
* **Allowed labels**: `yes|partial|no`

## H1G|H1-HOP-006|f82194d2-31a8-4c91-a83d-e42109ab7d12  (priority 2, h1_gold_relevance)

* **Context**: [multi_hop_associative, calibration] Following project structure preferences through the Elite Quant Bot, what architectural principles govern XAU_Kinetic?
* **Evidence**: XAU_Kinetic Clean Architecture Principles (f82194d2-31a8-4c91-a83d-e42109ab7d12)

> XAU Kinetic Clean Architecture Principles Overview XAU Kinetic is a institutional quantitative trading engine built with Clean Architecture in Python 3.12+. It separates concerns strictly between core domain entities, application orchestration, infrastructure drivers, risk management, and trading strategy decisions. Core Architectural Invariants 1. Zero-Trust Data Flow (Pydantic V2) All inputs and outputs crossing architectural boundaries use strict Pydantic V2 schemas ( SignalObject , Position , TickData , AccountInfo , OrderResult , AuditEvent ). No raw dictionaries or unvalidated floating point values are passed between strategy and risk modules. 2. Isolated Risk Veto Power RiskManager ac

* **Question**: Reading only the note text below, does this note answer the query? yes / partial / no. In 'other_relevant_ids' list the id of any OTHER note in the vault that answers it better or equally well (optional).
* **Allowed labels**: `yes|partial|no`

## H1G|H1-HOP-007|b492a8e1-5c02-4b21-9e12-c7f8a31e9202  (priority 1, h1_gold_relevance)

* **Context**: [multi_hop_associative, held_out] Starting from multi-file preferences through the trading bot dependency, what procedure handles deployment and rollback?
* **Evidence**: Procedure: Deployment and Operation of XAU_Kinetic Quant Bot (b492a8e1-5c02-4b21-9e12-c7f8a31e9202)

> Procedure: Deployment and Operation of XAU Kinetic Quant Bot Purpose Standard operating procedure for deploying, configuring, verifying, and monitoring the XAU Kinetic quantitative trading engine in standalone test or live MetaTrader 5 production environments. Scope Applies to local and server environments running Python 3.12+ with MetaTrader 5 terminal connectivity on Windows. Preconditions 1. Python 3.12+ installed. 2. Dependencies installed: pydantic =2.0.0 , pandas =2.0.0 , numpy =1.24.0 . 3. MetaTrader 5 terminal installed, updated, and logged into broker account (for live trading mode). Dependencies - Package dependencies: xau kinetic/requirements.txt - Configuration file: xau kinetic/

* **Question**: Reading only the note text below, does this note answer the query? yes / partial / no. In 'other_relevant_ids' list the id of any OTHER note in the vault that answers it better or equally well (optional).
* **Allowed labels**: `yes|partial|no`

## H1G|H1-HOP-008|67e1cb1d-3e5a-45be-b265-67bb126a2cd3  (priority 1, h1_gold_relevance)

* **Context**: [multi_hop_associative, held_out] Tracing from zero-dollar SaaS through implementation prompt splits, what document provides the tech stack reference?
* **Evidence**: Tech Stack — Referință (67e1cb1d-3e5a-45be-b265-67bb126a2cd3)

> Tech Stack — Referință Limbaje & Runtime Tehnologie Context principal de utilizare --- --- Python Automation, algo trading, tooling securitate PowerShell Administrare AD, GPO automation, scripting sistem JavaScript/Node.js Tooling, integrări SQL Interogări/administrare baze de date MQL5 Expert Advisors MetaTrader5 .NET (C , WPF) Aplicații desktop (LogAnalyzer MVP) Infrastructură - Active Directory — administrare, GPO - Docker — containerizare tooling/servicii - Windows Server — mediu principal de administrare Note tehnice recurente (constrângeri cunoscute) - MQL5: const string globals, static vs dynamic arrays, TimeCurrent / TimeToStruct , auto-detecție filling mode broker - PowerShell stand

* **Question**: Reading only the note text below, does this note answer the query? yes / partial / no. In 'other_relevant_ids' list the id of any OTHER note in the vault that answers it better or equally well (optional).
* **Allowed labels**: `yes|partial|no`

## H1G|H1-HOP-009|c1a01101-7291-49fa-9481-22904c10b004  (priority 1, h1_gold_relevance)

* **Context**: [multi_hop_associative, held_out] From project structuring guidelines via quant bot architecture, which document details distributed trading system design?
* **Evidence**: Arhitectura Sistemelor de Tranzacționare Algoritmică în Python (MT5 & Risc) (c1a01101-7291-49fa-9481-22904c10b004)

> Arhitectura Sistemelor de Tranzacționare Algoritmică în Python (MT5 & Risc) TL;DR Un bug în codul de trading generează pierderi financiare directe. Arhitectura impune separarea decuplată a 5 module: data/ , strategy/ , risk/ , execution/ și journal/ . Modulul de risc deține drept de veto absolut asupra oricărui semnal generat de strategie; backtesting-ul interzice cu strictețe bias-ul de tip look-ahead . Key Facts - Separare Strictă pe 5 Straturi : 1. data/ : Feed-uri de preț, lumânări și normalizare (fără logică de decizie). 2. strategy/ : Semnale pure ( generate signal(data) - Signal ), fără side-effects sau apeluri de broker. 3. risk/ : Dimensionare poziție, calcul Stop-Loss din procent f

* **Question**: Reading only the note text below, does this note answer the query? yes / partial / no. In 'other_relevant_ids' list the id of any OTHER note in the vault that answers it better or equally well (optional).
* **Allowed labels**: `yes|partial|no`

## H1G|H1-HOP-010|7b6aae13-83e6-43ec-80df-7c8a202251fd  (priority 1, h1_gold_relevance)

* **Context**: [multi_hop_associative, held_out] Through the procedures ontology slot from security gap review, what rule defines quoting from copyrighted sources?
* **Evidence**: Quoting from copyrighted sources (7b6aae13-83e6-43ec-80df-7c8a202251fd)

> Quoting from copyrighted sources This repository is public. Books ingested from 06 INBOX/Carti/ are not: they are ordinary copyrighted works. The PDFs and the extracted text are kept out of git, and 07 EVALUATION/book corpus conversion/FINDINGS.md says so. The quotes are the part that slipped through: grounding checks store the sentence a concept came from, and those artefacts are committed. They were 33 verbatim sentences from one book, up to 539 characters each, before this procedure existed. Why the quotes exist at all A quote is how a fabricated concept is caught. verify agent submission.py requires the quote to appear verbatim in the source text, so a model that invents a definition can

* **Question**: Reading only the note text below, does this note answer the query? yes / partial / no. In 'other_relevant_ids' list the id of any OTHER note in the vault that answers it better or equally well (optional).
* **Allowed labels**: `yes|partial|no`

## H1G|H1-CNF-001|0c4c8b76-85c4-4fde-a14a-4bde0b840009  (priority 2, h1_gold_relevance)

* **Context**: [conflict, development] Which rollback procedure applies specifically to repository git recovery versus automated service deployment?
* **Evidence**: Git, Backup, Restore, and Rollback (0c4c8b76-85c4-4fde-a14a-4bde0b840009)

> Git, Backup, Restore, and Rollback Purpose Preserve recoverable history while protecting raw provenance. Procedure 1. Inspect the target files and run the Integrity Check before a material change. 2. Review the change set; never include credentials or other secrets. 3. Commit coherent, validated changes with a descriptive message when Git is available. 4. Keep an independent backup of the vault, including 06 INBOX/RAW IMPORTS/ , before a migration or mass modification. 5. Restore by copying from a verified backup or by reverting a reviewed commit; verify raw evidence paths and canonical links after restore. 6. Roll back by creating a corrective commit or restoring selected files. Do not dele

* **Question**: Reading only the note text below, does this note answer the query? yes / partial / no. In 'other_relevant_ids' list the id of any OTHER note in the vault that answers it better or equally well (optional).
* **Allowed labels**: `yes|partial|no`

## H1G|H1-CNF-001|b492a8e1-5c02-4b21-9e12-c7f8a31e9202  (priority 2, h1_gold_relevance)

* **Context**: [conflict, development] Which rollback procedure applies specifically to repository git recovery versus automated service deployment?
* **Evidence**: Procedure: Deployment and Operation of XAU_Kinetic Quant Bot (b492a8e1-5c02-4b21-9e12-c7f8a31e9202)

> Procedure: Deployment and Operation of XAU Kinetic Quant Bot Purpose Standard operating procedure for deploying, configuring, verifying, and monitoring the XAU Kinetic quantitative trading engine in standalone test or live MetaTrader 5 production environments. Scope Applies to local and server environments running Python 3.12+ with MetaTrader 5 terminal connectivity on Windows. Preconditions 1. Python 3.12+ installed. 2. Dependencies installed: pydantic =2.0.0 , pandas =2.0.0 , numpy =1.24.0 . 3. MetaTrader 5 terminal installed, updated, and logged into broker account (for live trading mode). Dependencies - Package dependencies: xau kinetic/requirements.txt - Configuration file: xau kinetic/

* **Question**: Reading only the note text below, does this note answer the query? yes / partial / no. In 'other_relevant_ids' list the id of any OTHER note in the vault that answers it better or equally well (optional).
* **Allowed labels**: `yes|partial|no`

## H1G|H1-CNF-002|0c4c8b76-85c4-4fde-a14a-4bde0b840010  (priority 2, h1_gold_relevance)

* **Context**: [conflict, development] When ingesting untrusted external material versus structured books, which sanitization rule takes precedence?
* **Evidence**: Import Sanitization (0c4c8b76-85c4-4fde-a14a-4bde0b840010)

> Import Sanitization Purpose Prepare an external-memory derivative without changing the original evidence. Procedure 1. Preserve the unmodified export permanently in 06 INBOX/RAW IMPORTS/ . 2. Create a derivative outside RAW IMPORTS/ ; record its source in provenance.source ref and provenance.original path . 3. Remove conversational noise, separate atomic concepts, classify the candidate, and assign provisional confidence and verification. 4. Redact credentials, secrets, and unnecessary personal data from the derivative only; record provenance.redaction: applied when used. 5. Check duplicates, contradictions, frontmatter, and links using Integrity Check. 6. Move only the derivative through RA

* **Question**: Reading only the note text below, does this note answer the query? yes / partial / no. In 'other_relevant_ids' list the id of any OTHER note in the vault that answers it better or equally well (optional).
* **Allowed labels**: `yes|partial|no`

## H1G|H1-CNF-002|3f2a91c4-7d18-4e5b-9a06-52c8b41d7e39  (priority 2, h1_gold_relevance)

* **Context**: [conflict, development] When ingesting untrusted external material versus structured books, which sanitization rule takes precedence?
* **Evidence**: Ingesting a book into the ontology (3f2a91c4-7d18-4e5b-9a06-52c8b41d7e39)

> Ingesting a book into the ontology The recipe below is what survived being measured. Most of what was tried did not, and the parts that failed are listed too — a procedure that only records its successes sends the next person down the same dead ends. Every number here was measured in this repository against 06 INBOX/Carti . The full evidence is in [ 07 EVALUATION/book corpus conversion/FINDINGS.md ](../../07 EVALUATION/book corpus conversion/FINDINGS.md). The recipe Convert, then have an agent read the chunks: The local-provider fallback Same gates, a model provider instead of an agent. --provider has no default: it used to be local , and a runner that called the extractor without thinking a

* **Question**: Reading only the note text below, does this note answer the query? yes / partial / no. In 'other_relevant_ids' list the id of any OTHER note in the vault that answers it better or equally well (optional).
* **Allowed labels**: `yes|partial|no`

## H1G|H1-CNF-003|e045ded5-01fd-4a28-8f85-203ebae670de  (priority 2, h1_gold_relevance)

* **Context**: [conflict, development] When an agent retrieves learned behavior, does declarative fact recall or procedural execution govern?
* **Evidence**: declarative memory (e045ded5-01fd-4a28-8f85-203ebae670de)

> declarative memory Canonical Definition A flexible cognitive retention system characterized by enabling novel inferential derivations and contextual problem-solving beyond rigid associative routines. Judgment & Evaluation Core cognitive architecture concept recurrent across schacter tulving memory systems 1994 and canonical literature. Promoted to REVIEW status for ontology grounding. Code Cross-References Potentially relevant to implementation modules in 03 IMPLEMENTATION/ or procedures in 10 DOCUMENTATION/procedures/ , not independently verified this package.

* **Question**: Reading only the note text below, does this note answer the query? yes / partial / no. In 'other_relevant_ids' list the id of any OTHER note in the vault that answers it better or equally well (optional).
* **Allowed labels**: `yes|partial|no`

## H1G|H1-CNF-003|6e014224-af01-4011-8050-082d3f9ea35e  (priority 2, h1_gold_relevance)

* **Context**: [conflict, development] When an agent retrieves learned behavior, does declarative fact recall or procedural execution govern?
* **Evidence**: procedural memory (6e014224-af01-4011-8050-082d3f9ea35e)

> procedural memory Canonical Definition A broad behavioral memory category encompassing motor coordination, perceptual skills, and cognitive algorithms that operate automatically without conscious inspection. Judgment & Evaluation Core cognitive architecture concept recurrent across schacter tulving memory systems 1994 and canonical literature. Promoted to REVIEW status for ontology grounding. Code Cross-References Potentially relevant to implementation modules in 03 IMPLEMENTATION/ or procedures in 10 DOCUMENTATION/procedures/ , not independently verified this package.

* **Question**: Reading only the note text below, does this note answer the query? yes / partial / no. In 'other_relevant_ids' list the id of any OTHER note in the vault that answers it better or equally well (optional).
* **Allowed labels**: `yes|partial|no`

## H1G|H1-CNF-004|fd63129c-091c-4171-9eb4-b7c28190b1a4  (priority 2, h1_gold_relevance)

* **Context**: [conflict, development] When debugging test pass failures, is the root cause cancelling changes or a deceptive defensive guard?
* **Evidence**: Two independently correct changes can cancel each other and report success (fd63129c-091c-4171-9eb4-b7c28190b1a4)

> Two independently correct changes can cancel each other and report success Problem Graph expansion ran on every query, reported status ok , and added exactly zero nodes. No test failed, no error appeared, and the graph-on arm was silently identical to the baseline. How it was found The expansion budget was min(2 len(notes), 20) - len(notes) . A separate, correct change had raised the lexical candidate limit from a small number to 200, which makes that expression negative. Every test still passed because the tests use small mock corpora where the formula behaves. The contradiction that exposed it was a metric that cannot happen: context recall above candidate recall, meaning a note reached th

* **Question**: Reading only the note text below, does this note answer the query? yes / partial / no. In 'other_relevant_ids' list the id of any OTHER note in the vault that answers it better or equally well (optional).
* **Allowed labels**: `yes|partial|no`

## H1G|H1-CNF-004|123fe8df-2490-44f0-b695-143c3d5a997f  (priority 2, h1_gold_relevance)

* **Context**: [conflict, development] When debugging test pass failures, is the root cause cancelling changes or a deceptive defensive guard?
* **Evidence**: A guard that fires for the wrong reason hides the defect it was protecting (123fe8df-2490-44f0-b695-143c3d5a997f)

> A guard that fires for the wrong reason hides the defect it was protecting Problem The held-out benchmark refused to run: FROZEN SET HASH MISMATCH . The obvious readings were tampering or a bad freeze. Both were wrong, and the real defect was far larger. How it was found The recorded hash matched the file's content once CRLF was normalised to LF, and git reported the file unchanged. So the set was intact and the guard was hashing raw bytes on a Windows checkout — a platform-dependent false alarm that would pass on Linux CI. Because the guard aborted at the second line, nobody had ever run the benchmark to completion. Doing so revealed that all 48 cases referenced two gold notes that do not e

* **Question**: Reading only the note text below, does this note answer the query? yes / partial / no. In 'other_relevant_ids' list the id of any OTHER note in the vault that answers it better or equally well (optional).
* **Allowed labels**: `yes|partial|no`

## H1G|H1-CNF-005|idx-leg-eu-aiact-2024-1689  (priority 2, h1_gold_relevance)

* **Context**: [conflict, calibration] When handling personal data in high-risk AI decision systems, which European regulation defines compliance boundaries?
* **Evidence**: Index Structurat: Regulamentul (UE) 2024/1689 (AI Act) (idx-leg-eu-aiact-2024-1689)

> Index Structurat: Regulamentul (UE) 2024/1689 (AI Act) [!IMPORTANT] REGIM DE GUVERNANȚĂ & DISCLAIMER JURIDIC : Acest index este un instrument analitic structurat derivat din date normative externe ( instruction trust: NONE ). Nu reprezintă asistență juridică, consultanță sau politică activă. Statut: lifecycle: REVIEW / verification: verified source . Nu este promovat în ACTIVE fără aprobare umană prealabilă. 1. Definiții Cheie (Art. 3) - Sistem de IA (Art. 3 pct. 1) : Sistem bazat pe mașină conceput să funcționeze cu diferite niveluri de autonomie și care poate genera rezultate precum previziuni, recomandări sau decizii. - Model de IA de uz general — GPAI (Art. 3 pct. 63) : Model de IA cu ca

* **Question**: Reading only the note text below, does this note answer the query? yes / partial / no. In 'other_relevant_ids' list the id of any OTHER note in the vault that answers it better or equally well (optional).
* **Allowed labels**: `yes|partial|no`

## H1G|H1-CNF-005|idx-leg-eu-gdpr-2016-679  (priority 2, h1_gold_relevance)

* **Context**: [conflict, calibration] When handling personal data in high-risk AI decision systems, which European regulation defines compliance boundaries?
* **Evidence**: Index Structurat: Regulamentul (UE) 2016/679 (GDPR) (idx-leg-eu-gdpr-2016-679)

> Index Structurat: Regulamentul (UE) 2016/679 (GDPR) [!IMPORTANT] REGIM DE GUVERNANȚĂ & DISCLAIMER JURIDIC : Acest index este un instrument analitic structurat derivat din date normative externe ( instruction trust: NONE ). Nu reprezintă asistență juridică, consultanță sau politică activă. Statut: lifecycle: REVIEW / verification: verified source . Nu este promovat în ACTIVE fără aprobare umană prealabilă. 1. Definiții Cheie (Art. 4) - Date cu caracter personal (Art. 4 pct. 1) : Orice informații privind o persoană fizică identificată sau identificabilă. - Prelucrare (Art. 4 pct. 2) : Orice operațiune efectuată asupra datelor (colectare, înregistrare, stocare, modificare, consultare, ștergere)

* **Question**: Reading only the note text below, does this note answer the query? yes / partial / no. In 'other_relevant_ids' list the id of any OTHER note in the vault that answers it better or equally well (optional).
* **Allowed labels**: `yes|partial|no`

## H1G|H1-CNF-006|slot-05-state  (priority 2, h1_gold_relevance)

* **Context**: [conflict, calibration] When storing active runtime variables, should they be indexed in the dynamic state slot or the retrieval catalog?
* **Evidence**: Ontology Slot: State (slot-05-state)

> Ontology Slot: State Question What is implemented/real/active right now? Theoretical sources - None (measured empirically from execution, never from literature) Validates module - 00 GOVERNANCE/VAULT STATE.md Candidate concepts concept source book confidence status date added promoted note id evidence occurrences --- --- --- --- --- --- --- --- state 2504.05840v1; laird soar cognitive architecture 0.90 promoted 2026-09-12 be1863da-7db4-48b7-9486-ad13b1a74152 However, rendering and then extracting general visual information is computationally burdensome and in general is beyond the current state of the art in machine vision. 17 short-term memory 2601.09113v1; memory in the age of ai agents; s

* **Question**: Reading only the note text below, does this note answer the query? yes / partial / no. In 'other_relevant_ids' list the id of any OTHER note in the vault that answers it better or equally well (optional).
* **Allowed labels**: `yes|partial|no`

## H1G|H1-CNF-006|slot-15-retrieval  (priority 2, h1_gold_relevance)

* **Context**: [conflict, calibration] When storing active runtime variables, should they be indexed in the dynamic state slot or the retrieval catalog?
* **Evidence**: Ontology Slot: Retrieval (slot-15-retrieval)

> Ontology Slot: Retrieval Question How is relevant memory found? Theoretical sources - None (grounded in real code, not primarily literature) Validates module - 03 IMPLEMENTATION/packages/memory/controller.py (verified definition of MemoryController.search() at line 376; distinct from the 03 IMPLEMENTATION/packages/memory controller/ compatibility shim) Candidate concepts concept source book confidence status date added promoted note id evidence occurrences --- --- --- --- --- --- --- --- retrieval 2601.09113v1; memory in the age of ai agents; wcs 1488 0.95 promoted 2026-09-12 9e6da9df-7fca-4cf5-99bf-03fbdee15170 related concepts such as LLM memory, retrieval-augmented generation (RAG), and c

* **Question**: Reading only the note text below, does this note answer the query? yes / partial / no. In 'other_relevant_ids' list the id of any OTHER note in the vault that answers it better or equally well (optional).
* **Allowed labels**: `yes|partial|no`

## H1G|H1-CNF-007|knw-context-packing-p1-0001  (priority 1, h1_gold_relevance)

* **Context**: [conflict, held_out] When context token limits are reached, should the system truncate candidate generation or compress the final pack?
* **Evidence**: Context Packing P1 Empirical Findings & Architectural Specification (knw-context-packing-p1-0001)

> Context Packing P1 Empirical Findings & Architectural Specification This knowledge document formalizes the empirical findings, measurements, failure modes, and architectural implications derived from the P1 Context Packing Laboratory executed over real AI Memory Vault data. --- 1. Executive Summary Empirical measurement of the retrieval-packing pipeline isolated PACKING FAILURE as the primary bottleneck preventing high candidate recall ($76.7\%$) from reaching generative LLM accuracy. Under the production baseline ( P0 ): - Packing Loss Rate : 76.1% of all discovered factual keywords were discarded or corrupted before reaching the LLM context. - Root causes in production ContextPackBuilder :

* **Question**: Reading only the note text below, does this note answer the query? yes / partial / no. In 'other_relevant_ids' list the id of any OTHER note in the vault that answers it better or equally well (optional).
* **Allowed labels**: `yes|partial|no`

## H1G|H1-CNF-007|knw-retrieval-bottleneck-p0-0001  (priority 1, h1_gold_relevance)

* **Context**: [conflict, held_out] When context token limits are reached, should the system truncate candidate generation or compress the final pack?
* **Evidence**: Retrieval Bottleneck — P0 Empirical Findings & Architectural Specification (knw-retrieval-bottleneck-p0-0001)

> Retrieval Bottleneck — P0 Empirical Findings & Architectural Specification This knowledge document formalizes the empirical findings, mathematical measurements, failure modes, and architectural implications derived from the P0 Real Pipeline Diagnostic Experiment executed against the AI Memory Vault. --- 1. Executive Summary Empirical measurement of the real production pipeline ( MemoryController.search $\rightarrow$ QueryClassifier $\rightarrow$ RetrievalEngine $\rightarrow$ RelevanceScorer $\rightarrow$ ProgressiveDisclosure $\rightarrow$ ContextPackBuilder ) revealed that the primary accuracy bottleneck in sparse-context agentic execution is Candidate Discovery / Retrieval Composition , ra

* **Question**: Reading only the note text below, does this note answer the query? yes / partial / no. In 'other_relevant_ids' list the id of any OTHER note in the vault that answers it better or equally well (optional).
* **Allowed labels**: `yes|partial|no`

## H1G|H1-CNF-008|f10029dc-e28e-4710-bf3a-b2b48bfb84dc  (priority 1, h1_gold_relevance)

* **Context**: [conflict, held_out] When deploying bilingual Romanian-English user interfaces, does the strict English prompt preference apply to internal system prompts?
* **Evidence**: Every AI-facing prompt is written in English, in full (f10029dc-e28e-4710-bf3a-b2b48bfb84dc)

> Every AI-facing prompt is written in English, in full Problem Requests arrive as one informal line in Romanian. What reached other agents was sometimes that same line, so the receiving agent began by reconstructing intent, context and constraints that the sender already knew. How it was found Stated directly by the user, generalising an existing narrower preference: Trading Bot Prompt Language English recorded the same rule but scoped only to trading-bot prompts. The rule is not domain-specific. What fixed it Replies to the user stay in Romanian. Everything transmitted to another agent is English and complete: verified context, task, requirements, what is forbidden, the traps already paid fo

* **Question**: Reading only the note text below, does this note answer the query? yes / partial / no. In 'other_relevant_ids' list the id of any OTHER note in the vault that answers it better or equally well (optional).
* **Allowed labels**: `yes|partial|no`

## H1G|H1-CNF-008|0923cd4f-b2bd-4285-b11f-63be9d9977e8  (priority 1, h1_gold_relevance)

* **Context**: [conflict, held_out] When deploying bilingual Romanian-English user interfaces, does the strict English prompt preference apply to internal system prompts?
* **Evidence**: Designing a Zero-Dollar Bilingual (RO/EN) AI Trading Journal SaaS (0923cd4f-b2bd-4285-b11f-63be9d9977e8)

> Designing a Zero-Dollar Bilingual (RO/EN) AI Trading Journal SaaS Summary Utilizatorul a explorat construirea primului trading journal SaaS bilingv (romana-engleza) din Romania, pe un stack cu cost zero, vizand un gol de piata neocupat. Context Traderii romani foloseau in prezent template-uri Excel, Google Sheets de la Binance Academy sau unelte scumpe doar in engleza; nu exista o solutie SaaS locala bilingva. What Happened S-a construit un research asupra oportunitatii si apoi un prompt tehnic pentru Gemma, pentru implementarea aplicatiei conform research-ului, cu stack React, FastAPI, Python, MongoDB si integrari AI. My Actions - Am definit un fallback chain de modele LLM (ex. groq llama3.

* **Question**: Reading only the note text below, does this note answer the query? yes / partial / no. In 'other_relevant_ids' list the id of any OTHER note in the vault that answers it better or equally well (optional).
* **Allowed labels**: `yes|partial|no`

## H1G|H1-CNF-009|d94d32d6-701e-43b7-b031-761b2c6c8e43  (priority 1, h1_gold_relevance)

* **Context**: [conflict, held_out] When setting up trade routing in MetaTrader, should execution logic reside in the EA bot or the clean architecture layer?
* **Evidence**: Monn — MetaTrader5 Auto-Trading Bot with Multiple Strategies and Timeframes (d94d32d6-701e-43b7-b031-761b2c6c8e43)

> Monn — MetaTrader5 Auto-Trading Bot with Multiple Strategies and Timeframes Summary Monn este un bot de trading care folosește MetaTrader5 API și poate rula simultan mai multe strategii și analiza multiple timeframes, oferind backtesting și tuning de parametri. Core Concept Botul separă configurarea contului MT5, fișiere de config pentru simboluri și strategii, modul de live trading și modul de test/backtest, permițând orchestrarea mai multor strategii pe diferite timeframes. Key Points - Multiple strategies: configurabile în fișiere JSON, fiecare analizată independent. - Multiple timeframes: colectează date pentru timeframes diferite pentru decizii mai informate. - Backtesting: modul de tes

* **Question**: Reading only the note text below, does this note answer the query? yes / partial / no. In 'other_relevant_ids' list the id of any OTHER note in the vault that answers it better or equally well (optional).
* **Allowed labels**: `yes|partial|no`

## H1G|H1-CNF-009|f82194d2-31a8-4c91-a83d-e42109ab7d12  (priority 1, h1_gold_relevance)

* **Context**: [conflict, held_out] When setting up trade routing in MetaTrader, should execution logic reside in the EA bot or the clean architecture layer?
* **Evidence**: XAU_Kinetic Clean Architecture Principles (f82194d2-31a8-4c91-a83d-e42109ab7d12)

> XAU Kinetic Clean Architecture Principles Overview XAU Kinetic is a institutional quantitative trading engine built with Clean Architecture in Python 3.12+. It separates concerns strictly between core domain entities, application orchestration, infrastructure drivers, risk management, and trading strategy decisions. Core Architectural Invariants 1. Zero-Trust Data Flow (Pydantic V2) All inputs and outputs crossing architectural boundaries use strict Pydantic V2 schemas ( SignalObject , Position , TickData , AccountInfo , OrderResult , AuditEvent ). No raw dictionaries or unvalidated floating point values are passed between strategy and risk modules. 2. Isolated Risk Veto Power RiskManager ac

* **Question**: Reading only the note text below, does this note answer the query? yes / partial / no. In 'other_relevant_ids' list the id of any OTHER note in the vault that answers it better or equally well (optional).
* **Allowed labels**: `yes|partial|no`

## H1G|H1-CNF-010|knw-leg-hg585-2002-0001  (priority 1, h1_gold_relevance)

* **Context**: [conflict, held_out] When handling classified state data in defense contexts, does general HG 585/2002 or ministerial order M.172/2021 govern?
* **Evidence**: Hotărârea Guvernului nr. 585/2002 — Standardele Naționale de Protecție a Informațiilor Clasificate în România (knw-leg-hg585-2002-0001)

> Hotărârea Guvernului nr. 585/2002 — Standardele Naționale de Protecție a Informațiilor Clasificate în România Preambul și Cadrul Juridic Hotărârea Guvernului nr. 585/2002 aprobă Standardele naționale de protecție a informațiilor clasificate în România , adoptate în temeiul Legii nr. 182/2002 privind protecția informațiilor clasificate. Actul normativ reprezintă cadrul fundamental de procedură și implementare tehnică a securității informațiilor clasificate pe teritoriul României, obligatoriu pentru toate autoritățile și instituțiile publice, operatorii economici cu capital public sau privat, precum și persoanele fizice care gestionează secrete de stat sau de serviciu. Autoritatea națională de

* **Question**: Reading only the note text below, does this note answer the query? yes / partial / no. In 'other_relevant_ids' list the id of any OTHER note in the vault that answers it better or equally well (optional).
* **Allowed labels**: `yes|partial|no`

## H1G|H1-CNF-010|idx-leg-ro-mapn-m172-2021  (priority 1, h1_gold_relevance)

* **Context**: [conflict, held_out] When handling classified state data in defense contexts, does general HG 585/2002 or ministerial order M.172/2021 govern?
* **Evidence**: Index Structurat: Ordinul Ministrului Apărării Naționale nr. M.172/2021 (idx-leg-ro-mapn-m172-2021)

> Index Structurat: Ordinul Ministrului Apărării Naționale nr. M.172/2021 [!IMPORTANT] REGIM DE GUVERNANȚĂ & DISCLAIMER JURIDIC : Acest index este un instrument analitic structurat derivat din date normative externe ( instruction trust: NONE ). Nu reprezintă asistență juridică, consultanță sau politică activă. Statut: lifecycle: REVIEW / verification: verified source . Nu este promovat în ACTIVE fără aprobare umană prealabilă. 1. Definiții Cheie - CDC : Centrul de Documente Clasificate (pentru informații naționale). - CSNR : Punctul de Control Subregistru (pentru informații NATO, UE și Echivalente). - CDC autorizat : Gestionare până la nivelul NATO RESTRICTED / UE RESTREINT. - AOSSIC : Autorit

* **Question**: Reading only the note text below, does this note answer the query? yes / partial / no. In 'other_relevant_ids' list the id of any OTHER note in the vault that answers it better or equally well (optional).
* **Allowed labels**: `yes|partial|no`

## H1G|H1-DST-001|vault-architecture-map-0001  (priority 2, h1_gold_relevance)

* **Context**: [distractor, development] Show the architectural system dataflow and boundary topology of the memory vault.
* **Evidence**: Vault Architecture & Information Dataflow Map (vault-architecture-map-0001)

> Vault Architecture & Information Dataflow Map 1. Information Flow Pipeline The I Memory Vault strictly separates research, evidence, evaluation, canonical knowledge, dynamic memory, and runtime telemetry: --- 2. Semantic Responsibility Boundaries Layer 1: Governance & Core Invariants ( 00 CORE/ ) Role : Foundational identity, security trust boundaries P0-P18, and confidence models. Mutability : Strict human/admin attestation required. Layer 2: Canonical Knowledge ( 01 KNOWLEDGE/ ) Role : Verified domain architectures, research findings, and protocols. Mutability : AI proposed into REVIEW; human promotes to ACTIVE. Layer 3: System Blueprints & Projects ( 02 PROJECTS/ ) Role : Engineering proj

* **Question**: Reading only the note text below, does this note answer the query? yes / partial / no. In 'other_relevant_ids' list the id of any OTHER note in the vault that answers it better or equally well (optional).
* **Allowed labels**: `yes|partial|no`

## H1G|H1-DST-002|0c4c8b76-85c4-4fde-a14a-4bde0b840009  (priority 2, h1_gold_relevance)

* **Context**: [distractor, development] What steps are required to restore git repository state after a corrupted commit?
* **Evidence**: Git, Backup, Restore, and Rollback (0c4c8b76-85c4-4fde-a14a-4bde0b840009)

> Git, Backup, Restore, and Rollback Purpose Preserve recoverable history while protecting raw provenance. Procedure 1. Inspect the target files and run the Integrity Check before a material change. 2. Review the change set; never include credentials or other secrets. 3. Commit coherent, validated changes with a descriptive message when Git is available. 4. Keep an independent backup of the vault, including 06 INBOX/RAW IMPORTS/ , before a migration or mass modification. 5. Restore by copying from a verified backup or by reverting a reviewed commit; verify raw evidence paths and canonical links after restore. 6. Roll back by creating a corrective commit or restoring selected files. Do not dele

* **Question**: Reading only the note text below, does this note answer the query? yes / partial / no. In 'other_relevant_ids' list the id of any OTHER note in the vault that answers it better or equally well (optional).
* **Allowed labels**: `yes|partial|no`

## H1G|H1-DST-003|knw-context-packing-p1-0001  (priority 2, h1_gold_relevance)

* **Context**: [distractor, development] What rules govern soft context budgeting and graceful disclosure degradation?
* **Evidence**: Context Packing P1 Empirical Findings & Architectural Specification (knw-context-packing-p1-0001)

> Context Packing P1 Empirical Findings & Architectural Specification This knowledge document formalizes the empirical findings, measurements, failure modes, and architectural implications derived from the P1 Context Packing Laboratory executed over real AI Memory Vault data. --- 1. Executive Summary Empirical measurement of the retrieval-packing pipeline isolated PACKING FAILURE as the primary bottleneck preventing high candidate recall ($76.7\%$) from reaching generative LLM accuracy. Under the production baseline ( P0 ): - Packing Loss Rate : 76.1% of all discovered factual keywords were discarded or corrupted before reaching the LLM context. - Root causes in production ContextPackBuilder :

* **Question**: Reading only the note text below, does this note answer the query? yes / partial / no. In 'other_relevant_ids' list the id of any OTHER note in the vault that answers it better or equally well (optional).
* **Allowed labels**: `yes|partial|no`

## H1G|H1-DST-004|11e9fe55-6924-4231-9635-c5629bff6ae3  (priority 2, h1_gold_relevance)

* **Context**: [distractor, development] Which system stores consolidated long-term neural memory across extended timeframes?
* **Evidence**: long-term memory (11e9fe55-6924-4231-9635-c5629bff6ae3)

> long-term memory Canonical Definition The externalized store containing factual archives and operational manuals that guide agent execution without consuming continuous attention. Judgment & Evaluation Tier 2 cognitive concept with recurrence occurrences 10-19 across memory in the age of ai agents. Promoted to REVIEW status for ontology scaffolding. Code Cross-References Potentially relevant to implementation modules in 03 IMPLEMENTATION/ or procedures in 10 DOCUMENTATION/procedures/ , not independently verified this package.

* **Question**: Reading only the note text below, does this note answer the query? yes / partial / no. In 'other_relevant_ids' list the id of any OTHER note in the vault that answers it better or equally well (optional).
* **Allowed labels**: `yes|partial|no`

## H1G|H1-DST-005|slot-15-retrieval  (priority 2, h1_gold_relevance)

* **Context**: [distractor, calibration] Which ontology slot defines the memory search and candidate generation pipeline?
* **Evidence**: Ontology Slot: Retrieval (slot-15-retrieval)

> Ontology Slot: Retrieval Question How is relevant memory found? Theoretical sources - None (grounded in real code, not primarily literature) Validates module - 03 IMPLEMENTATION/packages/memory/controller.py (verified definition of MemoryController.search() at line 376; distinct from the 03 IMPLEMENTATION/packages/memory controller/ compatibility shim) Candidate concepts concept source book confidence status date added promoted note id evidence occurrences --- --- --- --- --- --- --- --- retrieval 2601.09113v1; memory in the age of ai agents; wcs 1488 0.95 promoted 2026-09-12 9e6da9df-7fca-4cf5-99bf-03fbdee15170 related concepts such as LLM memory, retrieval-augmented generation (RAG), and c

* **Question**: Reading only the note text below, does this note answer the query? yes / partial / no. In 'other_relevant_ids' list the id of any OTHER note in the vault that answers it better or equally well (optional).
* **Allowed labels**: `yes|partial|no`

## H1G|H1-DST-006|proc-reflexion-0001  (priority 2, h1_gold_relevance)

* **Context**: [distractor, calibration] What is the closed-loop protocol for self-reflection and multi-stage evaluation?
* **Evidence**: 🔄 Procedură Canonică: Pipeline de Reflecție Închisă (Closed-Loop Reflexion) (proc-reflexion-0001)

> 🔄 Procedură Canonică: Pipeline de Reflecție Închisă (Closed-Loop Reflexion) Această procedură definește ciclul automat de reflecție închisă ( Generate → Critique → Revise → Consolidate ) pentru conversia erorilor runtime în lecții și proceduri reutilizabile. --- 🔁 1. Ciclul de Reflecție în 4 Etape Etapa A: Captura Erorii - Când un task întâmpină un eșec sau o aserțiune picată, se creează o notă în 04 MEMORY/Errors/ cu type: error , lifecycle: REVIEW , log-ul complet nealterat și atribuirea cauzei rădăcină. Etapa B: Critica SelfRefine ( cognitive core/reflection.py ) - CriticAgent și VerifierAgent analizează traiectul eșecului prin masca de reguli SelfRefine : 1. Identificarea cauzei rădăcină

* **Question**: Reading only the note text below, does this note answer the query? yes / partial / no. In 'other_relevant_ids' list the id of any OTHER note in the vault that answers it better or equally well (optional).
* **Allowed labels**: `yes|partial|no`

## H1G|H1-DST-007|f82194d2-31a8-4c91-a83d-e42109ab7d12  (priority 1, h1_gold_relevance)

* **Context**: [distractor, held_out] What clean domain layers isolate business logic from MT5 API connectors in XAU_Kinetic?
* **Evidence**: XAU_Kinetic Clean Architecture Principles (f82194d2-31a8-4c91-a83d-e42109ab7d12)

> XAU Kinetic Clean Architecture Principles Overview XAU Kinetic is a institutional quantitative trading engine built with Clean Architecture in Python 3.12+. It separates concerns strictly between core domain entities, application orchestration, infrastructure drivers, risk management, and trading strategy decisions. Core Architectural Invariants 1. Zero-Trust Data Flow (Pydantic V2) All inputs and outputs crossing architectural boundaries use strict Pydantic V2 schemas ( SignalObject , Position , TickData , AccountInfo , OrderResult , AuditEvent ). No raw dictionaries or unvalidated floating point values are passed between strategy and risk modules. 2. Isolated Risk Veto Power RiskManager ac

* **Question**: Reading only the note text below, does this note answer the query? yes / partial / no. In 'other_relevant_ids' list the id of any OTHER note in the vault that answers it better or equally well (optional).
* **Allowed labels**: `yes|partial|no`

## H1G|H1-DST-008|idx-leg-eu-aiact-2024-1689  (priority 1, h1_gold_relevance)

* **Context**: [distractor, held_out] What European regulation establishes harmonized transparency rules for foundation models and artificial intelligence?
* **Evidence**: Index Structurat: Regulamentul (UE) 2024/1689 (AI Act) (idx-leg-eu-aiact-2024-1689)

> Index Structurat: Regulamentul (UE) 2024/1689 (AI Act) [!IMPORTANT] REGIM DE GUVERNANȚĂ & DISCLAIMER JURIDIC : Acest index este un instrument analitic structurat derivat din date normative externe ( instruction trust: NONE ). Nu reprezintă asistență juridică, consultanță sau politică activă. Statut: lifecycle: REVIEW / verification: verified source . Nu este promovat în ACTIVE fără aprobare umană prealabilă. 1. Definiții Cheie (Art. 3) - Sistem de IA (Art. 3 pct. 1) : Sistem bazat pe mașină conceput să funcționeze cu diferite niveluri de autonomie și care poate genera rezultate precum previziuni, recomandări sau decizii. - Model de IA de uz general — GPAI (Art. 3 pct. 63) : Model de IA cu ca

* **Question**: Reading only the note text below, does this note answer the query? yes / partial / no. In 'other_relevant_ids' list the id of any OTHER note in the vault that answers it better or equally well (optional).
* **Allowed labels**: `yes|partial|no`

## H1G|H1-DST-009|proc-stats-reporting-0001  (priority 1, h1_gold_relevance)

* **Context**: [distractor, held_out] What standard prevents reporting deceptive percentage deltas over tiny evaluation samples?
* **Evidence**: 📊 Procedură Canonică: Raportarea Proporțiilor pe Eșantioane Mici în Evaluări și Benchmark-uri (proc-stats-reporting-0001)

> 📊 Procedură Canonică: Raportarea Proporțiilor pe Eșantioane Mici în Evaluări și Benchmark-uri Această procedură stabilește standardul canonic obligatoriu pentru raportarea proporțiilor, ratelor de succes și transferului cognitiv în rapoartele de evaluare și benchmark ale AI Memory Vault. Standardul derivă direct din principiile matematice de inferență statistică predate în OpenStax Introductory Statistics 2e , Capitolul 8 ( Confidence Intervals ). --- 🎯 1. Principiu de Bază O proporție empirică obținută pe un eșantion finit (ex: $\hat{p} = 6/12 = 50\%$) este o estimare punctuală , nu un parametru fix. Fără precizarea dimensiunii eșantionului $n$ și a intervalului de încredere asociat, orice

* **Question**: Reading only the note text below, does this note answer the query? yes / partial / no. In 'other_relevant_ids' list the id of any OTHER note in the vault that answers it better or equally well (optional).
* **Allowed labels**: `yes|partial|no`

## H1G|H1-DST-010|fd63129c-091c-4171-9eb4-b7c28190b1a4  (priority 1, h1_gold_relevance)

* **Context**: [distractor, held_out] What retrospective lesson describes when two independently correct fixes cancel each other?
* **Evidence**: Two independently correct changes can cancel each other and report success (fd63129c-091c-4171-9eb4-b7c28190b1a4)

> Two independently correct changes can cancel each other and report success Problem Graph expansion ran on every query, reported status ok , and added exactly zero nodes. No test failed, no error appeared, and the graph-on arm was silently identical to the baseline. How it was found The expansion budget was min(2 len(notes), 20) - len(notes) . A separate, correct change had raised the lexical candidate limit from a small number to 200, which makes that expression negative. Every test still passed because the tests use small mock corpora where the formula behaves. The contradiction that exposed it was a metric that cannot happen: context recall above candidate recall, meaning a note reached th

* **Question**: Reading only the note text below, does this note answer the query? yes / partial / no. In 'other_relevant_ids' list the id of any OTHER note in the vault that answers it better or equally well (optional).
* **Allowed labels**: `yes|partial|no`

## PN|0479ee1a-44b8-41b8-b608-0c232f2f9a31  (priority 1, candidate_note_faithfulness)

* **Context**: term: experience replay   (claimed confidence: high)
* **Evidence**: experience replay (0479ee1a-44b8-41b8-b608-0c232f2f9a31)

> DEFINITION: Definește tehnica și procesul cognitiv activ de re-eșantionare și re-traversare a traiectoriilor istorice stocate pentru actualizarea reprezentărilor fără uitare catastrofică, reprezentând un mecanism distinct de consolidare offline.
> SOURCE PASSAGE: , 2022 SYNERGY BETWEEN SYNAPTIC CONSOLIDATION AND EXPERIENCE REPLAY FOR GENERAL CONTINUAL LEARNING Fahad Sarfraz∗, Elahe Arani*, Bahram Zonooz Advanced Research Lab, NavInfo Europe,

* **Question**: Is the definition faithful to the quoted source passage (says what the passage supports, adds nothing it does not)? faithful / partial / unfaithful, or not_a_term if the heading is not a meaningful concept (an extraction artefact).
* **Allowed labels**: `faithful|partial|unfaithful|not_a_term`

## PN|07d27579-d273-4b81-b2b7-25ad820f2535  (priority 1, candidate_note_faithfulness)

* **Context**: term: subsymbolic   (claimed confidence: high)
* **Evidence**: subsymbolic (07d27579-d273-4b81-b2b7-25ad820f2535)

> DEFINITION: Definește cantitățile continue matematice (valori de activare, forțe asociative) care moderează accesibilitatea și viteza de regăsire a structurilor simbolice, reprezentând un palier computațional indispensabil în slotul ontology.
> SOURCE PASSAGE: c components that are accessible to the model and subsymbolic information that moderates behavior and accessibility of the declarative memory. Accessing them increases their

* **Question**: Is the definition faithful to the quoted source passage (says what the passage supports, adds nothing it does not)? faithful / partial / unfaithful, or not_a_term if the heading is not a meaningful concept (an extraction artefact).
* **Allowed labels**: `faithful|partial|unfaithful|not_a_term`

## PN|0f6d608d-2737-49b4-9818-a695ab43eb61  (priority 1, candidate_note_faithfulness)

* **Context**: term: dispersion   (claimed confidence: high)
* **Evidence**: dispersion (0f6d608d-2737-49b4-9818-a695ab43eb61)

> DEFINITION: Descrie proprietatea topologică cibernetică de dispersie a conexiunilor care izolează subsistemele și previne interferența generalizată în timpul învățării, aparținând organizării structurale a hărții.
> SOURCE PASSAGE: Not only does random dispersion lead to the intermingling of subsystems, with abundant chances of random interaction and confusion, but even more confusion is added with every fresh act of learning.

* **Question**: Is the definition faithful to the quoted source passage (says what the passage supports, adds nothing it does not)? faithful / partial / unfaithful, or not_a_term if the heading is not a meaningful concept (an extraction artefact).
* **Allowed labels**: `faithful|partial|unfaithful|not_a_term`

## PN|0f9affc9-8fac-415c-b059-31a23a284926  (priority 1, candidate_note_faithfulness)

* **Context**: term: cross-exclusion   (claimed confidence: high)
* **Evidence**: cross-exclusion (0f9affc9-8fac-415c-b059-31a23a284926)

> DEFINITION: Definește mecanismul de inhibiție reciprocă concurențială prin care activarea unei agenții suspendă automat rutarea și formarea de memorie în agențiile rivale, prevenind conflictele de control.
> SOURCE PASSAGE: We can accomplish that by building them into a cross-exclusion system so that, for example, Hunger’s memories can be formed only when Hunger is active.

* **Question**: Is the definition faithful to the quoted source passage (says what the passage supports, adds nothing it does not)? faithful / partial / unfaithful, or not_a_term if the heading is not a meaningful concept (an extraction artefact).
* **Allowed labels**: `faithful|partial|unfaithful|not_a_term`

## PN|2543b0b9-8580-423f-9feb-0f38ccb87fa4  (priority 1, candidate_note_faithfulness)

* **Context**: term: schema   (claimed confidence: high)
* **Evidence**: schema (2543b0b9-8580-423f-9feb-0f38ccb87fa4)

> DEFINITION: Definește structura cognitivă de cunoștințe generale organizate pe roluri și scenarii care oferă scheletul de interpretare a evenimentelor noi, constituind o formă canonică de reprezentare în slotul ontology.
> SOURCE PASSAGE: The Impact of Missing or Incorrect Schemas - The COVID-19 pandemic created scenarios that disrupted established schemas, making familiar situations feel uncomfortable and requiring additional mental effort to navigate.

* **Question**: Is the definition faithful to the quoted source passage (says what the passage supports, adds nothing it does not)? faithful / partial / unfaithful, or not_a_term if the heading is not a meaningful concept (an extraction artefact).
* **Allowed labels**: `faithful|partial|unfaithful|not_a_term`

## PN|2831032f-0fe9-44d6-b9e8-8214284babd7  (priority 1, candidate_note_faithfulness)

* **Context**: term: mnemonic   (claimed confidence: high)
* **Evidence**: mnemonic (2831032f-0fe9-44d6-b9e8-8214284babd7)

> DEFINITION: Definește abilitatea procedurală dobândită de transformare deliberată a informațiilor abstracte în ancore vizuale sau narative durabile, constituind un skill cognitiv formal de memorare.
> SOURCE PASSAGE: Mark Twain's Memory Techniques Mark Twain utilized various mnemonic systems to enhance his memory, notably employing visual cues linked to places, such as drawing pictures to remember stories and organizing them by spatial memory.

* **Question**: Is the definition faithful to the quoted source passage (says what the passage supports, adds nothing it does not)? faithful / partial / unfaithful, or not_a_term if the heading is not a meaningful concept (an extraction artefact).
* **Allowed labels**: `faithful|partial|unfaithful|not_a_term`

## PN|29fe72cf-57bb-4a5e-a912-3eb8f945e05f  (priority 1, candidate_note_faithfulness)

* **Context**: term: b-brain   (claimed confidence: high)
* **Evidence**: b-brain (29fe72cf-57bb-4a5e-a912-3eb8f945e05f)

> DEFINITION: Definește arhitectura reflectivă de ordinul doi a lui Minsky în care un modul separat monitorizează exclusiv configurațiile și performanțele interne ale procesorului principal, fără contact direct cu stimulii externi.
> SOURCE PASSAGE: There is one way for a mind to watch itself and still keep track of what’s happening. Divide é the brain into two parts, A and B. Connect the A-brain’s inputs and outputs to the real world— E so it can sense what happens there.

* **Question**: Is the definition faithful to the quoted source passage (says what the passage supports, adds nothing it does not)? faithful / partial / unfaithful, or not_a_term if the heading is not a meaningful concept (an extraction artefact).
* **Allowed labels**: `faithful|partial|unfaithful|not_a_term`

## PN|2c1ed306-faa7-48a7-bf28-95917ef959fe  (priority 1, candidate_note_faithfulness)

* **Context**: term: law of requisite variety   (claimed confidence: high)
* **Evidence**: law of requisite variety (2c1ed306-faa7-48a7-bf28-95917ef959fe)

> DEFINITION: Definește legea matematică cibernetică universală conform căreia capacitatea unui regulator de a bloca perturbațiile este limitată strict de varietatea stărilor sale interne, răspunzând direct cerințelor de frontieră din slotul constraints.
> SOURCE PASSAGE: complexity, with climate, soil, host’s reactions, predalaw of Requisite Variety is Likely to play a dominating part. Its tors, competitors, and many other factors playing a part. The importance is

* **Question**: Is the definition faithful to the quoted source passage (says what the passage supports, adds nothing it does not)? faithful / partial / unfaithful, or not_a_term if the heading is not a meaningful concept (an extraction artefact).
* **Allowed labels**: `faithful|partial|unfaithful|not_a_term`

## PN|2ecc82e6-a508-4db9-9d62-60b9062da602  (priority 1, candidate_note_faithfulness)

* **Context**: term: relearning   (claimed confidence: high)
* **Evidence**: relearning (2ecc82e6-a508-4db9-9d62-60b9062da602)

> DEFINITION: Captures the quantitative savings metric of latent memory retention, measuring reacquisition speed and demonstrating structural persistence below the explicit retrieval threshold.
> SOURCE PASSAGE: how quickly she’s able to pick up the language after not speaking it for 13 years; this is an example of relearning.

* **Question**: Is the definition faithful to the quoted source passage (says what the passage supports, adds nothing it does not)? faithful / partial / unfaithful, or not_a_term if the heading is not a meaningful concept (an extraction artefact).
* **Allowed labels**: `faithful|partial|unfaithful|not_a_term`

## PN|3a70589a-5778-4a51-a822-e0fcd5f0effa  (priority 1, candidate_note_faithfulness)

* **Context**: term: k-line   (claimed confidence: high)
* **Evidence**: k-line (3a70589a-5778-4a51-a822-e0fcd5f0effa)

> DEFINITION: Definește mecanismul structural al lui Minsky de conectare mnemonică ce reactivează simultan constelația specifică de agenți care au participat la rezolvarea cu succes a unei probleme anterioare.
> SOURCE PASSAGE: A K-line is a wirelike structure that attaches / itself to whichever mental agents are active when you solve a problem or have a / good idea.

* **Question**: Is the definition faithful to the quoted source passage (says what the passage supports, adds nothing it does not)? faithful / partial / unfaithful, or not_a_term if the heading is not a meaningful concept (an extraction artefact).
* **Allowed labels**: `faithful|partial|unfaithful|not_a_term`

## PN|3d14b6f4-c885-409e-978a-fcffb8a2036a  (priority 1, candidate_note_faithfulness)

* **Context**: term: generalization   (claimed confidence: high)
* **Evidence**: generalization (3d14b6f4-c885-409e-978a-fcffb8a2036a)

> DEFINITION: Definește metrica de certitudine epistemică și transfer out-of-distribution a reprezentărilor dincolo de eșantioanele imediate de antrenare, completând slotul confidence cu o măsură cantitativă de robustețe a cunoștințelor.
> SOURCE PASSAGE: of-the-art rehearsal based approaches or provides generalization gains in almost all the considered scenarios. Particularly in the challenging Class-IL setting with low buffer size

* **Question**: Is the definition faithful to the quoted source passage (says what the passage supports, adds nothing it does not)? faithful / partial / unfaithful, or not_a_term if the heading is not a meaningful concept (an extraction artefact).
* **Allowed labels**: `faithful|partial|unfaithful|not_a_term`

## PN|43b9d288-3632-4aa1-9eb1-77fc54ca1c2a  (priority 1, candidate_note_faithfulness)

* **Context**: term: habituation   (claimed confidence: high)
* **Evidence**: habituation (43b9d288-3632-4aa1-9eb1-77fc54ca1c2a)

> DEFINITION: Definește procedura elementară de învățare non-asociativă manifestată prin reducerea eliberării de transmițători la stimulări repetate inofensive, completând repertoriul de proceduri reflexe alături de 'sensitization' și 'classical conditioning'.
> SOURCE PASSAGE: 28 CHAPTER TWO Studies of habituation in simple experimental animals provided the first evidence of how learn- ing and memory storage take place in the brain.

* **Question**: Is the definition faithful to the quoted source passage (says what the passage supports, adds nothing it does not)? faithful / partial / unfaithful, or not_a_term if the heading is not a meaningful concept (an extraction artefact).
* **Allowed labels**: `faithful|partial|unfaithful|not_a_term`

## PN|43f78207-5f51-4c8d-9c71-1b154394b9e6  (priority 1, candidate_note_faithfulness)

* **Context**: term: perception   (claimed confidence: high)
* **Evidence**: perception (43f78207-5f51-4c8d-9c71-1b154394b9e6)

> DEFINITION: Definește abilitatea și canalul cognitiv de conversie a semnalelor senzoriale brute în reprezentări structurate (chunks) compatibile cu producțiile sistemului, reprezentând o competență de bază în skills.
> SOURCE PASSAGE: or, for example, shifting visual attention. These perception modules are routinely expanded by researchers to accommodate other capabilities like hearing. These results can be

* **Question**: Is the definition faithful to the quoted source passage (says what the passage supports, adds nothing it does not)? faithful / partial / unfaithful, or not_a_term if the heading is not a meaningful concept (an extraction artefact).
* **Allowed labels**: `faithful|partial|unfaithful|not_a_term`

## PN|44dabf4a-e6b7-48f6-b8d3-3d271b93ea0e  (priority 1, candidate_note_faithfulness)

* **Context**: term: production rule   (claimed confidence: high)
* **Evidence**: production rule (44dabf4a-e6b7-48f6-b8d3-3d271b93ea0e)

> DEFINITION: Definește unitatea fundamentală de execuție de tip condiție-acțiune din sistemele de producție care citește și modifică memoria de lucru secvențial, aducând o primitivă esențială în slotul procedures.
> SOURCE PASSAGE: uction system During the decision cycle in ACT-R, production rules are used to analyze patterns in all of the buffers that may represent internal

* **Question**: Is the definition faithful to the quoted source passage (says what the passage supports, adds nothing it does not)? faithful / partial / unfaithful, or not_a_term if the heading is not a meaningful concept (an extraction artefact).
* **Allowed labels**: `faithful|partial|unfaithful|not_a_term`

## PN|452cc1f0-e03b-42ea-80d3-5f00e944ed8f  (priority 1, candidate_note_faithfulness)

* **Context**: term: synaptic plasticity   (claimed confidence: high)
* **Evidence**: synaptic plasticity (452cc1f0-e03b-42ea-80d3-5f00e944ed8f)

> DEFINITION: Definește mecanismul bio-computațional de modificare a forței de transmisie a joncțiunilor în funcție de activitate, oferind fundamentul fizic/structural prin care experiența se transformă în stocare permanentă în slotul consolidation.
> SOURCE PASSAGE: A SIMPLE CASE OF SYNAPTIC PLASTICITY The first attempt at the neural analysis of habitu- ation was undertaken as early as 1908, in the course of studies focusing on the isolated spinal cord of the cat.

* **Question**: Is the definition faithful to the quoted source passage (says what the passage supports, adds nothing it does not)? faithful / partial / unfaithful, or not_a_term if the heading is not a meaningful concept (an extraction artefact).
* **Allowed labels**: `faithful|partial|unfaithful|not_a_term`

## PN|48be737f-d50d-4f30-b55a-3aec9877e6da  (priority 1, candidate_note_faithfulness)

* **Context**: term: coupling   (claimed confidence: high)
* **Evidence**: coupling (48be737f-d50d-4f30-b55a-3aec9877e6da)

> DEFINITION: Definește canalul cibernetic și protocolul de interconectare funcțională prin care controlerul direcționează și influențează subsistemele regulatoare, răspunzând direct la 'când și cum e direcționat fluxul'.
> SOURCE PASSAGE: and the mode of assembling and coupling selected to show one end of the scale). Other cases lie further has to be selected from the other, incorrect, modes. The quantity

* **Question**: Is the definition faithful to the quoted source passage (says what the passage supports, adds nothing it does not)? faithful / partial / unfaithful, or not_a_term if the heading is not a meaningful concept (an extraction artefact).
* **Allowed labels**: `faithful|partial|unfaithful|not_a_term`

## PN|4c109b4e-7ce1-4ee5-873e-b525ebe5d1ba  (priority 1, candidate_note_faithfulness)

* **Context**: term: problem space   (claimed confidence: high)
* **Evidence**: problem space (4c109b4e-7ce1-4ee5-873e-b525ebe5d1ba)

> DEFINITION: Definește arena topologică universală postulată de Newell în care are loc deliberarea cognitivă prin tranziții incrementale între descrieri de stări, răspunzând la întrebarea 'unde trăiește informația'.
> SOURCE PASSAGE: This uniform use of problem spaces as the task representation is called the problem space hypothesis (Newell, l980c)

* **Question**: Is the definition faithful to the quoted source passage (says what the passage supports, adds nothing it does not)? faithful / partial / unfaithful, or not_a_term if the heading is not a meaningful concept (an extraction artefact).
* **Allowed labels**: `faithful|partial|unfaithful|not_a_term`

## PN|4fec1482-ab22-4b34-aa94-07a5dddeb6b2  (priority 1, candidate_note_faithfulness)

* **Context**: term: goal   (claimed confidence: high)
* **Evidence**: goal (4fec1482-ab22-4b34-aa94-07a5dddeb6b2)

> DEFINITION: Definește structura cognitivă activă din memoria de lucru care menține contextul obiectivului curent și filtrează regulile relevante în timpul execuției, constituind componenta cheie din slotul state.
> SOURCE PASSAGE: derson, Farrell, and Sauers (1984) formulated the Goal-Restricted Production System (GRAPES) to implement these latest developments to the theory. GRAPES is marked as the

* **Question**: Is the definition faithful to the quoted source passage (says what the passage supports, adds nothing it does not)? faithful / partial / unfaithful, or not_a_term if the heading is not a meaningful concept (an extraction artefact).
* **Allowed labels**: `faithful|partial|unfaithful|not_a_term`

## PN|51edb1be-9b8d-49cb-a8ff-f9ae5d0732e0  (priority 1, candidate_note_faithfulness)

* **Context**: term: collective memory   (claimed confidence: high)
* **Evidence**: collective memory (51edb1be-9b8d-49cb-a8ff-f9ae5d0732e0)

> DEFINITION: Definește constructul social și relațional prin care mai mulți agenți coordonează narative comune despre istorie și își aliniază perspectivele mutuale, încadrându-se direct în slotul relationships.
> SOURCE PASSAGE: Answer:Collective memory shapes the narratives we share about historical events, such as wars.

* **Question**: Is the definition faithful to the quoted source passage (says what the passage supports, adds nothing it does not)? faithful / partial / unfaithful, or not_a_term if the heading is not a meaningful concept (an extraction artefact).
* **Allowed labels**: `faithful|partial|unfaithful|not_a_term`

## PN|54008569-384d-406f-aa47-f21a90d138e0  (priority 1, candidate_note_faithfulness)

* **Context**: term: frame   (claimed confidence: high)
* **Evidence**: frame (54008569-384d-406f-aa47-f21a90d138e0)

> DEFINITION: Definește structura de date topologică și situațională a lui Minsky cu terminale relaționale și valori implicite, oferind scheletul de mapare a scenelor și contextelor în slotul map.
> SOURCE PASSAGE: When every change af view engages frames whose terminals are already filled, albeit only by default, then sight seems instantaneous.

* **Question**: Is the definition faithful to the quoted source passage (says what the passage supports, adds nothing it does not)? faithful / partial / unfaithful, or not_a_term if the heading is not a meaningful concept (an extraction artefact).
* **Allowed labels**: `faithful|partial|unfaithful|not_a_term`

## PN|675a43b3-6ac0-4b69-82d6-1c207cab43c6  (priority 1, candidate_note_faithfulness)

* **Context**: term: proactive interference   (claimed confidence: high)
* **Evidence**: proactive interference (675a43b3-6ac0-4b69-82d6-1c207cab43c6)

> DEFINITION: Directly formalizes the directional memory retrieval constraint where previously consolidated knowledge impairs the acquisition or recall of new information, providing the psychological foundation for CognitiveInterferenceGate.
> SOURCE PASSAGE: These are examples of proactive interference: when old information hinders the recall of newly learned information.

* **Question**: Is the definition faithful to the quoted source passage (says what the passage supports, adds nothing it does not)? faithful / partial / unfaithful, or not_a_term if the heading is not a meaningful concept (an extraction artefact).
* **Allowed labels**: `faithful|partial|unfaithful|not_a_term`

## PN|6dd28a01-e257-4065-a481-f2c3cb5d1659  (priority 1, candidate_note_faithfulness)

* **Context**: term: multistable system   (claimed confidence: high)
* **Evidence**: multistable system (6dd28a01-e257-4065-a481-f2c3cb5d1659)

> DEFINITION: Definește clasa ontologică de sisteme compuse din părți ultrastabile cuplate slab care se pot adapta succesiv la medii variate, completând taxonomia sistemelor dinamice alături de 'state-determined system'.
> SOURCE PASSAGE: The suggestion now before the reader is that the system of Figure 7/5/1, when looked at more closely in the forms in which it occurs in actual organisms and environments, will be found to break up into parts more like those of Figure 16/6/1 the multistable system.

* **Question**: Is the definition faithful to the quoted source passage (says what the passage supports, adds nothing it does not)? faithful / partial / unfaithful, or not_a_term if the heading is not a meaningful concept (an extraction artefact).
* **Allowed labels**: `faithful|partial|unfaithful|not_a_term`

## PN|70c8724d-1c24-43d6-9de1-1282f7fe0fbb  (priority 1, candidate_note_faithfulness)

* **Context**: term: retrieval cue   (claimed confidence: high)
* **Evidence**: retrieval cue (70c8724d-1c24-43d6-9de1-1282f7fe0fbb)

> DEFINITION: Definește semnalul sau cheia de interogare externă/internă care declanșează accesul asociativ rapid la rețelele de memorie autobiografică sau semantică, completând mecanismele din slotul retrieval.
> SOURCE PASSAGE: The smell serves as a retrieval cue, triggering a memory that brings back not only the aroma of onions but all associated sensations and emotions you experienced during breakfast, like seeing the newspaper headlines and hearing the song 'Imagine'.

* **Question**: Is the definition faithful to the quoted source passage (says what the passage supports, adds nothing it does not)? faithful / partial / unfaithful, or not_a_term if the heading is not a meaningful concept (an extraction artefact).
* **Allowed labels**: `faithful|partial|unfaithful|not_a_term`

## PN|72cf123b-b8b3-4d22-94ba-388fd72e20f1  (priority 1, candidate_note_faithfulness)

* **Context**: term: canonical representation   (claimed confidence: high)
* **Evidence**: canonical representation (72cf123b-b8b3-4d22-94ba-388fd72e20f1)

> DEFINITION: Definește expresia matematică standardizată prin care un sistem dinamic modelează transformările discrete ale stărilor sale, oferind o schemă riguroasă de tipizare ontologică.
> SOURCE PASSAGE: discussed so far transformation is the canonical representation of the machine, change by discrete jumps. These discrete transformations are, and the machine is said to embody the transformation. however, the

* **Question**: Is the definition faithful to the quoted source passage (says what the passage supports, adds nothing it does not)? faithful / partial / unfaithful, or not_a_term if the heading is not a meaningful concept (an extraction artefact).
* **Allowed labels**: `faithful|partial|unfaithful|not_a_term`

## PN|7c22c390-3ae4-43d7-b8b4-ff1ddf5ceecf  (priority 1, candidate_note_faithfulness)

* **Context**: term: censor   (claimed confidence: high)
* **Evidence**: censor (7c22c390-3ae4-43d7-b8b4-ff1ddf5ceecf)

> DEFINITION: Descrie dispozitivul de memorie inhibitorie care reține circumstanțele specifice de eșec pentru a suprima preventiv activarea acțiunilor care duc la blocaje, oferind o primitivă clară de constrângere decizională.
> SOURCE PASSAGE: special memory devices called “censors” and “suppressors” (we'll discuss this in detail later), which remember particular circumstances in which M fails and later proceed to suppress M when similar conditions recur.

* **Question**: Is the definition faithful to the quoted source passage (says what the passage supports, adds nothing it does not)? faithful / partial / unfaithful, or not_a_term if the heading is not a meaningful concept (an extraction artefact).
* **Allowed labels**: `faithful|partial|unfaithful|not_a_term`

## PN|7e93f968-16e9-4d3a-bde3-cf8e417dfc9d  (priority 1, candidate_note_faithfulness)

* **Context**: term: numeric preference   (claimed confidence: high)
* **Evidence**: numeric preference (7e93f968-16e9-4d3a-bde3-cf8e417dfc9d)

> DEFINITION: Definește reprezentarea formală a utilității cantitative și valorii de politică pentru rezolvarea preferințelor între acțiuni candidate în ciclul decizional din Soar, răspunzând direct la întrebarea slotului judgement.
> SOURCE PASSAGE: If the new rule creates a numeric preference, it is automatically tuned in the future by RL, so that the value function is dynamically extended through a combination of processing in a substate and chunking (Laird, Derbinsky, and Tinkerhess 2011).

* **Question**: Is the definition faithful to the quoted source passage (says what the passage supports, adds nothing it does not)? faithful / partial / unfaithful, or not_a_term if the heading is not a meaningful concept (an extraction artefact).
* **Allowed labels**: `faithful|partial|unfaithful|not_a_term`

## PN|7f381c04-caa8-49aa-9904-2c88ba54506b  (priority 1, candidate_note_faithfulness)

* **Context**: term: transience   (claimed confidence: high)
* **Evidence**: transience (7f381c04-caa8-49aa-9904-2c88ba54506b)

> DEFINITION: Defines the temporal attenuation and decay of memory trace accessibility over time, directly modeling the power-law decay of base-level activation in ACT-R retrieval.
> SOURCE PASSAGE: Let’s look at the first sin of the forgetting errors: transience, which means that memories can fade over time.

* **Question**: Is the definition faithful to the quoted source passage (says what the passage supports, adds nothing it does not)? faithful / partial / unfaithful, or not_a_term if the heading is not a meaningful concept (an extraction artefact).
* **Allowed labels**: `faithful|partial|unfaithful|not_a_term`

## PN|8a12b84d-ec27-44bd-a16c-5a4125b49742  (priority 1, candidate_note_faithfulness)

* **Context**: term: elaborative rehearsal   (claimed confidence: high)
* **Evidence**: elaborative rehearsal (8a12b84d-ec27-44bd-a16c-5a4125b49742)

> DEFINITION: Formalizes the intentional semantic linking of novel items to established conceptual structures, representing the active procedural mechanism of memory consolidation.
> SOURCE PASSAGE: You could also enhance memory by using elaborative rehearsal: a technique in which you think about the meaning of new information and its relation to knowledge already stored in your memory

* **Question**: Is the definition faithful to the quoted source passage (says what the passage supports, adds nothing it does not)? faithful / partial / unfaithful, or not_a_term if the heading is not a meaningful concept (an extraction artefact).
* **Allowed labels**: `faithful|partial|unfaithful|not_a_term`

## PN|8f2f1707-194c-4e18-b080-6058f1c62d4f  (priority 1, candidate_note_faithfulness)

* **Context**: term: exploration   (claimed confidence: high)
* **Evidence**: exploration (8f2f1707-194c-4e18-b080-6058f1c62d4f)

> DEFINITION: Definește competența și comportamentul activ de căutare deliberată a stărilor slab eșantionate din medii parțial observabile, constituind un skill critic de achiziție în învățarea prin întărire.
> SOURCE PASSAGE: ioned in the challenging set that emphasizes hard exploration tasks with long-term credit assignment [Badia et al., 2020] (Table 7). These results indicate the

* **Question**: Is the definition faithful to the quoted source passage (says what the passage supports, adds nothing it does not)? faithful / partial / unfaithful, or not_a_term if the heading is not a meaningful concept (an extraction artefact).
* **Allowed labels**: `faithful|partial|unfaithful|not_a_term`

## PN|9274b72b-ea97-4f5c-b235-2416a190cace  (priority 1, candidate_note_faithfulness)

* **Context**: term: pronome   (claimed confidence: high)
* **Evidence**: pronome (9274b72b-ea97-4f5c-b235-2416a190cace)

> DEFINITION: Definește mecanismul lui Minsky de pointeri temporari din memoria de lucru care leagă roluri tranzitorii de agenții activi fără modificarea conexiunilor permanente, răspunzând la 'ce este activ acum'.
> SOURCE PASSAGE: The basic difference, then, between a pronome and a K-line is that a pronome’s connections are temporary rather than permanent.

* **Question**: Is the definition faithful to the quoted source passage (says what the passage supports, adds nothing it does not)? faithful / partial / unfaithful, or not_a_term if the heading is not a meaningful concept (an extraction artefact).
* **Allowed labels**: `faithful|partial|unfaithful|not_a_term`

## PN|a2607d49-d1be-495b-9a28-32bfda843693  (priority 1, candidate_note_faithfulness)

* **Context**: term: false memory   (claimed confidence: high)
* **Evidence**: false memory (a2607d49-d1be-495b-9a28-32bfda843693)

> DEFINITION: Definește distorsiunea cognitivă și eșecul de judecată a realității surselor în reamintire, oferind o categorie ontologică esențială pentru diagnosticarea halucinațiilor și verificarea epistemică în slotul judgement.
> SOURCE PASSAGE: while learning new information can reduce errors in recall and help combat false memories.

* **Question**: Is the definition faithful to the quoted source passage (says what the passage supports, adds nothing it does not)? faithful / partial / unfaithful, or not_a_term if the heading is not a meaningful concept (an extraction artefact).
* **Allowed labels**: `faithful|partial|unfaithful|not_a_term`

## PN|ab693c77-2325-48a2-ae99-c869dc72782a  (priority 1, candidate_note_faithfulness)

* **Context**: term: reflection   (claimed confidence: high)
* **Evidence**: reflection (ab693c77-2325-48a2-ae99-c869dc72782a)

> DEFINITION: Definește procesul metacognitiv de auto-evaluare prin care un agent auditează propriile urme de execuție pentru diagnosticarea erorilor și extragerea corecțiilor, răspunzând direct la întrebarea slotului judgement.
> SOURCE PASSAGE: Few-shot prompting e.g., CoT, PALM - Self-Reflection e.g., Self-refine, CRITIC - KV compression/reuse e.g., AutoCompressor, SnapKV - Attention KV management e.g., Mixture-of-Memory - Long context processing e.g., Mamba, Memformer, MoA,

* **Question**: Is the definition faithful to the quoted source passage (says what the passage supports, adds nothing it does not)? faithful / partial / unfaithful, or not_a_term if the heading is not a meaningful concept (an extraction artefact).
* **Allowed labels**: `faithful|partial|unfaithful|not_a_term`

## PN|adca33fa-2bdc-48ee-bf9b-dbe70c26d20b  (priority 1, candidate_note_faithfulness)

* **Context**: term: Markov chain   (claimed confidence: high)
* **Evidence**: Markov chain (adca33fa-2bdc-48ee-bf9b-dbe70c26d20b)

> DEFINITION: Definește formalismul matematic stocastic de modelare a dinamicii sistemelor prin matrici de tranziție între stări discrete, oferind un model canonic de reprezentare a stărilor succesive.
> SOURCE PASSAGE: 4 5 YR RR Re THE MARKOV CHAIN Is the particular transformation so obtained determinate or not? (Hint: Is it 9/4. After eight chapters, we now know something about howa

* **Question**: Is the definition faithful to the quoted source passage (says what the passage supports, adds nothing it does not)? faithful / partial / unfaithful, or not_a_term if the heading is not a meaningful concept (an extraction artefact).
* **Allowed labels**: `faithful|partial|unfaithful|not_a_term`

## PN|afb5de0a-9538-42cc-ab49-618f890e8867  (priority 1, candidate_note_faithfulness)

* **Context**: term: misinformation effect   (claimed confidence: high)
* **Evidence**: misinformation effect (afb5de0a-9538-42cc-ab49-618f890e8867)

> DEFINITION: Captures reconstructive memory distortion caused by post-event misleading cues, acting as an essential guardrail for epistemic judgment and memory integrity.
> SOURCE PASSAGE: Loftus also developed the misinformation effect paradigm, which holds that after exposure to additional and possibly inaccurate information, a person may misremember the original event.

* **Question**: Is the definition faithful to the quoted source passage (says what the passage supports, adds nothing it does not)? faithful / partial / unfaithful, or not_a_term if the heading is not a meaningful concept (an extraction artefact).
* **Allowed labels**: `faithful|partial|unfaithful|not_a_term`

## PN|b0f011f3-960e-4089-b269-042c0180bc03  (priority 1, candidate_note_faithfulness)

* **Context**: term: isomorphism   (claimed confidence: high)
* **Evidence**: isomorphism (b0f011f3-960e-4089-b269-042c0180bc03)

> DEFINITION: Definește maparea bijectivă riguroasă de conservare a structurii și comportamentului între două mașini dinamice, fiind criteriul matematic formal de mapare structurală între sisteme.
> SOURCE PASSAGE: INTRODUCTION TO CYBERNETICS THE BLACK BOX ISOMORPHIC MACHINES through a spring S to a heavy wheel M, which is rigidly connected A : A he output shaft O. O’s degree

* **Question**: Is the definition faithful to the quoted source passage (says what the passage supports, adds nothing it does not)? faithful / partial / unfaithful, or not_a_term if the heading is not a meaningful concept (an extraction artefact).
* **Allowed labels**: `faithful|partial|unfaithful|not_a_term`

## PN|b48091d9-4ede-47cf-ada1-6907ff50a363  (priority 1, candidate_note_faithfulness)

* **Context**: term: associative memory   (claimed confidence: high)
* **Evidence**: associative memory (b48091d9-4ede-47cf-ada1-6907ff50a363)

> DEFINITION: Definește mecanismul de regăsire adresabil prin conținut (content-addressable memory) în care înregistrările sunt accesate pe baza similarității semantice sau suprapunerii de trăsături, aparținând esenței slotului retrieval.
> SOURCE PASSAGE: encompassing self-knowledge, facts, commonsense, associative memory, and other related elements, which collectively enable the generation of contextually relevant responses across a variety of tasks.

* **Question**: Is the definition faithful to the quoted source passage (says what the passage supports, adds nothing it does not)? faithful / partial / unfaithful, or not_a_term if the heading is not a meaningful concept (an extraction artefact).
* **Allowed labels**: `faithful|partial|unfaithful|not_a_term`

## PN|b4b53073-d620-4715-b780-a96cde3621d9  (priority 1, candidate_note_faithfulness)

* **Context**: term: perceptual representation system   (claimed confidence: high)
* **Evidence**: perceptual representation system (b4b53073-d620-4715-b780-a96cde3621d9)

> DEFINITION: Definește sistemul cortical pre-semantic (PRS) care păstrează hărțile structurale ale stimulilor vizuali și auditivi upstream de memoria declarativă, acoperind reprezentarea perceptuală în slotul map.
> SOURCE PASSAGE: The main reason for invoking the term "perceptual representation system'' is to emphasize the notion that all of the various subsystems are tied together by common properties and rules of operations: they are cortically based, operate at a presemantic level on domain-specific perceptual information,

* **Question**: Is the definition faithful to the quoted source passage (says what the passage supports, adds nothing it does not)? faithful / partial / unfaithful, or not_a_term if the heading is not a meaningful concept (an extraction artefact).
* **Allowed labels**: `faithful|partial|unfaithful|not_a_term`

## PN|bafb06ff-e437-4d23-b576-af8df6defc63  (priority 1, candidate_note_faithfulness)

* **Context**: term: default assumption   (claimed confidence: high)
* **Evidence**: default assumption (bafb06ff-e437-4d23-b576-af8df6defc63)

> DEFINITION: Desemnează mecanismul euristic de atribuire a valorilor pre-configurate în terminalele de reprezentare în absența informației explicite, permițând decizia rapidă sub informație parțială.
> SOURCE PASSAGE: represented by a frame to whose terminals are already attached, as default assignments, G the most usual solutions to that particular kind of problem.

* **Question**: Is the definition faithful to the quoted source passage (says what the passage supports, adds nothing it does not)? faithful / partial / unfaithful, or not_a_term if the heading is not a meaningful concept (an extraction artefact).
* **Allowed labels**: `faithful|partial|unfaithful|not_a_term`

## PN|c44fb943-a5f9-46e9-8deb-31777f69d66a  (priority 1, candidate_note_faithfulness)

* **Context**: term: method of loci   (claimed confidence: high)
* **Evidence**: method of loci (c44fb943-a5f9-46e9-8deb-31777f69d66a)

> DEFINITION: Descrie arhitectura mnemotehnică de ancorare a conținuturilor în coordonate spațiale imaginate ('palatul memoriei'), constituind un protocol nativ de organizare spațio-topologică în slotul map.
> SOURCE PASSAGE: Method of Loci (Memory Palace) The Method of Loci, created by the ancient Greeks, involves associating memories with familiar locations (like your home) by mentally placing items you wish to remember in specific rooms or areas.

* **Question**: Is the definition faithful to the quoted source passage (says what the passage supports, adds nothing it does not)? faithful / partial / unfaithful, or not_a_term if the heading is not a meaningful concept (an extraction artefact).
* **Allowed labels**: `faithful|partial|unfaithful|not_a_term`

## PN|c64da878-30cf-421d-aff0-abcf43053ca6  (priority 1, candidate_note_faithfulness)

* **Context**: term: activation   (claimed confidence: high)
* **Evidence**: activation (c64da878-30cf-421d-aff0-abcf43053ca6)

> DEFINITION: Definește valoarea numerică asociativă calculată din istoric și propagare contextuală care dictează accesibilitatea și probabilitatea de regăsire a elementelor de memorie în ACT-R, răspunzând direct la slotul retrieval.
> SOURCE PASSAGE: ition of subsymbolic quantities, that is, numeric activation values for each production rule (sometimes, simply “rule”) and declarative memory element. These activation values enabled

* **Question**: Is the definition faithful to the quoted source passage (says what the passage supports, adds nothing it does not)? faithful / partial / unfaithful, or not_a_term if the heading is not a meaningful concept (an extraction artefact).
* **Allowed labels**: `faithful|partial|unfaithful|not_a_term`

## PN|ca5fbf68-d9df-40b0-bbd2-d95f18d00a31  (priority 1, candidate_note_faithfulness)

* **Context**: term: papert principle   (claimed confidence: high)
* **Evidence**: papert principle (ca5fbf68-d9df-40b0-bbd2-d95f18d00a31)

> DEFINITION: Formulează principiul arhitectural fundamental conform căruia dezvoltarea cognitivă avansată are loc prin sinteza de noi strategii administrative de orchestrare a cunoștințelor preexistente, nu prin simpla acumulare cumulativă de date.
> SOURCE PASSAGE: Papert�s Principle: Some of the most crucial steps in mental growth are based not i simply on acquiring new skills, but on acquiring new administrative ways to use what one already knows.

* **Question**: Is the definition faithful to the quoted source passage (says what the passage supports, adds nothing it does not)? faithful / partial / unfaithful, or not_a_term if the heading is not a meaningful concept (an extraction artefact).
* **Allowed labels**: `faithful|partial|unfaithful|not_a_term`

## PN|caad870f-00e9-4786-821a-5511ac7ec8e4  (priority 1, candidate_note_faithfulness)

* **Context**: term: engram   (claimed confidence: high)
* **Evidence**: engram (caad870f-00e9-4786-821a-5511ac7ec8e4)

> DEFINITION: Represents the physical or neural representational unit of memory storage, defining the fundamental substrate of declarative and nondeclarative persistence.
> SOURCE PASSAGE: He was searching for evidence of the engram: the group of neurons that serve as the “physical representation of memory” (Josselyn, 2010).

* **Question**: Is the definition faithful to the quoted source passage (says what the passage supports, adds nothing it does not)? faithful / partial / unfaithful, or not_a_term if the heading is not a meaningful concept (an extraction artefact).
* **Allowed labels**: `faithful|partial|unfaithful|not_a_term`

## PN|cf5c873d-4569-4ed8-a3a8-bda10fc1680d  (priority 1, candidate_note_faithfulness)

* **Context**: term: black box   (claimed confidence: high)
* **Evidence**: black box (cf5c873d-4569-4ed8-a3a8-bda10fc1680d)

> DEFINITION: Definește tipul ontologic fundamental în cibernetică al sistemelor ale căror mecanisme interne sunt opace și pot fi deduse exclusiv prin seturi de intrări și ieșiri, stabilind o categorie formală de reprezentare.
> SOURCE PASSAGE: Sons, appropriate selection”. Indeed, ifa talking Black Box were to Lewin, K. Principles of topological psychologJe McGraw-Hill Book Co., show high power of appropriate selection in such matters—so New York,

* **Question**: Is the definition faithful to the quoted source passage (says what the passage supports, adds nothing it does not)? faithful / partial / unfaithful, or not_a_term if the heading is not a meaningful concept (an extraction artefact).
* **Allowed labels**: `faithful|partial|unfaithful|not_a_term`

## PN|d2219383-13a8-4fd8-a409-2da2c4ee1a7a  (priority 1, candidate_note_faithfulness)

* **Context**: term: retrograde amnesia   (claimed confidence: high)
* **Evidence**: retrograde amnesia (d2219383-13a8-4fd8-a409-2da2c4ee1a7a)

> DEFINITION: Defines the selective disruption of consolidated historical memory traces, separating retrieval indexing failures from encoding deficits.
> SOURCE PASSAGE: Retrograde amnesia is loss of memory for events that occurred prior to the trauma.

* **Question**: Is the definition faithful to the quoted source passage (says what the passage supports, adds nothing it does not)? faithful / partial / unfaithful, or not_a_term if the heading is not a meaningful concept (an extraction artefact).
* **Allowed labels**: `faithful|partial|unfaithful|not_a_term`

## PN|d51064f2-0563-45f5-8d7c-b3bdc9e219c7  (priority 1, candidate_note_faithfulness)

* **Context**: term: hierarchical memory   (claimed confidence: high)
* **Evidence**: hierarchical memory (d51064f2-0563-45f5-8d7c-b3bdc9e219c7)

> DEFINITION: Definește organizarea memoriei în grafuri multi-nivel stratificate cu legături părinte-copil pe niveluri de abstractizare, reprezentând o topologie structurală explicită în slotul map.
> SOURCE PASSAGE: significantly hinder its practical deployment. 3.1.3 Hierarchical Memory (3D) Definition of Hierarchical (3D) Memory Hierarchical memory organizes information across layers, using inter-level connections to shape the memories into a volumetric

* **Question**: Is the definition faithful to the quoted source passage (says what the passage supports, adds nothing it does not)? faithful / partial / unfaithful, or not_a_term if the heading is not a meaningful concept (an extraction artefact).
* **Allowed labels**: `faithful|partial|unfaithful|not_a_term`

## PN|d6f5ed57-4f9c-42bd-8868-e7771774291b  (priority 1, candidate_note_faithfulness)

* **Context**: term: anterograde amnesia   (claimed confidence: high)
* **Evidence**: anterograde amnesia (d6f5ed57-4f9c-42bd-8868-e7771774291b)

> DEFINITION: Defines the pathological failure of declarative consolidation from working memory to long-term storage, delineating architectural boundaries between buffer state and persistent memory.
> SOURCE PASSAGE: With anterograde amnesia, you cannot remember new information, although you can remember information and events that happened prior to your injury.

* **Question**: Is the definition faithful to the quoted source passage (says what the passage supports, adds nothing it does not)? faithful / partial / unfaithful, or not_a_term if the heading is not a meaningful concept (an extraction artefact).
* **Allowed labels**: `faithful|partial|unfaithful|not_a_term`

## PN|e4aeda45-277a-4cf4-a64e-804d68edff2a  (priority 1, candidate_note_faithfulness)

* **Context**: term: operator proposal   (claimed confidence: high)
* **Evidence**: operator proposal (e4aeda45-277a-4cf4-a64e-804d68edff2a)

> DEFINITION: Definește faza din ciclul decizional cognitiv în care producțiile analizează starea curentă și propun candidați de acțiune în setul de selecție, constituind pasul cheie de rutare a posibilităților.
> SOURCE PASSAGE: Operator proposal An operator is proposed by a rule that tests the current state and creates an acceptable preference for an operator.

* **Question**: Is the definition faithful to the quoted source passage (says what the passage supports, adds nothing it does not)? faithful / partial / unfaithful, or not_a_term if the heading is not a meaningful concept (an extraction artefact).
* **Allowed labels**: `faithful|partial|unfaithful|not_a_term`

## PN|efc34958-6529-4af7-8ad2-d59f050b1bbf  (priority 1, candidate_note_faithfulness)

* **Context**: term: equipotentiality hypothesis   (claimed confidence: high)
* **Evidence**: equipotentiality hypothesis (efc34958-6529-4af7-8ad2-d59f050b1bbf)

> DEFINITION: Formulates the distributed fault-tolerance principle of memory substrates where undamaged network components compensate for localized failures.
> SOURCE PASSAGE: he formulated the equipotentiality hypothesis: if part of one area of the brain involved in memory is damaged, another part of the same area can take over that memory function

* **Question**: Is the definition faithful to the quoted source passage (says what the passage supports, adds nothing it does not)? faithful / partial / unfaithful, or not_a_term if the heading is not a meaningful concept (an extraction artefact).
* **Allowed labels**: `faithful|partial|unfaithful|not_a_term`

## PN|f3977411-5918-430f-8099-c4eb254b20db  (priority 1, candidate_note_faithfulness)

* **Context**: term: chunk   (claimed confidence: high)
* **Evidence**: chunk (f3977411-5918-430f-8099-c4eb254b20db)

> DEFINITION: Definește pachetul simbolic atomic de informație semantică cu atribute discrete supus activării și degradării în ACT-R și Soar, reprezentând unitatea structurală esențială din slotul ontology.
> SOURCE PASSAGE: rning occurs through rule learning and changes to chunk activation. The parameters to these equations can be used to model individuals' differences and to

* **Question**: Is the definition faithful to the quoted source passage (says what the passage supports, adds nothing it does not)? faithful / partial / unfaithful, or not_a_term if the heading is not a meaningful concept (an extraction artefact).
* **Allowed labels**: `faithful|partial|unfaithful|not_a_term`

## PN|f3b8cf50-06fb-4c1d-9c4e-ff56e60e762d  (priority 1, candidate_note_faithfulness)

* **Context**: term: retroactive interference   (claimed confidence: high)
* **Evidence**: retroactive interference (f3b8cf50-06fb-4c1d-9c4e-ff56e60e762d)

> DEFINITION: Models the converse memory vulnerability where freshly encoded knowledge disrupts or overwrites pre-existing stored traces, governing retention policies and consolidation schedules.
> SOURCE PASSAGE: Retroactive interference happens when information learned more recently hinders the recall of older information.

* **Question**: Is the definition faithful to the quoted source passage (says what the passage supports, adds nothing it does not)? faithful / partial / unfaithful, or not_a_term if the heading is not a meaningful concept (an extraction artefact).
* **Allowed labels**: `faithful|partial|unfaithful|not_a_term`

## PN|fbfcc184-9fcf-406c-b60d-839e8fe24db2  (priority 1, candidate_note_faithfulness)

* **Context**: term: configural association   (claimed confidence: high)
* **Evidence**: configural association (fbfcc184-9fcf-406c-b60d-839e8fe24db2)

> DEFINITION: Definește mecanismul de legare a stimulilor elementari într-o reprezentare compusă unitară cu proprietăți emergente distincte, rezolvând problema compozițională a hărților cognitive.
> SOURCE PASSAGE: Elemental associations consist of simple pairwise links between two stimuli (e.g., a and b), whereas configural associations involve the construction of a higher order, joint representation (e.g., ab) that in turn can be linked with other stimuli.

* **Question**: Is the definition faithful to the quoted source passage (says what the passage supports, adds nothing it does not)? faithful / partial / unfaithful, or not_a_term if the heading is not a meaningful concept (an extraction artefact).
* **Allowed labels**: `faithful|partial|unfaithful|not_a_term`

