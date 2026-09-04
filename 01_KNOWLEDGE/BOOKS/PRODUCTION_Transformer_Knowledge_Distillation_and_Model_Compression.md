---
id: 96b1469a-cd5d-5531-9c9d-e884d34276f2
type: knowledge
lifecycle: REVIEW
category: deep_learning/knowledge_distillation_compression
tags:
- deep-learning
- ekman
- knowledge-distillation
- teacher-student
- model-compression
- pruning
- quantization-aware
- soft-targets
created: '2026-09-04'
updated: '2026-09-04'
provenance:
  source_type: ai
  source_ref: 06_INBOX/RAW_IMPORTS/BOOKS/Ekman-Learning-Deep-Learning-Ch15-Ch17
confidence: high
verification: unverified
relations:
- relation: references
  target: 01_KNOWLEDGE/BOOKS/EXPERT_Transformer_Mixture_of_Experts_and_Routing.md
- relation: references
  target: 01_KNOWLEDGE/BOOKS/MASTERY_MLOps_Model_Quantization_and_KV_Cache.md
- relation: references
  target: 01_KNOWLEDGE/BOOKS/Caiet_Teme_Aplicatii_Practice_Carti.md
---

# Deep Learning Production: Distilare de Cunoștințe și Compresie de Model

**Sursă**: Magnus Ekman, *Learning Deep Learning* (Capitolele 15-17: Model Optimization & Deployment)
**Domeniu**: Transfer de Cunoștințe Teacher→Student, Pruning Structurat, Compresie Multi-Tehnici

---

## 1. Motivația Compresiei de Modele

| Metric | Model Mare (Teacher) | Model Mic (Student) |
| :--- | :--- | :--- |
| Parametri | 70B | 7B |
| Latență (GPU) | 200ms | 25ms |
| Memorie | 140 GB FP16 | 14 GB FP16 |
| Acuratețe | 92% | 85% (fără distilare) → 89% (cu distilare) |
| Cost/1M tokens | \$15 | \$0.50 |

**Scopul**: Obținem 96% din performanța teacher-ului la 3% din cost.

---

## 2. Knowledge Distillation (Hinton et al., 2015)

### 2.1 Principiul Fundamental

Teacher-ul produce **soft targets** (distribuții de probabilitate) care conțin mai multă informație decât hard labels:

$$P_i^{\text{soft}} = \frac{\exp(z_i / T)}{\sum_j \exp(z_j / T)}$$

Unde $T$ = **temperatura**:
- $T = 1$ → distribuție standard (peaks ascuțite)
- $T = 5\text{-}20$ → distribuție netezită (transferă "dark knowledge")

### 2.2 Funcția de Pierdere Combinată

$$\mathcal{L} = \alpha \cdot T^2 \cdot D_{KL}(P_{\text{teacher}}^T \| P_{\text{student}}^T) + (1-\alpha) \cdot \mathcal{L}_{CE}(y, P_{\text{student}}^1)$$

| Component | Rol |
| :--- | :--- |
| $D_{KL}$ cu soft targets | Transferă cunoștințele inter-clasă din teacher |
| $\mathcal{L}_{CE}$ cu hard labels | Menține acuratețea pe etichete reale |
| $T^2$ | Factor de scalare (compensează magnitudinea gradienților la T mare) |
| $\alpha$ | Balansul între cele două pierderi (tipic 0.5-0.9) |

### 2.3 "Dark Knowledge"

Ce învață studentul din soft targets:
```
Hard label:    [1, 0, 0]  (doar "pisică")
Soft targets:  [0.85, 0.10, 0.05]  (pisică, dar seamănă puțin cu câine)
```

Informația „seamănă cu câine" este **dark knowledge** — nu apare în label-ul hard, dar ajută studentul să generalizeze mai bine.

---

## 3. Variante de Distilare

### 3.1 Feature-Based Distillation (FitNets)

Nu doar output-ul final, ci și **reprezentările intermediare**:

```
Teacher layer 6  ←→  Student layer 3
Teacher layer 12 ←→  Student layer 6

Lossfeat = Σᵢ ||Wᵢ · fstudent(x) - fteacher(x)||²
```

Necesită **adaptor liniar** $W_i$ deoarece dimensiunile pot diferi.

### 3.2 Self-Distillation

Modelul este propriul teacher dintr-o epocă anterioară:
```
Epoca 1: Antrenare normală → Salvare checkpoint T₁
Epoca 2: Antrenare cu soft targets din T₁
Epoca 3: Antrenare cu soft targets din T₂
```

Avantaj: Nu necesită model teacher separat.

### 3.3 Data-Free Distillation

Când datele originale nu sunt disponibile:
1. **Generator adversarial** produce date sintetice
2. Teacher-ul etichetează datele sintetice cu soft targets
3. Studentul antrenează pe datele sintetice etichetate

---

## 4. Pruning Structurat

### 4.1 Unstructured vs Structured

| Tip | Unitate Eliminată | Speedup Real | Implementare |
| :--- | :--- | :--- | :--- |
| **Unstructured** | Weights individuale | ~1x (sparsity) | Necesită hardware special |
| **Structured** | Neuroni/canale/atenții | 2-4x real | Standard pe orice GPU |

### 4.2 Criteriu de Importanță

```python
# L1 Magnitude Pruning
importance = torch.abs(layer.weight).sum(dim=1)  # per neuron
threshold = torch.quantile(importance, prune_ratio)
mask = importance > threshold
pruned_weight = layer.weight[mask]
```

### 4.3 Iterative Pruning (Lottery Ticket Hypothesis)

```
Train → Prune 20% → Retrain → Prune 20% → Retrain → ...
```

Frankle & Carlin (2019): Rețelele conțin **sub-rețele câștigătoare** care, antrenate izolat de la inițializarea originală, ating aceeași performanță.

---

## 5. Combinarea Tehnicilor

### 5.1 Pipeline Complet de Compresie

```
Model Original (70B FP32)
    ↓ Knowledge Distillation → Student (7B FP32)
    ↓ Structured Pruning (30%) → Student Pruned (5B FP32)
    ↓ Quantization-Aware Training → Student INT8 (5B INT8)
    ↓ Final: ~2.5 GB, 15ms latency
```

### 5.2 Comparație de Tehnici

| Tehnica | Compresie | Pierdere Acuratețe | Compatibilitate |
| :--- | :--- | :--- | :--- |
| **Distilare** | 10x parametri | 1-3% | Orice arhitectură |
| **Pruning Structurat** | 2-4x | 1-2% (cu retrain) | Standard |
| **Quantizare INT8** | 2-4x memorie | <1% | Hardware modern |
| **LoRA** (din Tier 1) | 100x parametri antrenabili | <1% | Fine-tuning efficient |

---

## 6. Aplicabilitate în Memory Vault

- **Embedding Model Selection**: Distilarea unui embedding model mare (768d) într-unul mic (384d) pentru search mai rapid
- **Local Inference**: Model distilat pentru clasificare de note fără apel API
- **Quantization Chain**: INT8 + distilare pentru deployment pe edge (laptop fără GPU dedicat)

---

## Referințe Obsidian

- [[EXPERT_Transformer_Mixture_of_Experts_and_Routing]]
- [[MASTERY_MLOps_Model_Quantization_and_KV_Cache]]
- [[CAPSTONE_Transformer_FlashAttention_Tiling_and_IO_Awareness]]
- [[Caiet_Teme_Aplicatii_Practice_Carti]]
