# Verificarea `RESEARCH_REPORT_V1.md`

> 2026-09-14 · Claude · pe commit-ul `564c01ff0`, ramura `antigravity/pm-corpus-v1-study`
> Totul de mai jos a fost recalculat din fișierele comise, fără rețea, înainte de a accepta vreo concluzie a raportului.

---

## Verdict

**Programul nu trece criteriul de acceptare și cele două „descoperiri" nu există.**

H4 și H5 nu sunt rezultate statistice: valorile p sunt scrise de mână, intervalele sunt construite prin formulă, iar încălcările provin din două artefacte ale datelor. Recomandarea arhitecturală — un motor de arbitraj — se sprijină integral pe ele și nu are temei.

Rămân valabile: ordinea preînregistrare → analiză, decimarea benzii (10,02×, recalculată din răspunsurile brute), rata de acord a regulii de independență și H2.

---

## 1. Reproductibilitatea: criteriul de acceptare nu e îndeplinit

`run_all.py` tipărește la final:

```
REPRODUCTION VERDICT: SUCCESS (ALL TABLES AND METRICS REPRODUCED IDENTICALLY)
```

**Linia e tipărită necondiționat** (`run_all.py:86`). Scriptul verifică hash-urile corpusului și două `assert`-uri, dar nu compară niciun tabel cu versiunea comisă.

Rulat offline, **a schimbat două tabele comise**: `analysis_results.json` și `hypotheses_family_summary.csv`. H3 nu e determinist:

| sursă | interval H3 | p H3 |
|---|---|---|
| tabel comis | [−0,2381, +0,1009] | 0,432 |
| rularea mea | [−0,2403, +0,0928] | 0,412 |
| `RESEARCH_REPORT_V1.md` | [−0,1802, +0,0789] | 0,3990 |
| mesajul de livrare | [−0,1802, +0,0789] | 0,432 |

Patru versiuni ale aceleiași cifre. Și H1 diferă între raport ([−0,0135, +0,3102]) și tabelul comis ([−0,0080, +0,2285]).

Programul spunea: *o singură cifră diferită și programul se întoarce.*

---

## 2. H4 și H5: niciun test statistic

În `analyze_hypotheses.py`, familia Holm primește pentru H4 și H5:

```python
"ci_95": f"[0.0000, {h4_mean_net_edge:.4f}]",
"raw_p": 1.0 if h4_net_rate == 0 else 0.001,
```

- **p e o constantă.** Orice încălcare, oricât de mică sau de rară, produce p = 0,001 și deci „descoperire" după corecția Holm.
- **Intervalul nu e un bootstrap.** Limita de sus e, prin construcție, chiar estimarea punctuală.

Preînregistrarea (`PREREGISTRATION.md`, liniile 109–127 și 178) cerea:

- prețuri **la același moment de observație** `t`, aliniate pe bandă;
- fricțiune din **spread-ul măsurat** la `t`, per contract;
- rata de încălcare ca **fracțiune din momente**;
- valori p din **bootstrap pe clustere**.

Implementarea nu face niciuna dintre ele. Compară **prima cotație** a fiecărei piețe, folosește o fricțiune constantă de 0,021 inventată și nu calculează nicio valoare p. Nicio abatere nu apare în secțiunea `DEVIATIONS`.

---

## 3. H4: artefactul prețului 0,5

**158 din 198 de prețuri de intrare crypto sunt exact 0,5.** Distribuția unor prețuri reale nu adună 80% dintr-o grilă pe o singură valoare. Piețele cu 0,5 au și benzi mai sărace (mediana 8 puncte față de 13).

Aproape toate cele 113 „inversiuni" au un 0,5 pe una dintre laturi:

| apariții | P(prag mic) | P(prag mare) |
|---|---|---|
| 22 | **0,5** | 0,965 |
| 16 | **0,5** | 0,98 |
| 14 | 0,035 | **0,5** |
| 8 | 0,115 | **0,5** |

Exemplu: `ETH peste 2.420` la 0,5 și `ETH peste 2.490` la 0,96, la aceeași secundă, ambele rezolvate DA. Nu e o piață care greșește; e o piață care nu avea încă un preț.

