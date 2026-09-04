---
id: f19267f1-6520-4802-ae5c-7ddcf22f9d72
type: knowledge
lifecycle: REVIEW
category: mlops/shadow_deployment_bandits
tags:
- mlops
- huyen
- shadow-deployment
- canary
- multi-armed-bandit
- thompson-sampling
- circuit-breaker
- capstone
created: '2026-09-04'
updated: '2026-09-04'
provenance:
  source_type: ai
  source_ref: 06_INBOX/RAW_IMPORTS/BOOKS/Chip-Huyen-ML-Systems-Ch8-9
confidence: high
verification: unverified
relations:
- relation: references
  target: 01_KNOWLEDGE/BOOKS/EXPERT_MLOps_RealTime_Streaming_Features_and_Windows.md
- relation: references
  target: 01_KNOWLEDGE/BOOKS/Caiet_Teme_Aplicatii_Practice_Carti.md
- relation: references
  target: 00_CORE/GRAPH/07 Knowledge Domains Map.md
---

# MLOps Capstone: Rulare ?n Umbr? (Shadow Deployment), Direc?ionare prin Bandi?i & Rollback Automat

**Surs?**: Chip Huyen, *Designing Machine Learning Systems* (Capitolele 8-9) + Automated Safe Deployment Patterns  
**Domeniu**: Lans?ri ?n Produc?ie, ?nv??are prin Re?nt?rire Online & Monitorare a Riscului Opera?ional

---

## 1. Taxonomia Strategiilor de Deployment
?n sistemele critice de produc?ie, comutarea direct? (*Blue/Green swap*) prezint? riscul propag?rii instantanee a erorilor de predic?ie sau degrad?rii de memorie. Alternativele sigure sunt:
1. **Shadow Deployment (Rulare ?n Umbr?)**: Noul model ($M_{\text{cand}}$) prime?te 100% din traficul real ?n paralel cu modelul stabil de baz? ($M_{\text{prod}}$), ?ns? predic?iile sale sunt salvate doar ?n telemetrie pentru analiz? diferen?ial?, f?r? a ajunge la utilizator.
2. **Canary Routing**: O cot? redus? de trafic ($p \in [0.01, 0.05]$) este alocat? noului model, cresc?nd progresiv dac? ratele de eroare ?i laten?a $P_{99}$ r?m?n sub pragurile limit?.
3. **Bandit-Driven Routing (Thompson Sampling)**: Traficul este alocat dinamic modelelor candidate ?n func?ie de probabilitatea posterioar? ca fiecare model s? fie optim conform unei distribu?ii Beta-Bernoulli:
   $$\theta_k \sim \text{Beta}(\alpha_k, \beta_k)$$
   Trimiterea cererii c?tre $k^* = \arg\max_k \theta_k$. Dac? r?spunsul este valid, $\alpha_{k^*} \leftarrow \alpha_{k^*} + 1$; altfel $\beta_{k^*} \leftarrow \beta_{k^*} + 1$.

## 2. Teste de Degradare ?i Circuit Breaker Automat
Dac? testul Kolmogorov-Smirnov cu dou? e?antioane pe distribu?ia scorurilor de ie?ire indic? $p < 0.01$ sau rata de excep?ii dep??e?te $\epsilon = 0.02$ ?ntr-o fereastr? de 5 minute, circuit breaker-ul decupleaz? instantaneu noul model ?i readuce tot traficul pe modelul de baz? securizat.

## 3. Leg?turi Canonice & Graf de Cuno?tin?e
- [[Production_ML_Systems_and_Continual_Learning]]
- [[ADVANCED_MLOps_Feature_Stores_Continual_Learning]]
- [[EXPERT_MLOps_RealTime_Streaming_Features_and_Windows]]
- [[Caiet_Teme_Aplicatii_Practice_Carti]]
- [[07 Knowledge Domains Map]]
