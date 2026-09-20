# Raport Dry-Run: Curățarea celor 29 de Muchii Respinse la Audit

- **Dată generare**: 2026-09-20T11:38:01+00:00
- **Eșantion auditat**: `07_EVALUATION/edge_audit_v2/audit_sample_declared_50.json`
- **SHA-256 Eșantion (verificat)**: `815d00d131297670533ae2a6ac207b78f45333415c13612909ef271136ba1c1b`
- **Muchii propuse spre ștergere**: 29
- **Mod de execuție**: DRY-RUN (nicio modificare aplicată asupra depozitului sau grafului)

---

## 1. Măsurători de Graf (Înainte vs. După Simulare)

| Metrică | Înainte de Purjare | După Purjare (Simulat) | Delta |
|---|---:|---:|---:|
| Total muchii graf | 536 | 507 | -29 |
| Muchii tipizate (`relation != related_to`) | 114 | 85 | -29 |

---

## 2. Distribuție pe Tipuri de Relații

| Relație | Număr Muchii Respinse |
|---|---:|
| `part_of` | 19 |
| `depends_on` | 6 |
| `applies_to` | 4 |

---

## 3. Distribuție pe Motive de Respingere

| Categorie Respingere | Număr Muchii |
|---|---:|
| `wrong_type` | 14 |
| `unrelated` | 10 |
| `unsupported` | 2 |
| `wrong_direction` | 2 |
| `shared_terms_only` | 1 |

---

## 4. Tabelul Detaliat al celor 29 de Muchii de Șters