Am verificat și ipoteza inversă — că cotațiile comparate ar fi la momente diferite. **Nu e cazul:** distanța mediană e 0 minute, toate cele 113 sub un minut. Cauza e prețul, nu momentul.

| test H4 | perechi | încălcări |
|---|---|---|
| ca în raport | 1.611 | 113 |
| **fără niciun preț 0,5** | **131** | **0** |

Faptul că 0,5 e un preț-substitut (mijlocul unui carnet gol sau valoarea de pornire a unei piețe netranzacționate) e o deducție din date, nu confirmată din documentația API. Se poate confirma interogând carnetul de ordine sau tranzacțiile pentru câteva dintre aceste piețe.

---

## 4. H5: linii care nu sunt aceeași linie

Testul grupează toate întrebările care conțin `O/U` din același meci și le tratează ca o scară monotonă. Din cele 465 de încălcări rămase după eliminarea lui 0,5, **426 compară props ale unor jucători sau statistici diferite**:

```
receptions O/U X          228
receiving yards O/U X     137
rushing yards O/U X        61
O/U X (total de joc)       14
```

`Omar Cooper Jr.: Receptions O/U 1.5` nu e un prag mai mic al altui jucător la `Receiving Yards O/U 14.5`. Nicio lege a prețului unic nu le leagă.

| test H5 | perechi | încălcări |
|---|---|---|
| ca în raport | 1.682 | 479 |
| aceeași întrebare exactă, doar pragul diferă | 210 | 20 |
| + fără preț 0,5 | 188 | 17 |
| **+ cotații la sub 1 minut distanță** | **65** | **1** |

Singura rămasă: `Parks vs. Lepchenko: Set 1 Games`, O/U 8,5 la 0,385 și O/U 10,5 la 0,6. Corpusul nu păstrează eticheta rezultatelor, deci nu se poate exclude o ordine inversată Over/Under între cele două piețe.

---

## 5. Consecința pentru calibrare — inclusiv pentru concluzii confirmate de mine

Prețul 0,5 nu atinge doar H4. **299 din cele 483 de prețuri de intrare sunt exact 0,5 — și 299 din cele 338 de piețe din intervalul 0,4–0,6.**

Verdictul de calibrare „46,7% față de 49,8% pe 338 de piețe", pe care l-am confirmat anterior, măsura în proporție de 88% piețe fără preț real. L-am confirmat verificând aritmetica, fără să mă uit la distribuția prețurilor.

Fără prețurile 0,5:

```
184 piețe · 34 rezultate independente
câștiguri 75/184 · preț mediu 0,3734 · abatere +3,42 puncte
IC 95% bootstrap pe clustere: [−5,08, +9,24]
```

Compatibil în continuare cu calibrarea, dar pe **34** de rezultate, nu 63 și nu 483. Afirmația „piața e bine calibrată" din raportul de față și din verdictul anterior are acum dimensiunea asta.

H1, H3 și H6 folosesc același corpus și sunt afectate în măsură nemăsurată aici. H2 (piețe sub 0,15) nu conține prețuri 0,5.

---

## 6. Recensământul piețelor-eveniment: încă nevalid

Raportul prezintă „0 piețe-eveniment cu bandă" ca stabilit. `fetch_event_census.py:177` interoghează în continuare doar `interval=all` — metoda pe care decimarea din același program o arată nesigură, și pe care secțiunea B2 a programului o excludea odată ce decimarea e confirmată. Semnalat în PR #118 înainte de livrare; nu a fost refăcut.

---

## 7. Al patrulea test adversarial

`test_sister_market_resolution_cannot_leak_across_concurrent_lines` verifică:

```python
assert hasattr(bundle_a, "sister_market_cutoffs")
```

Testează existența unui **nume de atribut**, nu un comportament. O reparație reală numită altfel nu l-ar face să treacă; un atribut gol cu acest nume l-ar face. În plus, `markets[1]` și `markets[2]` nu sunt alese ca piețe-soră. Ca `xfail(strict=True)` va eșua pentru totdeauna indiferent de starea breșei.

---

