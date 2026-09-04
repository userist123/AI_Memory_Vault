---
id: 6ff8f874-8e01-5aa5-8f84-e94caa4ec987
type: knowledge
lifecycle: REVIEW
category: architecture/deep_learning
tags:
- deep-learning
- ekman
- rope
- multi-head-attention
- sampling-strategies
- top-p-nucleus
- min-p
- temperature
- flash-attention
created: '2026-09-04'
updated: '2026-09-04'
provenance:
  source_type: ai
  source_ref: "06_INBOX/RAW_IMPORTS/BOOKS/Magnus-Ekman-Learning-Deep-Learning-Ch14-15"
confidence: high
verification: unverified
relations:
- relation: references
  target: 01_KNOWLEDGE/BOOKS/Deep_Learning_Representations_and_Attention.md
- relation: references
  target: 01_KNOWLEDGE/BOOKS/Caiet_Teme_Aplicatii_Practice_Carti.md
- relation: references
  target: 00_CORE/System_Architecture.md
---

# Deep Learning Avansat: Încadrare Pozițională Rotativă (RoPE), Atenție & Strategii de Eșantionare

**Sursă**: Magnus Ekman, *Learning Deep Learning* (Capitolele 14–15)  
**Domeniu**: Arhitecturi Transformer de Ultimă Generație, Mecanica Inferenței & Sampling

---

## 1. Încadrare Pozițională Rotativă (Rotary Position Embedding — RoPE)

Spre deosebire de încorporările poziționale absolute (adăugate ca vectori la intrare) sau relative clasice, RoPE (Su et al., 2021) încorporează informația de poziție prin **rotirea** vectorilor de interogare ($Q$) și cheie ($K$) în planul complex:

### Formularea Matematică RoPE
Pentru un vector 2D la poziția $m$, rotația cu unghiul $m\theta$ este:

$$R_{\Theta, m} x = \begin{pmatrix} \cos m\theta & -\sin m\theta \\ \sin m\theta & \cos m\theta \end{pmatrix} \begin{pmatrix} x_1 \\ x_2 \end{pmatrix}$$

Produsul scalar între o interogare la poziția $m$ și o cheie la poziția $n$ depinde exclusiv de distanța relativă $(m - n)$:

$$\langle R_{\Theta, m} q, R_{\Theta, n} k \rangle = \text{Re}\left( (q e^{i m \theta}) (k e^{i n \theta})^* \right) = \text{Re}\left( q k^* e^{i (m - n) \theta} \right)$$

Această proprietate conferă modelului o capacitate superioară de generalizare la lungimi de context mari și degradare lină a atenției cu distanța.

---

## 2. Atenția Multi-Head cu Mască Cauzală (Causal Multi-Head Attention)

Pentru a asigura că jetonul curent $t$ nu poate privi în viitor ($t' > t$), se aplică o matrice de mască triunghiulară superioară:

$$M_{ij} = \begin{cases} 0, & \text{dacă } i \ge j \\ -\infty, & \text{dacă } i < j \end{cases}$$

$$\text{Attention}(Q, K, V) = \text{softmax}\left(\frac{QK^T}{\sqrt{d_k}} + M\right)V$$

Valorile $-\infty$ devin exact $0$ după aplicarea funcției $\text{softmax}$, anulând complet gradientul și ponderile de atenție pentru pozițiile viitoare.
- **Optimizarea FlashAttention (Dao et al.)**: Elimină scrierea în memoria HBM a matricei dense de atenție $N \times N$, realizând fuziunea operațiilor de softmax și calcul bloc cu bloc direct în memoria SRAM a GPU-ului (reducând complexitatea I/O de la $\mathcal{O}(N^2)$ la $\mathcal{O}(N)$).

---

## 3. Strategii Avansate de Eșantionare (Decoding Strategies)

Generarea autoregresivă depinde critic de modul în care probabilitățile $P(x_t \mid x_{<t})$ sunt filtrate înainte de tragerea la sorți a jetonului:

1. **Scalarea prin Temperatură ($T$)**:
   $$P'(x_i) = \frac{\exp(z_i / T)}{\sum_j \exp(z_j / T)}$$
   $T \to 0$ produce căutare greedy deterministă; $T > 1$ aplatizează distribuția, stimulând creativitatea.
2. **Top-p (Nucleus Sampling)**: Selectează cel mai mic set de jetoane a căror probabilitate cumulată depășește pragul $p \in (0, 1]$:
   $$\sum_{i \in V^{(p)}} P(x_i) \ge p$$
3. **Min-p Sampling**: O alternativă modernă superioară la Top-p, care elimină jetoanele a căror probabilitate este mai mică decât o fracțiune $\text{min\_p}$ din probabilitatea celui mai probabil jeton:
   $$P(x_i) \ge \text{min\_p} \times \max_j P(x_j)$$
   Dacă jetonul de top are certitudine $99\%$, doar jetoane foarte plauzibile sunt luate în calcul; dacă distribuția este difuză, spațiul de candidați se deschide automat.
4. **Penalizarea Repetiției (Repetition Penalty)**: Împarte logit-ul jetonilor deja prezenți în context la un factor $\theta > 1.0$ pentru a evita buclele degenerative.

---

## 4. Playbook Operațional: Ce fac când configurez parametrii de generare și inferență?

1. **Pentru raționament, cod și extragere de date (Tasks Structurate)**: Setez $T = 0.0$ sau $T = 0.2$ cu $\text{min\_p} = 0.05$ pentru a elimina complet halucinațiile din coada distribuției.
2. **Pentru generare creativă sau sinteză liberă**: Setez $T = 0.7$, $\text{top\_p} = 0.9$ și $\text{repetition\_penalty} = 1.1$.
3. **Pentru contexte mari (> 8k tokens)**: Mă asigur că modelul folosește RoPE cu factor de scalare $\theta$ adecvat (ex: YaRN sau RoPE-scaling) și activez FlashAttention-2 pentru a preveni căderea în OOM.
