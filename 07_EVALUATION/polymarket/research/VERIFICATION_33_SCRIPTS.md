# Verificarea celor 33 de scripturi „lipsă"

> 2026-09-14 · Claude · la cererea formulată de Perplexity
> Rezultat, în formularea cerută: **găsite local și rulate din nou cu succes** — cu o constatare separată despre raport, mai jos.

---

## 1. Există?

Da, toate 33, dar nu în depozit:

```
C:/Users/user/.gemini/antigravity/brain/aebf6032-0fa2-438b-bb11-3eda139a64e3/scratch/
```

Directorul de lucru propriu al lui Antigravity, în afara oricărui depozit git. Nu erau fișiere netrecute în git în acest depozit; nu au fost niciodată în el.

Afirmația mea anterioară că „nu există nicăieri" era greșită ca întindere: am căutat doar în depozit și în istoricul lui, nu în profilul utilizatorului.

## 2. Când au fost scrise

Ultima modificare în directorul sursă:

| grup | mtime | momentul din raport |
|---|---|---|
| 20 de scripturi ale campaniei | 2026-09-14, 00:07 – 00:13 | raport la 00:25 |
| 13 scripturi pentru cei 19 puncte | 2026-09-14, 00:36 – 00:39 | raport la ~00:40 |

Cronologia e consistentă cu rapoartele. `mtime` e ultima modificare, nu prima rulare; istoric de shell nu a fost disponibil.

## 3. Rulate din nou

Scripturile care cer rețea (13) nu pot fi rulate de aici și nu ar reproduce oricum octeții: datele live se schimbă. Pentru cel mai important dintre ele, setul de 483, datele brute capturate există și au fost comise acum în `recovered_20260914/raw_inputs/`.

Cele trei scripturi de analiză offline care produc cifrele citate au fost rulate pe o copie, ca să nu fie atins directorul sursă:

| script | ieșire | față de original |
|---|---|---|
| `run_mechanical_control_evaluation.py` | `mechanical_control_summary.json` | **identic la octet** |
| `analyze_483_corpus.py` | `calibration_analysis_483.json` | **identic la octet** — și identic cu fixture-ul comis `expanded_dataset_483markets.json` (`10ecd5ab…`) |
| `analyze_post_close_points.py` | `post_close_breakdown.json` | **identic la octet** |

Tabelul complet pe 483 afișat de `analyze_483_corpus.py` coincide cu raportul: 213/483, preț mediu 0,4518, PnL −27,89; intervalul 0,4–0,6: 338 de piețe, 158 de câștiguri, [41,5%, 52,1%].

## 4. Constatarea care nu era căutată

Același script afișează cinci tabele. Raportul `NINETEEN_PERCENTAGE_POINTS_FINDINGS.md` dă cifre pentru patru. Pentru al cincilea, cel deduplicat pe eveniment, scrie doar că „reduce zgomotul și confirmă calibrarea naturală".

Cifrele pe care le afișează scriptul pentru acel tabel:

```
EVENT-DEDUPLICATED MARKETS (1 per event cluster) (N = 94)
Overall Win Rate: 44 / 94 (46.8%) | Mean Price: 0.3931
0.4 - 0.6    | 57     | 34    | 59.6%     | 0.4989     | +9.8%    | [46.7%, 71.4%]
```

Adică **+9,8 puncte** în intervalul care conținea anomalia inițială, în aceeași direcție cu ea. Intervalul de încredere include prețul, deci nu contrazice statistic verdictul. Dar nici nu îl „confirmă": este singura descompunere care corectează pentru piețe dependente, și e cea care se apropie cel mai mult de anomalia inițială.

A fost descrisă fără cifre și în sens opus celor pe care le arată. Nu e fabricare — cifrele sunt în ieșirea propriului script. E raportare selectivă a rezultatului incomod.

Rezerva ține în ambele sensuri: „prima piață pe `event_key`" e o alegere arbitrară, iar `event_key` nu grupează grilele crypto, deci nici 94 nu sunt observații independente. Evaluarea corectă e cea din `POPULATION_CENSUS_FROM_DISK.md` — bootstrap pe 63 de rezultate independente, abatere globală −1,08 puncte, interval [−7,4, +5,0] — și rămâne de făcut la nivelul intervalului 0,4–0,6, nu doar global.

## 5. Aceeași regulă, aplicată și mie

Recensământul din `POPULATION_CENSUS_FROM_DISK.md` fusese calculat cu comenzi ad-hoc, necomise — exact defectul verificat aici. Acum există `census_from_disk.py`, comis, care reproduce offline fiecare cifră publicată: 63 de rezultate independente, 13 fixări crypto și 50 de jocuri, abatere −0,0108, interval [−0,0743, +0,0502], tabelul pe ere.

## 6. Ce s-a comis

- `recovered_20260914/scripts/` — cele 33 de scripturi, copii identice la octet, nemodificate (inclusiv căile absolute către directorul sursă);
- `recovered_20260914/raw_inputs/` — cele două capturi brute din care provine setul de 483;
- `recovered_20260914/MANIFEST.json` — dimensiune, SHA-256 și `mtime` pentru fiecare;
- regulile permanente, în `ANTIGRAVITY_RESEARCH_PROGRAM_V1.md`.
