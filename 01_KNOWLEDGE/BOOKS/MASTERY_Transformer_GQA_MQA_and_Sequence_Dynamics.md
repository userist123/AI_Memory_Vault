---
id: 86ab1671-9afa-55fc-a636-27c4fbb17d4b
type: knowledge
lifecycle: REVIEW
category: deep_learning/gqa_mqa_sequence_dynamics
tags:
- deep-learning
- ekman
- transformer
- grouped-query-attention
- multi-query-attention
- kv-cache
- attention-sink
- rmsnorm
created: '2026-09-04'
updated: '2026-09-04'
provenance:
  source_type: ai
  source_ref: 06_INBOX/RAW_IMPORTS/BOOKS/Magnus-Ekman-Learning-DL-Ch13-14
confidence: high
verification: unverified
relations:
- relation: references
  target: 01_KNOWLEDGE/BOOKS/Deep_Learning_Representations_and_Attention.md
- relation: references
  target: 01_KNOWLEDGE/BOOKS/ADVANCED_Transformer_RoPE_Attention_Sampling.md
- relation: references
  target: 01_KNOWLEDGE/BOOKS/SPECIALIZED_Transformer_Optimization_AdamW_Dynamics.md
- relation: references
  target: 01_KNOWLEDGE/BOOKS/Caiet_Teme_Aplicatii_Practice_Carti.md
---

# Deep Learning M?iestrie: Grouped-Query Attention (GQA), MQA & Dinamica Aten?iei pe Secven?e Lungi

**Surs?**: Magnus Ekman, *Learning Deep Learning* (Capitolele 13 & 14) & Inova?iile Moderne Transformer (Ainslie et al., Shazeer)  
**Domeniu**: Arhitecturi Transformer Scalabile, Aten?ie Multi-Cap & Dinamica Gradientelor

---

## 1. Evolu?ia Mecanismelor de Aten?ie: MHA vs MQA vs GQA

L??imea de band? a memoriei GPU (*Memory Bandwidth Bound*) reprezint? g?tul de g?sc? principal ?n generarea autoregresiv? a tokenilor. Evolu?ia arhitecturii aten?iei rezolv? aceast? problem? prin partajarea cheilor ?i valorilor:

```text
  Multi-Head Attention (MHA)      Grouped-Query Attention (GQA)      Multi-Query Attention (MQA)
  [Q1 Q2] [Q3 Q4] [Q5 Q6] [Q7 Q8] [Q1 Q2] [Q3 Q4] [Q5 Q6] [Q7 Q8]   [Q1 Q2 Q3 Q4 Q5 Q6 Q7 Q8]
     |       |       |       |        \     /       \     /                      |
  [K1 V1] [K2 V2] [K3 V3] [K4 V4]     [K1 V1]         [K2 V2]                  [K1 V1]
  (8 capete Q, 8 capete KV)       (8 capete Q, 2 grupuri KV)        (8 capete Q, 1 singur set KV)
```

### Compara?ie Arhitectural?

| Arhitectur? | Capete Q | Capete KV | Dimensiune KV-Cache | Vitez? Inferen?? | Calitate Reprezentare |
| :--- | :---: | :---: | :---: | :---: | :---: |
| **MHA (Vaswani et al.)** | $H$ | $H$ | $100\%$ (Referin??) | Standard | Maxim? |
| **MQA (Shazeer, 2019)** | $H$ | $1$ | $1/H$ (Reducere masiv?) | Ultrarapid? | U?oar? degradare a capacit??ii |
| **GQA (Ainslie et al., 2023)** | $H$ | $G$ ($1 < G < H$) | $G/H$ (ex: $1/4$ sau $1/8$) | Aproape egal? cu MQA | P?streaz? calitatea MHA |

*Exemplu ?n LLaMA-3 70B*: Folose?te 64 capete Q ?i 8 capete KV ($G = 8$), ob?in?nd o compresie de 8x a memoriei KV f?r? pierderi de performan??.

---

## 2. Algoritmul Matematic de Calcul GQA

Fie $H$ num?rul total de capete Query ?i $G$ num?rul de grupuri Key/Value. Fiecare grup deserve?te $k = H / G$ capete Query:
1. Pentru fiecare grup $g \in \{1, \dots, G\}$:
   - Se extrag cheile $K_g \in \mathbb{R}^{L \times d_{\text{head}}}$ ?i valorile $V_g \in \mathbb{R}^{L \times d_{\text{head}}}$.
   - Pentru fiecare cap query $j \in \{1, \dots, k\}$ asociat grupului $g$:
     $$\text{Attention}_{g, j} = \operatorname{softmax}\left( \frac{Q_{g, j} K_g^T}{\sqrt{d_{\text{head}}}} + M \right) V_g$$
2. Ie?irile tuturor celor $H$ capete sunt concatenate ?i proiectate liniar prin matricea de ie?ire $W_O \in \mathbb{R}^{d_{\text{model}} \times d_{\text{model}}}$.

---

## 3. Dinamica Aten?iei pe Secven?e Lungi: Fenomenul "Attention Sink"

?n secven?e foarte lungi ($> 32k$ tokens), aten?ia modelului sufer? modific?ri calitative ne-intuitive:
- **Fenomenul Attention Sink (StreamingLLM)**: O mare parte din masa de probabilitate a aten?iei softmax este direc?ionat? persistent c?tre primii $1-4$ tokeni din context (adesea tokenul `<s>` de start), chiar dac? ace?tia nu con?in informa?ie semantic? relevant?.
- **Cauza Matematic?**: Softmax for?eaz? suma probabilit??ilor la 1. C?nd niciun token anterior nu este relevant pentru pasul curent, modelul folose?te primii tokeni ca un depozit gol (*sink*) pentru a desc?rca greutatea rezidual?.
- **Solu?ie Opera?ional?**: P?strarea permanent? a primilor tokeni ?n KV-cache al?turi de o fereastr? glisant? (*rolling buffer*) previne pr?bu?irea complet? a perplexit??ii modelului la streaming continuu.