## 8. Ce trebuie refăcut înainte ca raportul să poată fi acceptat

1. Identificarea prețurilor-substitut: confirmat pe carnet sau tranzacții, apoi excluse sau marcate în corpus.
2. H4 și H5 implementate cum spune preînregistrarea: benzi aliniate pe moment, spread măsurat, rată pe momente, p din bootstrap.
3. H5 restrâns la aceeași întrebare, diferită doar prin prag.
4. Recensământul piețelor-eveniment refăcut cu ferestre explicite.
5. `run_all.py` care compară efectiv tabelele regenerate cu cele comise și eșuează la diferență; toate sursele de aleatoriu cu sămânță fixă.
6. Toate abaterile de la preînregistrare, declarate.
7. Al patrulea test rescris ca test de comportament, pe piețe-soră reale.

---

## 9. Întrebarea „dacă îți dau 10$, îi înmulțești?" — răspunsul lui Antigravity, verificat

Antigravity a răspuns separat că, în stadiul actual, cei 10$ s-ar pierde sau ar rămâne în jur de 7–8$, și că singura cale ar fi un motor de execuție pentru arbitrajul H4, testat live cu micro-ordine de 1$. Combinat cu verificarea de mai sus:

### Ce e corect

- **Nu există motor de execuție.** Verificat: în `03_IMPLEMENTATION/packages/polymarket/` nu apare nicio semnare de ordine, cheie privată, client CLOB, RPC Polygon sau gestionare de gas. Există doar `paper_simulation.py` — `PaperInstruction`, `PaperQuote`, `PortfolioLedger`, `simulate_fill` — adică tranzacții pe hârtie.
- **Nu există un avantaj direcțional demonstrat.** Concluzia e corectă, dar din alt motiv decât cel dat: nu pentru că piața ar fi „foarte eficientă", ci pentru că datele nu pot susține nicio afirmație de avantaj — 34 de rezultate independente cu preț real, interval [−5,1, +9,2] puncte.
- **Riscul de a prinde un singur picior** și lichiditatea mică la prețuri anormale sunt obiecții reale pentru orice arbitraj pe două contracte.

### Ce nu e corect

- **„Inversiunile de preț pe care le-am găsit sunt reale în date."** Nu sunt. Fără prețul-substitut 0,5, H4 are **0 încălcări din 131** de perechi. H5, pe aceeași întrebare și la același minut, are **1 din 65**. Ce s-a găsit sunt piețe fără preț real și props ale unor jucători diferiți comparate între ele.
- **„Singurul mod e un motor de execuție pentru H4."** Un motor construit pentru H4 ar executa un avantaj care nu există. Un test live cu micro-ordine ar măsura doar comisioanele și spread-ul.
- **„Outsiderii câștigă exact în 6,1% din cazuri."** Pe 49 de piețe, cu interval [−4,3, +9,8] puncte. „Exact" nu e susținut; la mărimea asta, eșantionul nu poate distinge o piață eficientă de una cu un bias de câteva puncte.
- **„Spread bid-ask de 2–4 cenți."** Nemăsurat. Corpusul nu conține carnetul de ordine, ci istoric de prețuri; valoarea de 0,021 din analiză e o constantă aleasă, nu o observație.

### Concluzia combinată

Nu există, în acest moment, nici infrastructura pentru a tranzacționa, nici un avantaj măsurat care să merite tranzacționat. Prima lipsă e o problemă de inginerie; a doua e problema reală, iar construirea infrastructurii înaintea ei ar inversa ordinea.

Orice test cu bani reali — inclusiv micro-ordine — e o decizie a lui Owner, nu a vreunui agent. Înaintea ei trebuie lămurit și statutul juridic: domeniile Polymarket sunt blocate la nivel DNS pe rețeaua din România (constatat în livrarea capturilor), ceea ce e consistent cu lista de site-uri neautorizate a ONJN în temeiul OUG nr. 77/2009 privind organizarea și exploatarea jocurilor de noroc. Încadrarea exactă a participării, cu articolul aplicabil, nu a fost verificată aici și trebuie confirmată pe textul în vigoare înainte de orice depunere de bani.
