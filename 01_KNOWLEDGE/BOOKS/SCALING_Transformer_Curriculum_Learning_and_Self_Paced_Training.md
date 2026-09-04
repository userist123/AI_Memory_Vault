---
id: c83e7ee9-2262-570b-8f38-e95f8c59ad5b
type: knowledge
lifecycle: REVIEW
category: deep_learning/curriculum_learning_self_paced
tags:
- deep-learning
- ekman
- curriculum-learning
- self-paced
- training-dynamics
- sample-difficulty
- progressive-training
- baby-step
created: '2026-09-04'
updated: '2026-09-04'
provenance:
  source_type: ai
  source_ref: 06_INBOX/RAW_IMPORTS/BOOKS/Ekman-Learning-Deep-Learning-Ch13-Ch14
confidence: high
verification: unverified
relations:
- relation: references
  target: 01_KNOWLEDGE/BOOKS/PRODUCTION_Transformer_Knowledge_Distillation_and_Model_Compression.md
- relation: references
  target: 01_KNOWLEDGE/BOOKS/HARDENING_Transformer_Numerical_Stability_and_Catastrophic_Forgetting.md
- relation: references
  target: 01_KNOWLEDGE/BOOKS/Caiet_Teme_Aplicatii_Practice_Carti.md
---

# Deep Learning Scaling: Curriculum Learning și Antrenament Auto-Ghidat

**Sursă**: Magnus Ekman, *Learning Deep Learning* (Capitolele 13-14: Advanced Training Strategies)
**Domeniu**: Ordonarea Eșantioanelor de Antrenament, Dificultate Progresivă, Accelerarea Convergenței

---

## 1. Motivația: Ordinea Contează

### 1.1 Analogia Pedagogică

Un student uman nu începe cu ecuații diferențiale — începe cu aritmetică:

```
Aritmetică → Algebră → Calcul → Ecuații Diferențiale
   (ușor)                              (greu)
```

Bengio et al. (2009) au arătat că **aceeași strategie** funcționează pentru rețele neurale: prezentarea eșantioanelor în ordine de dificultate crescătoare accelerează convergența și îmbunătățește generalizarea.

### 1.2 Evidența Empirică

| Scenariu | Acuratețe Finală | Epoci până la Convergență |
| :--- | :--- | :--- |
| Random shuffling | 85.2% | 100 |
| Easy → Hard (curriculum) | 87.1% | 70 |
| Hard → Easy (anti-curriculum) | 82.3% | 120 |

---

## 2. Curriculum Learning (Bengio 2009)

### 2.1 Principiul

1. **Definește** o funcție de dificultate $d(x_i)$ pentru fiecare eșantion
2. **Sortează** eșantioanele de la ușor la greu
3. **Antrenează** progresiv: începe cu cele mai ușoare, adaugă treptat cele grele

### 2.2 Funcții de Dificultate

| Criteriu | Formulă/Mecanism | Când se folosește |
| :--- | :--- | :--- |
| **Loss-based** | $d(x_i) = \mathcal{L}(f(x_i), y_i)$ | General (loss mare = greu) |
| **Confidence-based** | $d(x_i) = 1 - P(y_i \mid x_i)$ | Clasificare |
| **Lungime** | $d(x_i) = \text{len}(x_i)$ | NLP (propoziții lungi = grele) |
| **Noise level** | $d(x_i) = \text{SNR}^{-1}(x_i)$ | Imagini/audio |
| **Manual** | Expert annotation | Când e disponibil |

### 2.3 Strategie de Pachetizare (Pacing Function)

```
Epoca 1:  [────────] doar 20% cele mai ușoare
Epoca 5:  [──────────────] 50% (cresc treptat)
Epoca 10: [────────────────────] 80%
Epoca 15: [──────────────────────────] 100% (tot setul)
```

Funcția de pacing controlează **ritmul** de introducere a eșantioanelor:

$$\lambda(t) = \min\left(1, \lambda_0 + \frac{t}{T} \cdot (1 - \lambda_0)\right)$$

Unde $\lambda(t)$ = fracțiunea din dataset disponibilă la epoca $t$.

---

## 3. Self-Paced Learning (SPL)

### 3.1 Diferența față de Curriculum Learning

| Aspect | Curriculum Learning | Self-Paced Learning |
| :--- | :--- | :--- |
| **Cine decide dificultatea?** | Pre-definit (fix) | Modelul însuși (dinamic) |
| **Adaptivitate** | Nu | Da (se actualizează la fiecare epocă) |
| **Mecanism** | Sorting pre-antrenament | Variabile latente de selecție |

### 3.2 Formularea Matematică

$$\min_{\theta, v} \sum_{i=1}^{N} v_i \cdot \mathcal{L}(f_\theta(x_i), y_i) - \lambda \sum_{i=1}^{N} v_i$$

Unde:
- $v_i \in \{0, 1\}$ — variabila de selecție (1 = eșantion inclus)
- $\lambda$ — pragul de dificultate (crește progresiv)

**Soluție optimă** (la $\theta$ fix):
$$v_i^* = \begin{cases} 1 & \text{dacă } \mathcal{L}_i < \lambda \\ 0 & \text{altfel} \end{cases}$$

La $\lambda$ mic → doar eșantioanele ușoare. La $\lambda \to \infty$ → toate eșantioanele.

---

## 4. Variante Moderne

### 4.1 Curriculum Dropout

Rata de dropout crește progresiv:
```
Epocile 1-10:  dropout = 0.1 (puțină regularizare, învață repede)
Epocile 10-30: dropout = 0.3 (regularizare moderată)
Epocile 30+:   dropout = 0.5 (regularizare agresivă, fine-tuning)
```

### 4.2 Data Mixing (Multi-Task Curriculum)

```
Faza 1: 80% date simple + 20% date complexe
Faza 2: 60% simple + 40% complexe
Faza 3: 40% simple + 60% complexe
```

Foarte folosit în **antrenarea LLM-urilor**: GPT-4, LLaMA își variază mixul de date (web, cod, cărți) pe parcursul antrenamentului.

### 4.3 Competence-Based Curriculum

```python
competence_t = min(1.0, sqrt(t / T))  # crește subliniar

# La fiecare epocă:
threshold = quantile(all_losses, competence_t)
batch = sample(x for x in dataset if loss(x) < threshold)
```

---

## 5. Anti-Patternuri

| Anti-Pattern | Consecință | Soluție |
| :--- | :--- | :--- |
| Curriculum prea lent | Underfitting pe date complexe | Pacing function mai agresivă |
| Curriculum prea rapid | Nu beneficiază de easy-first | Start cu λ₀ = 0.1-0.2 |
| Dificultate calculată o singură dată | Stale difficulty scores | Recalculare la fiecare N epoci |
| Ignorare noise samples | Memorare de noise | Filter noise la loss > percentila 95 |

---

## 6. Aplicabilitate în Memory Vault

- **Indexare Progresivă**: Notele simple (tags clare, metadata completă) sunt indexate mai întâi; cele ambigue ulterior
- **Learning Path Design**: Tema caietului de teme urmează exact un curriculum: Tier 1 (core) → Tier 9 (scaling)
- **Embedding Fine-Tuning**: Curriculum learning pe embedding model — începe cu queries simple, progresează la queries complexe

---

## Referințe Obsidian

- [[PRODUCTION_Transformer_Knowledge_Distillation_and_Model_Compression]]
- [[HARDENING_Transformer_Numerical_Stability_and_Catastrophic_Forgetting]]
- [[EXPERT_Transformer_Mixture_of_Experts_and_Routing]]
- [[Caiet_Teme_Aplicatii_Practice_Carti]]
