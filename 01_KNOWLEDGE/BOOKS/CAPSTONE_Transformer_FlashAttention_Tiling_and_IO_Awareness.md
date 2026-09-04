---
id: 76972796-42d3-4d7b-8ec3-2e30154b015c
type: knowledge
lifecycle: REVIEW
category: deep_learning/flashattention_kernel_tiling
tags:
- deep-learning
- glassner
- flash-attention
- online-softmax
- kernel-tiling
- gpu-io-awareness
- sram
- capstone
created: '2026-09-04'
updated: '2026-09-04'
provenance:
  source_type: ai
  source_ref: 06_INBOX/RAW_IMPORTS/BOOKS/Arthur-Glassner-Deep-Learning-Ch12
confidence: high
verification: unverified
relations:
- relation: references
  target: 01_KNOWLEDGE/BOOKS/EXPERT_Transformer_Mixture_of_Experts_and_Routing.md
- relation: references
  target: 01_KNOWLEDGE/BOOKS/Caiet_Teme_Aplicatii_Practice_Carti.md
- relation: references
  target: 00_CORE/GRAPH/07 Knowledge Domains Map.md
---

# Deep Learning Capstone: FlashAttention Tiling, Optimizare I/O & Softmax Online Fuzionat

**Surs?**: Arthur Glassner, *Learning Deep Learning* (Ch. 12) + Dao et al. (FlashAttention Exact Tiling)  
**Domeniu**: Arhitectura GPU & Acceleratoare, Eficien?? I/O-Aware & Algoritmi f?r? Materializare de Matrici

---

## 1. Bariera Memoriei ?n Mecanismul Standard de Aten?ie
Standard Self-Attention calculeaz?:
$$S = \frac{Q K^T}{\sqrt{d_k}} \in \mathbb{R}^{N \times N}, \quad P = \text{softmax}(S) \in \mathbb{R}^{N \times N}, \quad O = P V \in \mathbb{R}^{N \times d}$$
Pentru secven?e lungi ($N = 32\text{k}$), salvarea matricilor intermediare $S$ ?i $P$ ?n High Bandwidth Memory (HBM) necesit? zeci de gigaocte?i de trafic I/O la o l??ime de band? limitat?, conduc?nd la fenomenul *Memory-Bound* (unde opera?iile de calcul FLOP/s sunt blocate a?tept?nd transferul din memoria HBM ?n memoria rapid? SRAM a GPU-ului).

## 2. Solu?ia FlashAttention: Tiling ?i Online Softmax
FlashAttention ?mparte matricile $Q, K, V$ ?n blocuri compatibile cu dimensiunea memoriei SRAM ($B_r \times d$ ?i $B_c \times d$). Pentru calculul softmax-ului pe blocuri f?r? a ?ti suma global? a numitorului $\sum e^{s_j}$, se utilizeaz? formula recursiv? **Online Softmax**:
Fie dou? blocuri cu maximele $m_1, m_2$ ?i normalizatorii $l_1 = \sum e^{x_i - m_1}, l_2 = \sum e^{x_j - m_2}$:
$$m_{\text{new}} = \max(m_1, m_2)$$
$$l_{\text{new}} = e^{m_1 - m_{\text{new}}} l_1 + e^{m_2 - m_{\text{new}}} l_2$$
$$O_{\text{new}} = \text{diag}\left(e^{m_1 - m_{\text{new}}}\right) O_1 + e^{m_2 - m_{\text{new}}} P_2 V_2$$
Prin aceast? fuziune de nucleu, consumul de memorie scade de la $O(N^2)$ la $O(N)$, iar timpul total de execu?ie scade cu 2-4x datorit? reducerii radicale a acces?rilor de memorie HBM.

## 3. Leg?turi Canonice & Graf de Cuno?tin?e
- [[Deep_Learning_Representations_and_Attention]]
- [[ADVANCED_Transformer_RoPE_Attention_Sampling]]
- [[EXPERT_Transformer_Mixture_of_Experts_and_Routing]]
- [[Caiet_Teme_Aplicatii_Practice_Carti]]
- [[07 Knowledge Domains Map]]
