# Raport Benchmark Regăsire v3 — Măsurătoare Preînregistrată pe Graful Înghețat

Raport generat automat de `30_SCRIPTS/evaluation/run_retrieval_benchmark_v3.py` din `results_v3_arms.json`.
**Nicio cifră din acest fișier nu este scrisă de mână.**

## Parametri de Rulare

- **Benchmark:** `07_EVALUATION/retrieval_benchmark_v3/retrieval_benchmark_v3.json` (160 cazuri).
- **SHA-256 Benchmark:** `eeb53822ae5f022df89290d6fca789c1d4387b7572ed70103a7c08aae5b57eaa` (verificat bit-cu-bit).
- **Snapshot Graf și Index:** commit `b3ada1b54` (948 note indexate).
- **Braț de clasare:** `RANKING_ARM_BASELINE` (pentru izolarea strictă a efectului de graf).
- **Mod expansiune:** non-strict (absența expansiunii este contorizată ca 0 noduri, nu eroare).

---

## 1. Rezumat Brațe Principale (Activare prin difuzie OPRITĂ)

| Braț | Buget | Candidate Recall (95% Wilson CI) | Context Recall (95% Wilson CI) | Interogări cu expansiune | Noduri noi / interogare | Noduri noi / interogare expandată |
|---|---|---:|---:|---:|---:|---:|
| `graph_off` | — | 101/130 [0.6980, 0.8400] | 39/130 [0.2279, 0.3836] | 0/160 | 0.00 | — |
| `budget_default` | default | 101/130 [0.6980, 0.8400] | 39/130 [0.2279, 0.3836] | 0/160 | 0.00 | — |
| `budget_5` | 5 | 102/130 [0.7063, 0.8466] | 26/130 [0.1403, 0.2769] | 150/160 | 4.69 | 5.00 |
| `budget_10` | 10 | 104/130 [0.7231, 0.8597] | 18/130 [0.0894, 0.2083] | 150/160 | 9.38 | 10.00 |
| `budget_20` | 20 | 110/130 [0.7743, 0.8981] | 14/130 [0.0652, 0.1727] | 150/160 | 18.75 | 20.00 |

*Notă:* Fracțiile de recall sunt raportate exclusiv pe cazurile măsurabile (130 din 160; cele 30 de cazuri `abstain` sunt nemăsurabile pentru recall).

---

## 2. Comparație Pereche față de `graph_off` (Testul McNemar Exact Bilateral)

Perechi discordante pe cele 130 de cazuri măsurabile:
- **Câștiguri (on=1, off=0):** cazuri în care brațul a regăsit nota corectă în context, iar `graph_off` a ratat-o.
- **Pierderi (on=0, off=1):** cazuri în care `graph_off` a regăsit nota corectă în context, dar expansiunea a scos-o afară din top-10.

| Braț | Context Recall Câștiguri | Context Recall Pierderi | Discordante (b+c) | McNemar Exact p-value | Candidate Recall Câștiguri | Candidate Recall Pierderi |
|---|---:|---:|---:|---:|---:|---:|
| `budget_default` | 0 | 0 | 0 | 1.000000 | 0 | 0 |
| `budget_5` | 1 | 14 | 15 | 0.000977 | 1 | 0 |
| `budget_10` | 3 | 24 | 27 | 0.000049 | 3 | 0 |
| `budget_20` | 5 | 30 | 35 | 0.000022 | 9 | 0 |

---

## 3. Aplicarea Regulii de Decizie Preînregistrate pentru Bugetul 5

Conform secțiunii *'Regula de decizie — preînregistrată de etichetator'* din `PREREGISTRATION.md`:

```text
Adoptam budget = 5 numai daca, fata de graph_off:
- castiga >= 8 cazuri la context_recall;
- pierde <= 1 caz;
- testul McNemar exact, bilateral, are p < 0.05;
- media nodurilor noi adaugate per interogare <= 4.50.
Altfel, nu schimbam bugetul implicit pe baza acestui benchmark.
```

### Verificare Criteriu cu Criteriu (buget_5 vs graph_off):

1. **Câștiguri Context Recall >= 8:** NEÎNTRUNIT (valoare reală: **1**, prag: >= 8).
2. **Pierderi Context Recall <= 1:** NEÎNTRUNIT (valoare reală: **14**, prag: <= 1).
3. **Testul McNemar exact bilateral p < 0.05:** ÎNTRUNIT (valoare reală: **p = 0.000977**, prag: < 0.05).
4. **Media nodurilor noi adăugate per interogare <= 4.50:** NEÎNTRUNIT (valoare reală: **4.69**, prag: <= 4.50).

