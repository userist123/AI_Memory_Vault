---
id: 57bb2166-b2ba-58af-834c-a0c9ebbcfeca
type: knowledge
lifecycle: REVIEW
category: agents/deliberative_search_tree_of_thoughts
tags:
- agent-architecture
- zvarydchuk
- tree-of-thoughts
- graph-of-thoughts
- deliberative-search
- beam-search
- monte-carlo-tree-search
- self-evaluation
created: '2026-09-04'
updated: '2026-09-04'
provenance:
  source_type: ai
  source_ref: 06_INBOX/RAW_IMPORTS/BOOKS/Zvarydchuk-Building-Agent-Powered-Apps-Ch6-Ch10
confidence: high
verification: unverified
relations:
- relation: references
  target: 01_KNOWLEDGE/BOOKS/SCALING_Agent_Context_Window_and_Conversation_Memory.md
- relation: references
  target: 01_KNOWLEDGE/BOOKS/PRODUCTION_Agent_Tool_Grounding_and_Verification_Chains.md
- relation: references
  target: 01_KNOWLEDGE/BOOKS/Caiet_Teme_Aplicatii_Practice_Carti.md
---

# Agent Frontier: Căutare Deliberativă, Arbore de Gânduri (ToT) și Grafuri de Raționament (GoT)

**Sursă**: Vasyl Zvarydchuk, *Building Agent-Powered Applications* (Capitolele 6 și 10) & Yao et al. (2023, *Tree of Thoughts*), Besta et al. (2024, *Graph of Thoughts*)  
**Domeniu**: Planificare Euristică Deliberativă, Evaluare de Stare Intermediară, Backtracking în Agenți

---

## 1. Evoluția Paradigmelor de Prompting și Planificare

| Paradigma | Topologie | Mod de Execuție | Capacitate de Backtracking | Complexitate Probleme |
| :--- | :--- | :--- | :--- | :--- |
| **Standard Input-Output** | $1 \to 1$ | Liniar fără reflecție | Nulă | Sarcini simple de clasificare |
| **Chain-of-Thought (CoT)** | $1 \to N \to 1$ | Secvențial pas-cu-pas | Nulă (eroarea timpurie se propagă) | Aritmetică, raționament comun |
| **Self-Consistency (CoT-SC)** | Pădure de lanțuri paralele | Vot majoritar la final | Nulă (lanțurile eșuate sunt doar ignorate) | Sarcini cu răspunsuri scurte |
| **Tree-of-Thoughts (ToT)** | Arbore explorat (BFS/DFS/A*) | Branching + Autoevaluare + Backtrack | Totală (taie ramurile eșuate devreme) | Jocul 24, Sudoku, Planificare strategică |
| **Graph-of-Thoughts (GoT)** | DAG / Graf dinamic | Unire ramuri (*Merge*), cicluri de rafinare | Avansată (sinteză multi-cale) | Sinteză de documente, optimizare cod |

---

## 2. Arhitectura Tree-of-Thoughts (ToT)

ToT transformă generarea secvențială de text într-o căutare într-un spațiu de stări:
1. **Descompunerea în Gânduri (*Thought Decomposition*)**: Împărțirea problemei în pași intermediari discreți.
2. **Generatorul de Gânduri (*Thought Generator*)**:
   - *Sample i.i.d.* (pentru spații mici de gânduri).
   - *Propose sequentially* (când spațiul este vast, modelul propune $k$ gânduri distincte).
3. **Evaluatorul de Stare (*State Evaluator*)**:
   - *Value classifier* ($V(s) \in [0, 1]$ sau $\{sure, likely, impossible\}$).
   - *Vote* (compară stările concurente și alege pe cea mai promițătoare).
4. **Motorul de Căutare (*Search Algorithm*)**:
   - **BFS (Breadth-First Search)**: Menține o frontieră cu cele mai bune $b$ stări per nivel (*Beam Search*).
   - **DFS (Depth-First Search)**: Explorează în profunzime până la o frunză sau până când starea este evaluată sub pragul minim, declanșând **backtracking**.

```
                           [Rădăcină: Sarcina Inițială]
                                 /            \
                       [Gândul 1A (0.8)]    [Gândul 1B (0.2 - REJECT)]
                            /        \
                 [Gând 2A (0.9)]  [Gând 2B (0.3)]
                       |
               [SOLUȚIE FINALĂ]
```

---

## 3. Graph-of-Thoughts (GoT): Dincolo de Ierarhii Stricte

În timp ce ToT permite doar ramificare și explorare descendentă, GoT introduce operații de topologie complexă:
- **Transformare $1 \to 1$**: Rafinare incrementală a unui gând.
- **Generare $1 \to N$**: Ramificare exploratorie.
- **Agregare / Fuziune $N \to 1$**: Combinarea celor mai bune elemente din mai multe ramuri independente într-un nou gând superior.
- **Buclă $1 \to 1$ (Feedback loop)**: Re-evaluare și ajustare directă pe graful de stări.

---

## 4. Analiză de Eficiență și Buget de Tokeni

Deliberarea profundă implică un cost computațional ridicat. Pentru a fi viabilă în producție, agenții aplică euristicile:
- **Tăiere Timpurie (*Early Pruning*)**: Dacă evaluatorul returnează `impossible`, ramura este eliminată instantaneu fără a cheltui tokeni pe descendenți.
- **Beam Width Dinamic**: Lățimea fasciculului $b$ se ajustează invers proporțional cu certitudinea evaluatorului.
- **Cache de Evaluări de Stare**: Stările similare își partajează scorurile prin semantic similarity.

---

## 5. Aplicabilitate în AI Memory Vault

- **Rezolvarea Task-urilor Complexe de Refactorizare**: Sub-agentul `compiler_and_tooling_engineer` poate explora 3 arhitecturi concurente cu ToT BFS, evaluând compilabilitatea la fiecare pas.
- **Sinteza Multi-Sursă din Vault**: Căutarea GoT permite agregarea a 4 note disparate într-o singură decizie arhitecturală unitară.
- **Planificare Reversibilă**: Evitarea blocajelor ireversibile prin păstrarea stărilor de checkpoint în arborele de căutare.

---

## Referințe Obsidian

- [[SCALING_Agent_Context_Window_and_Conversation_Memory]]
- [[PRODUCTION_Agent_Tool_Grounding_and_Verification_Chains]]
- [[CAPSTONE_Agent_Swarm_Blackboard_and_Dynamic_Orchestration]]
- [[Caiet_Teme_Aplicatii_Practice_Carti]]
