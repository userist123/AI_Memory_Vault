---
id: 706a8256-36f8-429e-96b6-384f4ec6edc8
type: knowledge
lifecycle: REVIEW
category: llm/dpo_contrastive_alignment
tags:
- llm
- zvarydchuk
- dpo
- rlhf
- bradley-terry
- contrastive-alignment
- capstone
created: '2026-09-04'
updated: '2026-09-04'
provenance:
  source_type: ai
  source_ref: 06_INBOX/RAW_IMPORTS/BOOKS/Eugene-Zvarydchuk-LLM-Apps-Ch8
confidence: high
verification: unverified
relations:
- relation: references
  target: 01_KNOWLEDGE/BOOKS/EXPERT_RAG_Speculative_Decoding_and_Prefix_Caching.md
- relation: references
  target: 01_KNOWLEDGE/BOOKS/Caiet_Teme_Aplicatii_Practice_Carti.md
- relation: references
  target: 00_CORE/GRAPH/07 Knowledge Domains Map.md
---

# LLM Capstone: Alinierea Preferin?elor prin Optimizare Direct? (DPO) & Ra?ionament Contrastiv

**Surs?**: Eugene Zvarydchuk, *Designing LLM Applications* (Capitolul 8) + Rafailov et al. (Direct Preference Optimization)  
**Domeniu**: Aliniere LLM, Teoria Preferin?elor Bradley-Terry & Optimizare Direct? f?r? Reward Model

---

## 1. De la RLHF (PPO) la DPO
?n alinierea clasic? RLHF, se antreneaz? mai ?nt?i un Reward Model $r_\phi(x, y)$ pe perechi de preferin?e $(y_w, y_l)$ (c??tig?tor vs ?nvins), urmat de optimizare prin algoritmul PPO ?mpotriva modelului de recompens? cu o penalizare de divergen?? KL:
$$\max_{\pi_\theta} \mathbb{E}_{(x, y) \sim \mathcal{D}, \pi_\theta} \left[ r_\phi(x, y) \right] - \beta \, \mathbb{D}_{\text{KL}}(\pi_\theta(y \mid x) \parallel \pi_{\text{ref}}(y \mid x))$$
Aceast? procedur? este instabil? numeric, costisitoare ?n memorie ?i predispus? la *reward hacking*.

## 2. Derivarea Matematic? a Func?iei de Pierdere DPO
Rafailov et al. (2023) au demonstrat c? exist? o leg?tur? analitic? direct? ?i exact? ?ntre recompensa optim? $r^*(x, y)$ ?i log-raportul probabilit??ilor:
$$r^*(x, y) = \beta \ln \frac{\pi^*(y \mid x)}{\pi_{\text{ref}}(y \mid x)} + \beta \ln Z(x)$$
Substituind aceast? expresie ?n modelul de preferin?? Bradley-Terry $P(y_w \succ y_l \mid x) = \sigma(r(x, y_w) - r(x, y_l))$, termenul de parti?ie $Z(x)$ se anuleaz? reciproc, permi??nd optimizarea direct? a modelului lingvistic prin func?ia de pierdere DPO:
$$\mathcal{L}_{\text{DPO}}(\pi_\theta; \pi_{\text{ref}}) = - \mathbb{E}_{(x, y_w, y_l) \sim \mathcal{D}} \left[ \ln \sigma \left( \beta \ln \frac{\pi_\theta(y_w \mid x)}{\pi_{\text{ref}}(y_w \mid x)} - \beta \ln \frac{\pi_\theta(y_l \mid x)}{\pi_{\text{ref}}(y_l \mid x)} \right) \right]$$
unde $\beta$ controleaz? devia?ia acceptabil? fa?? de modelul ini?ial de referin?? $\pi_{\text{ref}}$.

## 3. Leg?turi Canonice & Graf de Cuno?tin?e
- [[LLM_Application_Design_and_RAG_Pipelines]]
- [[MASTERY_RAG_Triad_and_Hallucination_Diagnostics]]
- [[EXPERT_RAG_Speculative_Decoding_and_Prefix_Caching]]
- [[Caiet_Teme_Aplicatii_Practice_Carti]]
- [[07 Knowledge Domains Map]]
