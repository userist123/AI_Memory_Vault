# Raport Experimental: Normalizarea Tokenizatorului (EXP-TOKEN-001)

> **Raport Experimental Formal — Programul de Măsurare (Partea 3, PR 1)**  
> **Depozit**: `userist123/AI_Memory_Vault_CODEX_READY`  
> **Hash Benchmark Înghețat (SHA-256)**: `eeb53822ae5f022df89290d6fca789c1d4387b7572ed70103a7c08aae5b57eaa`  
> **Cazuri măsurabile**: 130 din 160 (61 română, 69 engleză)  
> **Braț de clasare**: `fused_score`  

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
| **Brațul 1 (Linie de Referință — Baseline)** | 24 / 130 | **18.46%** | [12.73%, 26.00%] | 5 / 61 | **8.20%** | 19 / 69 | **27.54%** | 41 / 61 |
| **Brațul 2 (Litere Unicode — Unicode Preserving)** | 24 / 130 | **18.46%** | [12.73%, 26.00%] | 5 / 61 | **8.20%** | 19 / 69 | **27.54%** | 41 / 61 |
| **Brațul 3 (Foldare Diacritice — Diacritics Stripping)** | 25 / 130 | **19.23%** | [13.38%, 26.85%] | 6 / 61 | **9.84%** | 19 / 69 | **27.54%** | 42 / 61 |

> [!IMPORTANT]
> **Brațele diferă.** Diferențele, pe cazuri, sunt în tabelul 2.

### Control negativ — tokenizator gol

Cu `tokenize()` care întoarce `[]` pentru orice text, s-au obținut 1 reușite, iar 94 cazuri și-au schimbat rezultatul sau rangul notei de aur față de brațul 1. **Control TRECUT.** Patch-ul ajunge în calea de căutare, deci brațele sunt reale.

Module în care a fost înlocuit `tokenize`: `memory_controller.context.candidate_generation`, `memory_controller.hybrid_retrieval`, `retrieval.context.candidate_generation`, `retrieval.hybrid_retrieval`.

---

## 3. Tabelul 2 — Analiza Pereche și Testul Exact McNemar
### [Punct de operare: Principal.AI_AGENT, page_size=5, braț de clasare `fused_score`]

| Comparație vs Baseline | Felie | Câștiguri ($b$) | Pierderi ($c$) | Cazuri Discordante | $\Delta$ Cazuri | $\Delta$ Procentual (pp) | $p$ McNemar Exact | Semnificație la 0.05 |
|:---|:---:|:---:|:---:|:---:|:---:|:---:|:---:|:---|
| Brațul 2 (Unicode) | `Română (N=61)` | 0 | 0 | 0 | +0 | +0.00 pp | `1.000000` | fără cazuri discordante |
| Brațul 2 (Unicode) | `Engleză (N=69)` | 0 | 0 | 0 | +0 | +0.00 pp | `1.000000` | fără cazuri discordante |
| Brațul 2 (Unicode) | `Total (N=130)` | 0 | 0 | 0 | +0 | +0.00 pp | `1.000000` | fără cazuri discordante |
| Brațul 3 (Stripped) | `Română (N=61)` | 1 | 0 | 1 | +1 | +1.64 pp | `1.000000` | nesemnificativ |
| Brațul 3 (Stripped) | `Engleză (N=69)` | 0 | 0 | 0 | +0 | +0.00 pp | `1.000000` | fără cazuri discordante |
| Brațul 3 (Stripped) | `Total (N=130)` | 1 | 0 | 1 | +1 | +0.77 pp | `1.000000` | nesemnificativ |

---

## 4. Tabelul 3 — Evaluarea Ipotezelor Preînregistrate

Regula se aplică în cazuri, nu în puncte procentuale: preînregistrarea dă pentru română atât „+5.00 pp” cât și „≥ 3 cazuri nete”, iar pe 61 de cazuri trei cazuri înseamnă 4.92 pp. Numărul de cazuri este lectura neambiguă.

| Criteriu | Condiție | Brațul 2 (Unicode) | Brațul 3 (Stripped) |
|:---|:---|:---:|:---:|
| H-TOKEN-1, câștig pe română | $\ge 3$ cazuri nete | +0 cazuri (+0.00 pp) — nu | +1 cazuri (+1.64 pp) — nu |
| Non-regresie pe engleză | $\ge -1$ caz, iar la pierdere $p > 0.10$ | +0 cazuri (+0.00 pp) — da | +0 cazuri (+0.00 pp) — da |
| Câștig net total | $\ge 2$ cazuri nete | +0 cazuri (+0.00 pp) — nu | +1 cazuri (+0.77 pp) — nu |

H-TOKEN-1: **INFIRMATĂ**. Non-regresie pe engleză: **CONFIRMATĂ**. Câștig net total: **INFIRMATĂ**.

---

## 5. Decizia

**MENȚINERE BASELINE (RESPINGERE ADOPTARE TOKENIZATOR NOU)**

Niciun braț nu îndeplinește simultan cele trei condiții preînregistrate; tokenizatorul de producție rămâne cum este.

---
*Fiecare cifră și fiecare verdict din acest raport sunt citite din `07_EVALUATION/tokenizer_experiment/tokenizer_experiment_cases.json`.*
