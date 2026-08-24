# Cognitive Core Architecture (v4.0.0 — Brain Upgrade)

`cognitive_core` implementează motorul cognitiv simbolic și bio-inspirat al Vault-ului de Memorie AI.

---

## 🧠 Harta Modulei — Concepte din Neuroștiință & Arhitecturi Cognitive

| Modul Python | Concept Neuroștiințific / Arhitectură Cognitivă | Descriere & Surse Teoretice |
|---|---|---|
| `activation.py` | **ACT-R Base-Level Activation Decay** | Formula de activare $B_i = \ln(\sum_j t_j^{-d})$ bazată pe recență și frecvență. Evită memoriile irelevante și le trece în stare `DORMANT_THRESHOLD` (Anderson 2004, *An Integrated Theory of Mind*). |
| `consolidation.py` | **Memory Reconsolidation & Volatility** | Memoriile canonice contrazise intră în starea `RECONSOLIDATING` cu istoricul versiunilor păstrat, permițând actualizări plastice fără pierdere de memorie (Nader et al. 2000, *Nature*). |
| `motivation.py` | **ACT-R Production Utility & Reward** | Calculul utilității acțiunilor $U = P \cdot G - C$ prin Exponential Moving Average pe semnalele de recompensă primite de la `VerifierAgent` și feedback de execuție (Lovett 1998). |
| `global_workspace.py` | **Global Workspace Theory (GWT)** | Spațiu competitiv central în care agenții (`Router`, `Retrieval`, `Verifier`, `Critic`) trimit propuneri, iar propunerea câștigătoare este difuzată (*broadcast*) global tuturor agenților (Baars 1988; Dehaene et al. 2001). |
| `attention.py` | **Attention Allocation Model** | Scor ponderat de atenție combinând activarea ACT-R, încrederea, recența și bonusul de utilitate. |
| `working_memory.py` | **Bounded Working Memory (Miller 7±2 / Cowan 4)** | Buffer activ cu capacitate limitată și eliminare pe bază de atenție. |
| `recall.py` | **Spreading Activation & Lineage Recall** | Căutare pe graf cu activare difuză, scor temporal și rezoluție automată a fiilor activi. |
| `orchestrator.py` | **Multi-Agent Least Privilege Orchestration** | Coordonare MNP între cele 21 de profiluri de agenți ai Consiliului cu difuzare prin Global Workspace. |
| `reflection.py` | **Formal Reflexion & SelfRefine Critique** | Cicluri de reflecție formală și auto-rafinare pentru lecțiile ephemere în stare `REVIEW`. |

---

## 🧪 Validarea Suitei de Teste

Run tests via repository root:
```bash
python -m pytest cognitive_core/tests/
```
