---
id: d57a6b1d-4996-48ba-9f0e-d522ee972f7b
type: knowledge
lifecycle: REVIEW
category: agents/blackboard_swarm_orchestration
tags:
- agents
- pai
- blackboard-pattern
- swarm-coordination
- hypothesis-space
- opportunistic-control
- capstone
created: '2026-09-04'
updated: '2026-09-04'
provenance:
  source_type: ai
  source_ref: 06_INBOX/RAW_IMPORTS/BOOKS/Armando-Pai-Agent-Powered-Apps-Ch9
confidence: high
verification: unverified
relations:
- relation: references
  target: 01_KNOWLEDGE/BOOKS/EXPERT_Agent_State_Checkpoints_and_Human_in_the_Loop.md
- relation: references
  target: 01_KNOWLEDGE/BOOKS/Caiet_Teme_Aplicatii_Practice_Carti.md
- relation: references
  target: 00_CORE/GRAPH/07 Knowledge Domains Map.md
---

# Agent Capstone: Arhitectur? Blackboard pentru Roiuri de Agen?i & Coordonare Oportunist?

**Surs?**: Armando Pai, *Building Agent-Powered Applications* (Ch. 9) + Daniel Corkill (Blackboard Systems)  
**Domeniu**: Arhitecturi Multi-Agent Autonome, Spa?ii de Ipoteze Partajate & Control Oportunist

---

## 1. Trilogia Arhitecturii Blackboard
Arhitectura Blackboard este compus? din trei entit??i distincte:
1. **The Blackboard**: Structur? de date global?, partajat? ?i ierarhizat? pe niveluri de abstractizare (e.g. `Raw Tokens -> Phrases -> Semantic Facts -> Candidate Hypotheses -> Verified Decisions`).
2. **Knowledge Sources (KS)**: Agen?i specializa?i autonomi, complet decupla?i unul de cel?lalt. Fiecare KS monitorizeaz? tabla ?i de?ine dou? componente:
   - *Condition (Precondi?ie)*: Verific? dac? starea curent? a tablei con?ine date relevante pentru capabilitatea sa.
   - *Action (Execu?ie)*: Preia datele, aplic? transformarea cognitiv? ?i public? rezultatul ?napoi pe tabl?.
3. **The Control Component**: Planificator oportunist care analizeaz? evenimentele emise de tabl?, sorteaz? agen?ii activi ?n func?ie de utilitate marginal? ?i acord? dreptul de scriere pe tabl? concurent sau secven?ial.

## 2. Rezolu?ia Conflictelor ?i Izolarea Memoriei
Spre deosebire de pipeline-urile rigide ?n lan? (*Chains*) sau arborii rigizi de delegare, modelul Blackboard permite convergen?? oportunist?:
- Niciun agent nu apeleaz? direct un alt agent; comunicarea se face exclusiv prin artefacte pe tabl?.
- Pentru evitarea supra-scrierii datelor, tabla aplic? reguli stricte de imutabilitate ?i versionare: intr?rile sunt ad?ugate ca noi ipoteze ?nso?ite de un scor Bayesian de ?ncredere ($P(H \mid E)$) ?i o leg?tur? de provenien?? c?tre eviden?a generatoare.
- C?nd dou? ipoteze concurente intr? ?n contradic?ie, Controlerul instan?iaz? un Verifier KS dedicat pentru falsificare empiric?.

## 3. Leg?turi Canonice & Graf de Cuno?tin?e
- [[Agent_Architecture_and_Tool_Orchestration]]
- [[SPECIALIZED_Agent_Reflexion_and_MultiAgent_Debate]]
- [[EXPERT_Agent_State_Checkpoints_and_Human_in_the_Loop]]
- [[Caiet_Teme_Aplicatii_Practice_Carti]]
- [[07 Knowledge Domains Map]]
