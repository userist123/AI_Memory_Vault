---
id: 31064d57-7eda-5720-8744-3c563403d570
type: knowledge
lifecycle: REVIEW
category: deep_learning/moe_and_routing
tags:
- deep-learning
- ekman
- mixture-of-experts
- moe
- top-k-routing
- load-balancing-loss
- sparse-activation
- mixtral
created: '2026-09-04'
updated: '2026-09-04'
provenance:
  source_type: ai
  source_ref: 06_INBOX/RAW_IMPORTS/BOOKS/Magnus-Ekman-Learning-DL-Ch14-MoE
confidence: high
verification: unverified
relations:
- relation: references
  target: 01_KNOWLEDGE/BOOKS/Deep_Learning_Representations_and_Attention.md
- relation: references
  target: 01_KNOWLEDGE/BOOKS/MASTERY_Transformer_GQA_MQA_and_Sequence_Dynamics.md
- relation: references
  target: 01_KNOWLEDGE/BOOKS/Caiet_Teme_Aplicatii_Practice_Carti.md
---

# Deep Learning Expert: Mixture of Experts (MoE), Rutare Spars? & Echilibrarea ?nc?rc?rii

**Surs?**: Magnus Ekman, *Learning Deep Learning* (Capitolul 14) & Modele Sparse Moderne (Shazeer, Mixtral)  
**Domeniu**: Arhitecturi Neurale Scalabile, Calcul Spars Condi?ionat & Optimizare Multi-Expert

---

## 1. De la Modele Dense la Modele Sparse MoE

Modelele dense tradi?ionale activeaz? 100% din parametrii re?elei pentru fiecare token procesat. Aceasta devine nesustenabil la sute de miliarde de parametri.
Arhitectura **Mixture of Experts (MoE)** ?nlocuie?te stratul Feed-Forward (FFN) dens cu $E$ re?ele independente de exper?i ?i o re?ea de rutare (*Gating Network*):

```text
               [ Intrare Token x ]
                     /     \
       [ Ruter / Gating ]   \
       Ponderi Top-2:        \
       E1: 0.7, E3: 0.3       \
             /        \        \
            v          v        v
      [ Expert 1 ]  [ Expert 3 ] ... [ Exper?i 2, 4..8 Inactivi ]
            \          /
             v        v
         [ Sum? Ponderat?: 0.7 * y1 + 0.3 * y3 ]
```

- **Eficien?? Computa?ional?**: Un model cu 47 miliarde de parametri totali (ex: Mixtral 8x7B) activeaz? doar 13 miliarde per token, oferind viteza unui model de 13B cu capacitatea de cunoa?tere a unui model mult mai mare!

---

## 2. Mecanismul Matematic de Rutare Top-$k$ Softmax

Fie $x$ reprezentarea tokenului ?i $W_g \in \mathbb{R}^{d \times E}$ matricea de proiec?ie a ruterului:
1. **Scorurile Logit**: $H(x) = x W_g + \epsilon$, unde $\epsilon \sim \mathcal{N}(0, \sigma^2)$ adaug? zgomot de explorare ?n antrenare.
2. **Selec?ia Top-$k$**: Se re?in doar cei mai mari $k$ indici (de regul? $k = 2$):
   $$\text{KeepTopK}(v, k)_i = \begin{cases} v_i & \text{dac? } v_i \text{ este ?n primii } k \\ -\infty & \text{altfel} \end{cases}$$
3. **Ponderile Softmax Normalizate**:
   $$G(x) = \operatorname{softmax}(\text{KeepTopK}(H(x), k))$$
4. **Ie?irea Stratului**:
   $$y = \sum_{i \in \text{TopK}} G(x)_i \cdot \text{Expert}_i(x)$$

---

## 3. Pr?bu?irea Rut?rii & Func?ia de Pierdere Auxiliar? de Echilibrare (*Load Balancing Loss*)

F?r? constr?ngeri suplimentare, ruterul tinde s? favorizeze c??iva exper?i populari (*winner-takes-all*), l?s?nd restul exper?ilor neantrena?i ?i supra?nc?rc?nd nodurile de calcul.

Pentru a for?a distribuirea uniform? a tokenilor ?ntre exper?i, se introduce o penalizare auxiliar?:
$$\mathcal{L}_{\text{balance}} = \alpha \cdot E \sum_{i=1}^E f_i \cdot P_i$$
Unde:
- $f_i = \frac{1}{N} \sum_{x} \mathbb{I}(\text{Expert } i \text{ selectat})$ este frac?iunea de tokeni direc?iona?i c?tre expertul $i$.
- $P_i = \frac{1}{N} \sum_{x} G(x)_i$ este probabilitatea medie alocat? expertului $i$.
- Minimul se atinge c?nd to?i $f_i = 1/E$ ?i to?i $P_i = 1/E$, garant?nd o paralelizare hardware optim?.

