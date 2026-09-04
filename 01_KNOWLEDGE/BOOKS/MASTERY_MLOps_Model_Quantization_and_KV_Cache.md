---
id: cf00cbf2-4033-5984-905a-781102ad7005
type: knowledge
lifecycle: REVIEW
category: mlops/quantization_and_kv_cache
tags:
- mlops
- huyen
- quantization
- ptq
- qat
- int8
- kv-cache
- paged-attention
- vllm
- inference-optimization
created: '2026-09-04'
updated: '2026-09-04'
provenance:
  source_type: ai
  source_ref: 06_INBOX/RAW_IMPORTS/BOOKS/Chip-Huyen-ML-Systems-Ch7-9
confidence: high
verification: unverified
relations:
- relation: references
  target: 01_KNOWLEDGE/BOOKS/Production_ML_Systems_and_Continual_Learning.md
- relation: references
  target: 01_KNOWLEDGE/BOOKS/ADVANCED_MLOps_Feature_Stores_Continual_Learning.md
- relation: references
  target: 01_KNOWLEDGE/BOOKS/SPECIALIZED_MLOps_Weak_Supervision_and_Snorkel.md
- relation: references
  target: 01_KNOWLEDGE/BOOKS/Caiet_Teme_Aplicatii_Practice_Carti.md
---

# MLOps M?iestrie: Cuantizarea Modelelor (PTQ vs QAT) & Dinamica Memoriei KV-Cache

**Surs?**: Chip Huyen, *Designing Machine Learning Systems* (Capitolele 7 & 9) & Sisteme Moderne de Inferen?? LLM  
**Domeniu**: Optimizarea Modelelor la Inferen??, Compresie Numeric? & Gestiunea Memoriei VRAM

---

## 1. Fundamentele Cuantiz?rii Numerice: De la FP32 la INT8/INT4

Cuantizarea reduce precizia reprezent?rii greut??ilor ?i activ?rilor re?elei pentru a mic?ora consumul de l??ime de band? a memoriei GPU ?i a accelera opera?iile matriciale.

### A. Cuantizare Uniform? Asimetric? (Affine Quantization)
Maparea unei valori reale continue $r \in [r_{\min}, r_{\max}]$ ?ntr-un ?ntreg cuantizat $q \in [q_{\min}, q_{\max}]$:
$$q = \operatorname{round}\left(\frac{r}{S}\right) + Z, \quad \tilde{r} = S \cdot (q - Z)$$
Unde:
- **Scara (*Scale*)**: $S = \frac{r_{\max} - r_{\min}}{q_{\max} - q_{\min}}$
- **Punctul Zero (*Zero-Point*)**: $Z = \operatorname{round}\left(\frac{-r_{\min}}{S}\right) + q_{\min}$

### B. Cuantizare Uniform? Simetric?
For?eaz? simetria intervalului real $[-r_{\max}, r_{\max}]$ ?n jurul valorii zero ($Z = 0$):
$$S = \frac{\max(|r_{\min}|, |r_{\max}|)}{q_{\max}}, \quad q = \operatorname{round}\left(\frac{r}{S}\right)$$
- **Avantaj**: Elimin? termenii de corec?ie din produsul scalar matricial, permi??nd instruc?iuni hardware SIMD ultrarapide.

---

## 2. Strategii de Cuantizare: PTQ vs QAT

| Proprietate | Post-Training Quantization (PTQ) | Quantization-Aware Training (QAT) |
| :--- | :--- | :--- |
| **Moment de aplicare** | Dup? finalizarea complet? a antren?rii | ?n timpul antren?rii sau al fine-tuning-ului |
| **Resurse de calcul** | Minime (c?teva minute pe CPU/GPU) | Ridicate (necesit? epoci suplimentare) |
| **Set de date necesar** | Doar un mic set de calibrare (128-512 e?antioane) | Set complet de date de antrenare cu etichete |
| **Tratarea gradientului** | Nu exist? retropropagare | Folose?te STE (*Straight-Through Estimator*) |
| **Acurate?e** | Excelent? pentru INT8; degradare la INT4 | Men?ine acurate?ea maxim? chiar ?i la INT4 |

---

## 3. Dinamica ?i Dimensionarea Memoriei KV-Cache

?n faza de generare autoregresiv? (decodare pas cu pas), vectorii de Cheie ($K$) ?i Valoare ($V$) calcula?i pentru to?i tokenii anteriori sunt memora?i ?n RAM-ul GPU pentru a evita recalcularea $\mathcal{O}(N^2)$.

### Formula Amprentei de Memorie pentru KV-Cache
Pentru un model cu $n_{\text{layers}}$ straturi, $n_{\text{kv\_heads}}$ capete per strat, dimensiunea capului $d_{\text{head}}$, lungimea secven?ei $L_{\text{seq}}$ ?i dimensiunea lotului $B$:
$$M_{\text{KV}} = 2 \times n_{\text{layers}} \times n_{\text{kv\_heads}} \times d_{\text{head}} \times L_{\text{seq}} \times B \times \text{bytes\_per\_element}$$

*Exemplu concret (Model LLaMA-3 8B, 32 straturi, 8 KV heads, $d_{\text{head}} = 128$, FP16 = 2 bytes)*:
- Per token per batch: $2 \times 32 \times 8 \times 128 \times 2 = 131.072 \text{ bytes} = 128 \text{ KB/token}$.
- Pentru un context de $32.000$ tokens ?i $B = 4$:
  $$M_{\text{KV}} = 128 \text{ KB} \times 32.000 \times 4 \approx 16.384 \text{ MB} = 16 \text{ GB VRAM doar pentru KV-Cache!}$$

### PagedAttention ?i Gestionarea Fragment?rii
Inspirat din memoria virtual? a sistemelor de operare (paging):
- VRAM-ul este parti?ionat ?n blocuri de memorie de m?rime fix? (ex: 16 tokeni per bloc).
- Tokenii noi sunt aloca?i ?n blocuri non-contigue fizic, elimin?nd fragmentarea intern? ?i permi??nd partajarea instantanee a prompturilor comune ?ntre cereri concurente.