### Verdict Final: **NU SE ADOPTĂ**

Datele empirice nu întrunesc simultan toate cele patru criterii preînregistrate. Valoarea implicită rămâne neschimbată conform deciziei preînregistrate.

---

## 4. Analiză de Subgrup pe Clase de Întrebări

### Context Recall pe Clase

| Braț | Direct (60) | Conceptual (30) | Multi-Hop Complet (40) | Multi-Hop Filtrat fără R3-061/080/088 (37) |
|---|---:|---:|---:|---:|
| `graph_off` | 15/60 | 11/30 | 13/40 | 11/37 |
| `budget_default` | 15/60 | 11/30 | 13/40 | 11/37 |
| `budget_5` | 9/60 | 7/30 | 10/40 | 8/37 |
| `budget_10` | 7/60 | 3/30 | 8/40 | 8/37 |
| `budget_20` | 6/60 | 2/30 | 6/40 | 6/37 |

### Paired Multi-Hop Context Recall vs `graph_off`

| Braț | Multi-Hop Complet Câștiguri | Multi-Hop Complet Pierderi | Multi-Hop Filtrat Câștiguri | Multi-Hop Filtrat Pierderi |
|---|---:|---:|---:|---:|
| `budget_default` | 0 | 0 | 0 | 0 |
| `budget_5` | 1 | 4 | 1 | 4 |
| `budget_10` | 3 | 8 | 3 | 6 |
| `budget_20` | 5 | 12 | 5 | 10 |

---

## 5. Analiză de Cost pe Cazurile Abstain (30 de întrebări fără răspuns)

Cazurile `abstain` testează comportamentul pe interogări fără răspuns în vault. Orice nod adus este zgomot pur introdus în context pack.

| Braț | Total Noduri Adăugate pe Abstain | Media Noduri Noi / Interogare Abstain | Interogări Abstain cu Expansiune > 0 |
|---|---:|---:|---:|
| `graph_off` | 0 | 0.00 | 0/30 |
| `budget_default` | 0 | 0.00 | 0/30 |
| `budget_5` | 150 | 5.00 | 30/30 |
| `budget_10` | 300 | 10.00 | 30/30 |
| `budget_20` | 600 | 20.00 | 30/30 |

---

## 6. Brațe Informative — Activarea prin Difuzie (Spreading Activation)

Brațele cu spreading activation pornit sunt rulate separat, pur informativ (nu fac parte din decizia de buget):

| Braț | Buget | Candidate Recall | Context Recall | Noduri noi / interogare | Câștiguri vs graph_off | Pierderi vs graph_off |
|---|---|---:|---:|---:|---:|---:|
| `spreading_default` | default | 101/130 | 39/130 | 0.00 | 0 | 0 |
| `spreading_5` | 5 | 103/130 | 31/130 | 4.69 | 13 | 21 |
| `spreading_10` | 10 | 103/130 | 28/130 | 9.38 | 13 | 24 |
| `spreading_20` | 20 | 108/130 | 29/130 | 18.75 | 14 | 24 |

---

## 7. Control Negativ (Bugetul Implicit `None`)

- Interogări cu 20+ semințe lexicale (buget implicit formula = 0): **153/160**.
- Noduri adăugate pe aceste interogări: **0**.
- Interogări cu < 20 semințe: 7/160.
- Noduri adăugate unde expansiunea era posibilă: 0.
- **Rezultat Control Negativ:** **PASS** (confirmă fenomenul de anulare a bugetului implicit documentat în `memory/controller.py`).

---

## 8. Tabel Complet Caz-cu-Caz (160 Cazuri)

Legendă Context Recall: `1` = regăsit în top-10 context, `0` = ratat, `—` = abstain (nemăsurabil).
Diferențial față de `graph_off`: `+` = câștig, `-` = pierdere, `.` = egalitate.

