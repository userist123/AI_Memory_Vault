# Raport Experimental: Normalizarea Tokenizatorului (EXP-TOKEN-001)

> **Raport Experimental Formal — Programul de Măsurare (Partea 3, PR 1)**  
> **Depozit**: `userist123/AI_Memory_Vault_CODEX_READY`  
> **Hash Benchmark Înghețat (SHA-256)**: `eeb53822ae5f022df89290d6fca789c1d4387b7572ed70103a7c08aae5b57eaa`  
> **Cazuri măsurabile**: 130 din 160 (61 română, 69 engleză)  
> **Stare**: FINALIZAT — EVALUAT CONFORM PREÎNREGISTRĂRII  

---

## 1. Punctul de Operare și Scopul Măsurătorii

Acest experiment a fost proiectat pentru a testa dacă înlocuirea tokenizatorului ASCII curent (`TOKEN_RE = [a-z0-9][a-z0-9_\-\.]*`) cu o variantă compatibilă Unicode sau cu eliminare simetrică a diacriticelor aduce o îmbunătățire măsurabilă pe felia românească a benchmark-ului.

Toate măsurătorile din acest raport folosesc strict punctul de operare de producție:
- **Principal**: `Principal.AI_AGENT`
- **Page Size**: `page_size = 5`
- **Prag Ciclu de Viață**: ACTIV (`Floor: ACTIV`, note din afara `{ACTIVE}` excluse)

---

## 2. Tabelul 1 — Rezultatele Primare ale Celor 3 Brațe Experimentale
### [Punct de operare: Principal.AI_AGENT, page_size=5, Floor: ACTIV]

| Braț Experimental | Total (N=130) | Recall Total (%) | Interval Wilson 95% | Română (N=61) | Recall RO (%) | Engleză (N=69) | Recall EN (%) | Top 200 RO |
|:---|:---:|:---:|:---:|:---:|:---:|:---:|:---:|:---:|
| **Brațul 1 (Linie de Referință — Baseline)** | 21 / 130 | **16.15%** | [10.82%, 23.44%] | 9 / 61 | **14.75%** | 12 / 69 | **17.39%** | 46 / 61 |
| **Brațul 2 (Litere Unicode — Unicode Preserving)** | 21 / 130 | **16.15%** | [10.82%, 23.44%] | 9 / 61 | **14.75%** | 12 / 69 | **17.39%** | 46 / 61 |
| **Brațul 3 (Foldare Diacritice — Diacritics Stripping)** | 21 / 130 | **16.15%** | [10.82%, 23.44%] | 9 / 61 | **14.75%** | 12 / 69 | **17.39%** | 46 / 61 |

> [!IMPORTANT]
> **Constatare Empirică Directă**: Toate cele 3 brațe obțin **exact același număr de reușite (21 / 130, 16.15%)**, cu exact aceleași 9 reușite pe română (14.75%) și 12 reușite pe engleză (17.39%).

---

## 3. Tabelul 2 — Analiza Pereche și Testul Exact McNemar
### [Punct de operare comparat: Principal.AI_AGENT, page_size=5, Floor: ACTIV]

| Comparație vs Baseline | Felie | Câștiguri ($b$) | Pierderi ($c$) | Cazuri Discordante | $\Delta$ Cazuri | $\Delta$ Procentual (pp) | $p$ McNemar Exact | Semnificație |
|:---|:---:|:---:|:---:|:---:|:---:|:---:|:---:|:---|
| Brațul 2 (Unicode) | `Română (N=61)` | 0 | 0 | 0 | +0 | +0.00 pp | `1.000000` | Identic ($p = 1.0$) |
| Brațul 2 (Unicode) | `Engleză (N=69)` | 0 | 0 | 0 | +0 | +0.00 pp | `1.000000` | Identic ($p = 1.0$) |
| Brațul 2 (Unicode) | `Total (N=130)` | 0 | 0 | 0 | +0 | +0.00 pp | `1.000000` | Identic ($p = 1.0$) |
| Brațul 3 (Stripped) | `Română (N=61)` | 0 | 0 | 0 | +0 | +0.00 pp | `1.000000` | Identic ($p = 1.0$) |
| Brațul 3 (Stripped) | `Engleză (N=69)` | 0 | 0 | 0 | +0 | +0.00 pp | `1.000000` | Identic ($p = 1.0$) |
| Brațul 3 (Stripped) | `Total (N=130)` | 0 | 0 | 0 | +0 | +0.00 pp | `1.000000` | Identic ($p = 1.0$) |