| Index | Relație | Sursă | Țintă | Motiv Respingere | Rationale |
|---:|---|---|---|---|---|
| 1 | `applies_to` | [adaptation](01_ARCHITECTURE\knowledge\Promoted_adaptation.md) | stability | `wrong_type` | adaptation si stability sunt definitii paralele; niciuna nu guverneaza pe cealalta. |
| 2 | `applies_to` | [regulation](01_ARCHITECTURE\knowledge\Promoted_regulation.md) | essential variables | `unsupported` | regulation e definita generic; textul nu mentioneaza essential variables. |
| 3 | `applies_to` | [consolidation](01_ARCHITECTURE\knowledge\Promoted_consolidation.md) | working memory | `shared_terms_only` | consolidation si working memory sunt sisteme distincte; termenul comun e memory. |
| 4 | `applies_to` | [mental imagery](01_ARCHITECTURE\knowledge\Promoted_mental_imagery.md) | working memory | `wrong_type` | mental imagery poate folosi working memory; ar fi uses sau part_of, nu applies_to. |
| 9 | `depends_on` | [📖 Optimizarea Memoriei: Strategii Mnemonice și Învățare Eficientă (OpenStax)](01_ARCHITECTURE\knowledge\openstax_psy2e_memory_enhancement_strategies.md) | 📖 Uitare, Interferență și Cele Șapte Păcate ale Memoriei (OpenStax) | `unrelated` | strategiile de imbunatatire nu depind structural de nota despre uitare. |
| 10 | `depends_on` | [📖 Tulburările de Memorie: Amnezie și Reconstrucție (OpenStax)](01_ARCHITECTURE\knowledge\openstax_psy2e_amnesia_and_memory_reconstruction.md) | 📖 Baza Biologică a Memoriei: Structuri Cerebrale și Engrama (OpenStax) | `unsupported` | amnezia mentioneaza doar brain trauma generic, fara sa citeze structurile tintei. |
| 12 | `depends_on` | [Procedură Operațională: Audit Heuristic UI/UX și Verificare Accesibilitate](10_DOCUMENTATION\procedures\UI_UX_Heuristic_Review.md) | Fundamentele Sistemelor de Design (Design Tokens & Ierarhie Vizuală) | `unrelated` | auditul heuristic UI nu depinde structural de sistemul de design tokens. |
| 13 | `depends_on` | [Arhitectura Panourilor de Administrare și Tablourilor de Bord (Dashboard & Admin UI)](01_ARCHITECTURE\knowledge\Dashboard_Admin_UI_Architecture.md) | Fundamentele Sistemelor de Design (Design Tokens & Ierarhie Vizuală) | `wrong_direction` | arhitectura dashboard-ului consuma tokenii de design; sursa si tinta sunt inversate. |
| 15 | `depends_on` | [Contracte Multi-Agent — Registru de Transferuri (Claude Code / Gemini CLI / Antigravity)](01_ARCHITECTURE\knowledge\Registru_Multi_Agent_Contracts.md) | Standarde Operaționale — Registru Militar de Transferuri & Device Control (C# WPF .NET 10) | `wrong_direction` | contractele multi-agent depind de standarde, nu standardele de contracte. |
| 18 | `depends_on` | [📖 Stocarea și Recuperarea Memoriei: Modelul Atkinson-Shiffrin (OpenStax)](01_ARCHITECTURE\knowledge\openstax_psy2e_memory_storage_and_retrieval.md) | 📖 Funcțiile Memoriei: Codificare și Niveluri de Procesare (OpenStax) | `unrelated` | Atkinson-Shiffrin si levels of processing sunt descrieri paralele, nu o dependenta. |
| 20 | `part_of` | [Dependency DAG — Phase 0](00_GOVERNANCE\phase_0\DEPENDENCY_DAG.md) | Ontology Slot: Procedures | `wrong_type` | Dependency DAG e un plan de sarcini, nu o componenta a slotului ontologic Procedures. |
| 21 | `part_of` | [state-determined system](01_ARCHITECTURE\knowledge\Promoted_state_determined_system.md) | Ontology Slot: Ontology | `wrong_type` | state-determined system nu e inclus in ontologia tipurilor de memorie. |
| 22 | `part_of` | [reinforcement learning](01_ARCHITECTURE\knowledge\Promoted_reinforcement_learning.md) | consolidation | `unrelated` | reinforcement learning nu e o componenta a consolidarii neurobiologice. |
| 23 | `part_of` | [adaptation](01_ARCHITECTURE\knowledge\Promoted_adaptation.md) | Ontology Slot: Procedures | `wrong_type` | adaptation e concept homeostatic, nu parte a slotului despre executia operatiilor. |
| 24 | `part_of` | [Baseline reconciliation — Phase 0](00_GOVERNANCE\phase_0\BASELINE_RECONCILIATION.md) | Ontology Slot: Procedures | `unrelated` | Baseline reconciliation e document de audit git, fara legatura cu slotul de proceduri. |
| 25 | `part_of` | [transformation](01_ARCHITECTURE\knowledge\Promoted_transformation.md) | Ontology Slot: Procedures | `wrong_type` | transformation e operator markovian; nicio relatie de includere cu slotul de proceduri. |
| 28 | `part_of` | [Module reality matrix — Phase 0](00_GOVERNANCE\phase_0\MODULE_REALITY_MATRIX.md) | Ontology Slot: Procedures | `unrelated` | Module reality matrix e audit tehnic, fara continut despre executia procedurilor. |
| 29 | `part_of` | [priming](01_ARCHITECTURE\knowledge\Promoted_priming.md) | Ontology Slot: Retrieval | `wrong_type` | priming e tip de memorie nondeclarativa, nu parte din mecanismul de retrieval. |
| 31 | `part_of` | [Decision register — Phase 0](00_GOVERNANCE\phase_0\DECISION_REGISTER.md) | Ontology Slot: Procedures | `unrelated` | Decision register e document de guvernanta, nu componenta a slotului de proceduri. |
| 34 | `part_of` | [Phase 0 — reconciliation](00_GOVERNANCE\phase_0\README.md) | Ontology Slot: Procedures | `unrelated` | README-ul Phase 0 e index de audit, nu procedura operationala. |
| 37 | `part_of` | [regulation](01_ARCHITECTURE\knowledge\Promoted_regulation.md) | Ontology Slot: Procedures | `wrong_type` | regulation e concept cibernetic, nu parte a executiei operatiilor. |
| 39 | `part_of` | [feedback](01_ARCHITECTURE\knowledge\Promoted_feedback.md) | Ontology Slot: Routing | `wrong_type` | feedback e concept de control, nu raspunde la rutarea agentilor. |
| 41 | `part_of` | [Task ledger — Phase 0](00_GOVERNANCE\phase_0\TASK_LEDGER.md) | Ontology Slot: Procedures | `wrong_type` | transducer e concept cibernetic generic, nu descrie rutare de agenti. |
| 42 | `part_of` | [transducer](01_ARCHITECTURE\knowledge\Promoted_transducer.md) | Ontology Slot: Routing | `wrong_type` | working memory e concept cognitiv; slotul State inseamna ce e implementat acum, masurat. |
| 43 | `part_of` | [working memory](01_ARCHITECTURE\knowledge\Promoted_working_memory.md) | Ontology Slot: State | `unrelated` | Corpus health baseline e raport de masurare, nu procedura operationala. |
| 44 | `part_of` | [Corpus health baseline — Phase 0](00_GOVERNANCE\phase_0\CORPUS_HEALTH_BASELINE.md) | Ontology Slot: Procedures | `wrong_type` | sensitization e fenomen comportamental, nu tip de memorie in taxonomia citata. |
| 45 | `part_of` | [sensitization](01_ARCHITECTURE\knowledge\Promoted_sensitization.md) | Ontology Slot: Ontology | `wrong_type` | stability e concept din literatura; slotul State e definit ca masurat empiric. |
| 46 | `part_of` | [stability](01_ARCHITECTURE\knowledge\Promoted_stability.md) | Ontology Slot: State | `wrong_type` | latent memory si state folosesc cuvantul stare cu sensuri incompatibile. |
| 49 | `part_of` | [buffer](01_ARCHITECTURE\knowledge\Promoted_buffer.md) | working memory | `unrelated` | capitole adiacente din aceeasi carte: vecinatate tematica, nu incluziune. |

---

## 5. Concluzie & Plan de Aplicare

Toate cele 29 de muchii identificate sunt prezente în `SynapseStore` și corespund
declarațiilor de relații din frontmatter-ul notelor-sursă.
Ștergerea se va efectua tranzacțional prin `graph.plasticity.PlasticityEngine` cu
garanție completă de rollback byte-cu-byte.
