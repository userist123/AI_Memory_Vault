---
id: "proc-brain-arch-0001"
type: procedure
lifecycle: ACTIVE
category: system-architecture
tags: [cognitive-brain, act-r, gwt, reconsolidation, neuromorphic, sNN, 21-agents]
created: 2026-08-24T23:18:00Z
updated: 2026-08-24T23:18:00Z
provenance:
  source_type: official
  source_ref: "codex-brain-upgrade"
confidence: very_high
verification: verified
relations:
  - "01_ARCHITECTURE/System_Architecture.md"
  - "01_ARCHITECTURE/knowledge/Agents_Skill_Matrix.md"
  - "01_ARCHITECTURE/knowledge/Master_Skills_Catalog_251.md"
---

# 🧠 Specificație Canonică: Arhitectura Cognitivă Bio-Inspirată (v4.5.0 Brain Core)

Acest document specifică arhitectura cognitivă bio-inspirată integrată în `AI_Memory_Vault_CODEX_READY`.

---

## 🏛️ 1. Cele 5 Module Cognitive ale Creierului AI

### A. Motorul de Decădere a Activării ACT-R (`cognitive_core/activation.py`)
- **Model Matematic**: $B_i = \ln\left(\sum_{j=1}^n (t - t_j)^{-d}\right)$ cu rata de decădere $d = 0.5$.
- **Mecanism**: Notele neaccesate pierd din activare și trec în stare `DORMANT_THRESHOLD` (-2.0), eliberând memoria de lucru. Notele reaccesate își cresc activarea în timp real.

### B. Reconsolidarea Plastice a Memoriei (`cognitive_core/consolidation.py`)
- **Mecanism**: Când `VerifierAgent` sau `CriticAgent` detectează o evidență contradictorie împotriva unei note canonice (`ACTIVE` / `VERIFIED`), apelează `challenge()`.
- **Tranziție de Stare**: Nota trece în starea `RECONSOLIDATING`, păstrând istoricul versiunii anterioare (`previous_version`).
- **Rezoluție**: În urma reflecției formale, nota revine la `ACTIVE` (dacă este actualizată) sau `REVIEW` (dacă rămâne nerezolvată).

### C. Modulul Motivațional & Utilitate a Producțiilor (`cognitive_core/motivation.py`)
- **Model Matematic**: Utilitatea acțiunilor $U_i = P_i \cdot G - C_i$ actualizată prin Exponential Moving Average (EMA).
- **Semnal de Recompensă**: Recompensele pozitive/negative de la `VerifierAgent` și executarea de cod ajustează dinamic utilitatea tipurilor de acțiuni, oferind un bonus de atenție în `attention.py`.

### D. Global Workspace Theory (GWT) Broadcast Engine (`cognitive_core/global_workspace.py`)
- **Mecanism**: Hub competitiv central în care agenții Consiliului (`Router`, `Retrieval`, `Verifier`, `Critic`) trimit `WorkspaceProposal`.
- **Scor de Competiție**:
  $$\text{Scor Final} = \text{Coerență Agent} \times 0.5 + \text{Activare ACT-R} \times 0.3 + \text{Utilitate} \times 0.2$$
- **Broadcast**: Propunerea cu scor maxim este difuzată (*broadcast*) tuturor agenților din sistem.

### E. Substrat Sub-Simbolic Spiking / Neuromorphic (`cognitive_core/neuromorphic/`)
- **Neuron LIF (`LIFNeuron`)**: Model cu integrare scursă de potențial $\tau \frac{dV}{dt} = -(V - V_{\text{rest}}) + R \cdot I(t)$, prag de potențial $V_{\text{th}}$, resetare și perioadă refractară.
- **Sinapsă STDP (`STDPSynapse`)**: Regula Hebbiană de învățare plastică bazată pe timing-ul impulsurilor $\Delta t = t_{\text{post}} - t_{\text{pre}}$ (LTP & LTD).
- **Bridge Network (`SpikingMemoryNetwork`)**: Leagă simbolurile din Vault (`note_id`) de dinamica impulsurilor electrice spiking.

---

## 📊 2. Integritatea Suitei de Teste
- **Total Teste Automate**: **463 PASSED**
- **Execuție**: `python -m pytest -q`

---

## 🔗 Legături de Memorie & Graf Obsidian
- [[12 Projects and Procedures Map]]
- [[Knowledge Graph Home]]
- [[Knowledge Graph Home]]