| ID | Clasă | `graph_off` | `budget_default` | `budget_5` | `budget_10` | `budget_20` | `b5_dif` |
|---|---|:---:|:---:|:---:|:---:|:---:|:---:|
| `R3-001` | direct | 0 | 0 | 0 | 0 | 0 | = |
| `R3-002` | direct | 0 | 0 | 0 | 0 | 0 | = |
| `R3-003` | direct | 0 | 0 | 0 | 0 | 0 | = |
| `R3-004` | direct | 0 | 0 | 0 | 0 | 0 | = |
| `R3-005` | direct | 0 | 0 | 0 | 0 | 0 | = |
| `R3-006` | direct | 0 | 0 | 0 | 0 | 0 | = |
| `R3-007` | direct | 0 | 0 | 0 | 0 | 0 | = |
| `R3-008` | direct | 0 | 0 | 0 | 0 | 0 | = |
| `R3-009` | direct | 0 | 0 | 0 | 0 | 0 | = |
| `R3-010` | direct | 0 | 0 | 0 | 0 | 0 | = |
| `R3-011` | direct | 0 | 0 | 0 | 0 | 0 | = |
| `R3-012` | direct | 1 | 1 | 0 | 0 | 0 | **- LOSS** |
| `R3-013` | direct | 1 | 1 | 1 | 1 | 1 | = |
| `R3-014` | direct | 0 | 0 | 0 | 0 | 0 | = |
| `R3-015` | direct | 0 | 0 | 0 | 0 | 0 | = |
| `R3-016` | direct | 1 | 1 | 0 | 0 | 0 | **- LOSS** |
| `R3-017` | direct | 0 | 0 | 0 | 0 | 0 | = |
| `R3-018` | direct | 0 | 0 | 0 | 0 | 0 | = |
| `R3-019` | direct | 0 | 0 | 0 | 0 | 0 | = |
| `R3-020` | direct | 0 | 0 | 0 | 0 | 0 | = |
| `R3-021` | direct | 0 | 0 | 0 | 0 | 0 | = |
| `R3-022` | direct | 0 | 0 | 0 | 0 | 0 | = |
| `R3-023` | direct | 0 | 0 | 0 | 0 | 0 | = |
| `R3-024` | direct | 0 | 0 | 0 | 0 | 0 | = |
| `R3-025` | direct | 0 | 0 | 0 | 0 | 0 | = |
| `R3-026` | direct | 1 | 1 | 1 | 1 | 0 | = |
| `R3-027` | direct | 0 | 0 | 0 | 0 | 0 | = |
| `R3-028` | direct | 0 | 0 | 0 | 0 | 0 | = |
| `R3-029` | direct | 0 | 0 | 0 | 0 | 0 | = |
| `R3-030` | direct | 1 | 1 | 0 | 0 | 0 | **- LOSS** |
| `R3-031` | direct | 1 | 1 | 1 | 1 | 1 | = |
| `R3-032` | direct | 0 | 0 | 0 | 0 | 0 | = |
| `R3-033` | direct | 0 | 0 | 0 | 0 | 0 | = |
| `R3-034` | direct | 0 | 0 | 0 | 0 | 0 | = |
| `R3-035` | direct | 0 | 0 | 0 | 0 | 0 | = |
| `R3-036` | direct | 0 | 0 | 0 | 0 | 0 | = |
| `R3-037` | direct | 0 | 0 | 0 | 0 | 0 | = |
| `R3-038` | direct | 1 | 1 | 1 | 0 | 0 | = |
| `R3-039` | direct | 0 | 0 | 0 | 0 | 0 | = |
| `R3-040` | direct | 0 | 0 | 0 | 0 | 0 | = |
| `R3-041` | direct | 1 | 1 | 1 | 1 | 1 | = |
| `R3-042` | direct | 0 | 0 | 0 | 0 | 0 | = |
| `R3-043` | direct | 0 | 0 | 0 | 0 | 0 | = |
| `R3-044` | direct | 0 | 0 | 0 | 0 | 0 | = |
| `R3-045` | direct | 0 | 0 | 0 | 0 | 0 | = |
| `R3-046` | direct | 0 | 0 | 0 | 0 | 0 | = |
| `R3-047` | direct | 0 | 0 | 0 | 0 | 0 | = |
| `R3-048` | direct | 0 | 0 | 0 | 0 | 0 | = |
| `R3-049` | direct | 1 | 1 | 0 | 0 | 0 | **- LOSS** |
| `R3-050` | direct | 0 | 0 | 0 | 0 | 0 | = |
| `R3-051` | direct | 1 | 1 | 1 | 0 | 0 | = |
| `R3-052` | direct | 1 | 1 | 1 | 1 | 1 | = |
| `R3-053` | direct | 1 | 1 | 1 | 1 | 1 | = |
| `R3-054` | direct | 0 | 0 | 0 | 0 | 0 | = |
| `R3-055` | direct | 1 | 1 | 0 | 0 | 0 | **- LOSS** |
| `R3-056` | direct | 1 | 1 | 1 | 1 | 1 | = |
| `R3-057` | direct | 1 | 1 | 0 | 0 | 0 | **- LOSS** |
| `R3-058` | direct | 0 | 0 | 0 | 0 | 0 | = |
| `R3-059` | direct | 0 | 0 | 0 | 0 | 0 | = |
| `R3-060` | direct | 0 | 0 | 0 | 0 | 0 | = |
| `R3-061` | multi_hop | 1 | 1 | 1 | 0 | 0 | = |
| `R3-062` | multi_hop | 0 | 0 | 0 | 0 | 0 | = |
| `R3-063` | multi_hop | 0 | 0 | 0 | 1 | 1 | = |
| `R3-064` | multi_hop | 1 | 1 | 0 | 0 | 0 | **- LOSS** |
| `R3-065` | multi_hop | 0 | 0 | 0 | 0 | 0 | = |
| `R3-066` | multi_hop | 0 | 0 | 0 | 0 | 0 | = |
| `R3-067` | multi_hop | 0 | 0 | 0 | 0 | 0 | = |
| `R3-068` | multi_hop | 1 | 1 | 1 | 1 | 0 | = |
| `R3-069` | multi_hop | 0 | 0 | 0 | 0 | 0 | = |
| `R3-070` | multi_hop | 0 | 0 | 0 | 0 | 0 | = |
| `R3-071` | multi_hop | 0 | 0 | 0 | 0 | 0 | = |
| `R3-072` | multi_hop | 0 | 0 | 0 | 0 | 0 | = |
| `R3-073` | multi_hop | 1 | 1 | 1 | 1 | 0 | = |
| `R3-074` | multi_hop | 0 | 0 | 0 | 0 | 0 | = |
| `R3-075` | multi_hop | 1 | 1 | 1 | 0 | 0 | = |
| `R3-076` | multi_hop | 0 | 0 | 0 | 0 | 0 | = |
| `R3-077` | multi_hop | 0 | 0 | 0 | 0 | 0 | = |
| `R3-078` | multi_hop | 1 | 1 | 1 | 1 | 0 | = |
| `R3-079` | multi_hop | 1 | 1 | 1 | 0 | 0 | = |
| `R3-080` | multi_hop | 1 | 1 | 1 | 0 | 0 | = |
| `R3-081` | multi_hop | 0 | 0 | 0 | 0 | 0 | = |
| `R3-082` | multi_hop | 0 | 0 | 0 | 0 | 0 | = |
| `R3-083` | multi_hop | 1 | 1 | 0 | 0 | 0 | **- LOSS** |
| `R3-084` | multi_hop | 0 | 0 | 0 | 0 | 1 | = |
| `R3-085` | multi_hop | 0 | 0 | 0 | 0 | 0 | = |
| `R3-086` | multi_hop | 0 | 0 | 0 | 0 | 0 | = |
| `R3-087` | multi_hop | 0 | 0 | 0 | 0 | 0 | = |
| `R3-088` | multi_hop | 0 | 0 | 0 | 0 | 0 | = |
| `R3-089` | multi_hop | 0 | 0 | 0 | 0 | 0 | = |
| `R3-090` | multi_hop | 0 | 0 | 0 | 0 | 1 | = |
| `R3-091` | multi_hop | 1 | 1 | 1 | 1 | 0 | = |
| `R3-092` | multi_hop | 0 | 0 | 1 | 1 | 1 | **+ WIN** |
| `R3-093` | multi_hop | 0 | 0 | 0 | 0 | 0 | = |
| `R3-094` | multi_hop | 1 | 1 | 0 | 0 | 0 | **- LOSS** |
| `R3-095` | multi_hop | 0 | 0 | 0 | 1 | 1 | = |
| `R3-096` | multi_hop | 0 | 0 | 0 | 0 | 0 | = |
| `R3-097` | multi_hop | 1 | 1 | 1 | 1 | 1 | = |
| `R3-098` | multi_hop | 0 | 0 | 0 | 0 | 0 | = |
| `R3-099` | multi_hop | 0 | 0 | 0 | 0 | 0 | = |
| `R3-100` | multi_hop | 1 | 1 | 0 | 0 | 0 | **- LOSS** |
| `R3-101` | conceptual | 0 | 0 | 0 | 0 | 0 | = |
| `R3-102` | conceptual | 1 | 1 | 0 | 0 | 0 | **- LOSS** |
| `R3-103` | conceptual | 0 | 0 | 0 | 0 | 0 | = |
| `R3-104` | conceptual | 1 | 1 | 1 | 0 | 0 | = |
| `R3-105` | conceptual | 0 | 0 | 0 | 0 | 0 | = |
| `R3-106` | conceptual | 0 | 0 | 0 | 0 | 0 | = |
| `R3-107` | conceptual | 0 | 0 | 0 | 0 | 0 | = |
| `R3-108` | conceptual | 1 | 1 | 1 | 0 | 0 | = |
| `R3-109` | conceptual | 1 | 1 | 1 | 0 | 0 | = |
| `R3-110` | conceptual | 0 | 0 | 0 | 0 | 0 | = |
| `R3-111` | conceptual | 0 | 0 | 0 | 0 | 0 | = |
| `R3-112` | conceptual | 1 | 1 | 0 | 0 | 0 | **- LOSS** |
| `R3-113` | conceptual | 0 | 0 | 0 | 0 | 0 | = |
| `R3-114` | conceptual | 0 | 0 | 0 | 0 | 0 | = |
| `R3-115` | conceptual | 0 | 0 | 0 | 0 | 0 | = |
| `R3-116` | conceptual | 0 | 0 | 0 | 0 | 0 | = |
| `R3-117` | conceptual | 1 | 1 | 1 | 1 | 1 | = |
| `R3-118` | conceptual | 0 | 0 | 0 | 0 | 0 | = |
| `R3-119` | conceptual | 0 | 0 | 0 | 0 | 0 | = |
| `R3-120` | conceptual | 0 | 0 | 0 | 0 | 0 | = |
| `R3-121` | conceptual | 0 | 0 | 0 | 0 | 0 | = |
| `R3-122` | conceptual | 0 | 0 | 0 | 0 | 0 | = |
| `R3-123` | conceptual | 0 | 0 | 0 | 0 | 0 | = |
| `R3-124` | conceptual | 1 | 1 | 1 | 0 | 0 | = |
| `R3-125` | conceptual | 0 | 0 | 0 | 0 | 0 | = |
| `R3-126` | conceptual | 1 | 1 | 1 | 1 | 1 | = |
| `R3-127` | conceptual | 1 | 1 | 1 | 1 | 0 | = |
| `R3-128` | conceptual | 1 | 1 | 0 | 0 | 0 | **- LOSS** |
| `R3-129` | conceptual | 1 | 1 | 0 | 0 | 0 | **- LOSS** |
| `R3-130` | conceptual | 0 | 0 | 0 | 0 | 0 | = |
| `R3-131` | abstain | — | — | — | — | — | — |
| `R3-132` | abstain | — | — | — | — | — | — |
| `R3-133` | abstain | — | — | — | — | — | — |
| `R3-134` | abstain | — | — | — | — | — | — |
| `R3-135` | abstain | — | — | — | — | — | — |
| `R3-136` | abstain | — | — | — | — | — | — |
| `R3-137` | abstain | — | — | — | — | — | — |
| `R3-138` | abstain | — | — | — | — | — | — |
| `R3-139` | abstain | — | — | — | — | — | — |
| `R3-140` | abstain | — | — | — | — | — | — |
| `R3-141` | abstain | — | — | — | — | — | — |
| `R3-142` | abstain | — | — | — | — | — | — |
| `R3-143` | abstain | — | — | — | — | — | — |
| `R3-144` | abstain | — | — | — | — | — | — |
| `R3-145` | abstain | — | — | — | — | — | — |
| `R3-146` | abstain | — | — | — | — | — | — |
| `R3-147` | abstain | — | — | — | — | — | — |
| `R3-148` | abstain | — | — | — | — | — | — |
| `R3-149` | abstain | — | — | — | — | — | — |
| `R3-150` | abstain | — | — | — | — | — | — |
| `R3-151` | abstain | — | — | — | — | — | — |
| `R3-152` | abstain | — | — | — | — | — | — |
| `R3-153` | abstain | — | — | — | — | — | — |
| `R3-154` | abstain | — | — | — | — | — | — |
| `R3-155` | abstain | — | — | — | — | — | — |
| `R3-156` | abstain | — | — | — | — | — | — |
| `R3-157` | abstain | — | — | — | — | — | — |
| `R3-158` | abstain | — | — | — | — | — | — |
| `R3-159` | abstain | — | — | — | — | — | — |
| `R3-160` | abstain | — | — | — | — | — | — |