---

## 4. Tabelul 3 — Evaluarea Formală a Ipotezelor Preînregistrate
### [Punct de operare: Principal.AI_AGENT, page_size=5, Floor: ACTIV]

| Criteriu / Ipoteză Preînregistrată | Condiție Formală | Măsurat Brațul 2 (Unicode) | Măsurat Brațul 3 (Stripped) | Verdict |
|:---|:---|:---:|:---:|:---:|
| **H-TOKEN-1 (Câștig Română)** | $\Delta_{\text{RO}} \ge +5.00$ pp ($\ge 3$ cazuri) | **+0.00 pp** (0 cazuri) | **+0.00 pp** (0 cazuri) | **INFIRMATĂ** |
| **Non-Regresie Engleză** | $\Delta_{\text{EN}} \ge -1.45$ pp ($p > 0.10$) | **+0.00 pp** (0 pierderi) | **+0.00 pp** (0 pierderi) | **CONFIRMATĂ (Fără regresie)** |
| **Câștig Net Total** | $\Delta_{\text{Total}} \ge +1.54$ pp ($\ge 2$ cazuri) | **+0.00 pp** (0 cazuri) | **+0.00 pp** (0 cazuri) | **INFIRMATĂ** |

---

## 5. Tabelul 4 — Aplicarea Regulii Decizionale Preînregistrate
### [Punct de operare: Principal.AI_AGENT, page_size=5, Floor: ACTIV]

| Criteriu Decizional Preînregistrat | Condiție Formală | Valoare Măsurată | Verdict Decizional |
|:---|:---|:---:|:---|
| **Adoptare Braț Nou** | $\Delta_{\text{RO}} \ge +5.00$ pp **ȘI** $\Delta_{\text{EN}} \ge -1.45$ pp **ȘI** $\Delta_{\text{Total}} \ge +1.54$ pp | $\Delta_{\text{RO}} = +0.00$ pp, $\Delta_{\text{EN}} = +0.00$ pp, $\Delta_{\text{Total}} = +0.00$ pp | **MENȚINERE BASELINE (RESPINGERE ADOPTARE TOKENIZATOR NOU)** |
| **Menținere Baseline** | Niciun braț nu întrunește condiția pe Română ($\Delta_{\text{RO}} < +5.00$ pp) | Niciun braț nu a produs cazuri noi câștigate | **APLICAT (Baseline Menținut)** |

> [!NOTE]
> **Explicația Mecanică a Rezultatului**:  
> 1. **Zero cazuri discordante**: Niciun caz din cele 130 nu a trecut de la eșec la succes și niciunul de la succes la eșec ($b=0, c=0$). Setul celor 21 de reușite este identic între toate cele 3 brațe.  
> 2. **Plafonul candidaților pe limba română**: Numărul de note de aur ajunse în top 200 de candidați este identic (46 / 61 = 75.41%).  
> 3. **Confirmarea diagnosticului din Partea 2**: Blocajul primar al sistemului este **clasarea / paginarea (`PAGINATION_CUT`)**, nu segmentarea lexicală. Modificarea tokenizatorului reordonează marginal unii candidați din intervalul 20–100, dar nu ridică notele românești dincolo de pragul paginii ($k=5$) fără un model de reranking.

---

## 6. Concluzie și Recomandare Tehnică

Conform contractului din preînregistrare:
1. **Tokenizatorul de producție din `hybrid_retrieval.py` rămâne neschimbat pe `main`**.
2. Se evită riscul de churn de cod, invalidare a indecșilor și complexitate de mentenanță fără beneficiu empiric.
3. Efortul de optimizare a regăsirii trebuie concentrat exclusiv pe construirea și dimensionarea modelului de reordonare (**Cross-Encoder Reranker**), conform verdictului din Partea 2 (PR 3).

---
*Raport generat determinist din `07_EVALUATION/tokenizer_experiment/tokenizer_experiment_cases.json` conform preînregistrării.*
