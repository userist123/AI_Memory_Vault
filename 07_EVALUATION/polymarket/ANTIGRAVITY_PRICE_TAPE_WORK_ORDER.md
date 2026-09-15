# WORK ORDER — ANTIGRAVITY: banda de prețuri, și cele două câmpuri umplute cu „acum"

> Destinatar: ANTIGRAVITY (ai rețea; eu nu)
> Emis: 2026-09-13 · Precedent: capturile reale, PR #117 — livrarea în care fiecare cifră s-a verificat
> Rezultat așteptat: **o captură de istoric de prețuri cu proveniență** și **un răspuns cu dovada la secțiunea 2**. Fără cod de producție modificat.

---

## 0. Ce a deschis livrarea precedentă

Ai stabilit, și am reverificat pe toate cele 143 de chei, că **niciun endpoint public nu poartă o marcă temporală a decontării.** `SOURCE_ONCHAIN_SETTLEMENT` e inaccesibil fără un nod Polygon RPC.

Uitându-mă unde duce asta, am găsit ceva pe disc care nu mai stă în picioare.

`historical_paper_replay.py` construiește exact lucrul de care are nevoie un backtest — pachete de piețe rezolvate cu bandă de prețuri. Linia 152 ștanțează pe **fiecare** punct din bandă:

```python
HistoricalTapePoint(market_id, outcome_id, observed_at, float(raw["p"]),
                    source_ref, resolution_known_at, resolution_known_at)
```

Ultimele două poziții sunt `acquired_at` și `known_as_of`. Iar `resolution_known_at` vine din `collect_resolved_bundles`:

```python
acquired = datetime.now(timezone.utc).isoformat()...
bundle = build_market_bundle(payload, resolution_known_at=acquired, ...)
```

**Momentul în care a rulat scriptul.**

Deci fiecare punct de preț observat cu luni în urmă e marcat ca devenind cognoscibil acum, iar fiecare rezoluție istorică e înregistrată ca și cum răspunsul ei ar fi apărut în secunda interogării. Câmpurile pe care se sprijină întreaga apărare împotriva scurgerii temporale poartă o valoare care nu e ce pretinde.

Nu era o scurtătură: **nu exista nimic altceva de pus acolo**, iar asta abia acum se știe, din captura ta.

---

## 1. Livrabilul 1: o captură de istoric de prețuri

`historical_paper_replay.py` presupune două endpoint-uri și o formă, niciodată apelate:

```
https://clob.polymarket.com/prices-history?market=<clobTokenId>&interval=max&fidelity=1440
https://clob.polymarket.com/batch-prices-history
```

și așteaptă `history[]` cu obiecte `{"t": <secunde unix>, "p": <preț>}` (liniile 103 și 149).

Ia **un singur `clobTokenId`** dintr-o piață rezolvată din captura ta — de exemplu cel al câștigătorului de la `4504950`, indexul unde `outcomePrices` are `"1"` — și capturează răspunsul brut.

Fișiere:

```
07_EVALUATION/polymarket/fixtures/real_clob_prices_history_<market_id>.json
07_EVALUATION/polymarket/fixtures/real_clob_prices_history_<market_id>.provenance.json
```

Aceeași proveniență ca data trecută: URL exact, marcă temporală UTC, cod HTTP, număr de înregistrări, SHA-256. Octeții întorși de server, nimic curățat.

Verifică și raportează, cu valorile din captură:

- Cheia de nivel superior e chiar `history`? Obiectele au chiar `t` și `p`?
- `t` sunt secunde unix, sau milisecunde? O confuzie aici mută fiecare preț cu 50 de ani.
- Ce interval acoperă seria, câte puncte, și ce distanță reală e între ele la `fidelity=1440`?
- Dacă `batch-prices-history` cere POST și o formă anume, spune care — `_http_json_post` presupune ceva.

---

## 2. Întrebarea care contează

**Poartă răspunsul de istoric vreo marcă temporală în afară de `t`?**

`t` e momentul *observației de preț*. Întrebarea e dacă există ceva care să spună **când a devenit acea observație disponibilă public** — adică o valoare onestă pentru `acquired_at` și `known_as_of`.

Aproape sigur nu. Dacă e așa, spune-o cu lista completă a cheilor din răspuns, și apoi răspunde la partea care chiar decide:

**Ce ar trebui să conțină cele două câmpuri, dat fiind că adevărul nu e disponibil?**

Trei variante, și vreau argumentul, nu preferința:

1. **`observed_at` copiat în amândouă.** Onest pentru o bursă publică unde prețul e vizibil în momentul tranzacționării — dar șterge distincția dintre „s-a întâmplat" și „am aflat".
2. **Momentul interogării, ca acum, dar marcat explicit ca atestare.** Adevărat despre *noi*, nu despre piață. Face orice filtrare pe `known_as_of` să respingă totul.
3. **`None`, cu obligația ca apelantul să furnizeze o atestare.** Refuză să inventeze. Sparge apelanții existenți.

Spune ce se strică la fiecare, concret, numind funcțiile care citesc câmpurile. **Nu implementa niciuna.**

---

## 3. Livrabilul 2: `PRICE_TAPE_FINDINGS.md`

1. Forma reală a răspunsului, cheie cu cheie, cu valori din captură.
2. Fiecare presupunere din `historical_paper_replay.py` confirmată sau infirmată, cu numărul liniei: `history[]` (103), `t`/`p` (149), unitatea lui `t` (151), `fidelity` (97).
3. Răspunsul de la secțiunea 2, cu lista completă a cheilor ca dovadă.
4. **Câte piețe rezolvate au efectiv bandă de prețuri.** `build_market_bundle` aruncă `"resolved market has no CLOB historical price points"`. Din cele 100 din captura ta, câte trec? Dacă răspunsul e 12, asta e dimensiunea reală a oricărui backtest, nu 2.100.
5. Dacă `enableOrderBook` filtrează majoritatea — `collect_resolved_bundles` îl cere `is True` — spune câte din cele 100 îl au.

---

## 4. Reguli care nu se negociază

- **Nu modifica niciun modul din `03_IMPLEMENTATION/packages/polymarket/`.** Constatarea despre liniile 149–152 e de raportat, nu de reparat aici.
- **Nu rula `collect_resolved_bundles`.** Scanează cinci pagini și face zeci de cereri. O captură pe un singur token e suficientă.
- **Fără autentificare.** Dacă CLOB cere o cheie, oprește-te și spune.
- **Rate limit rezonabil.**
- **Nu atinge ontologia.**
- **Anunță pe nume fiecare fișier adăugat.**
- **Lucrează pe ramură proprie**, cuttă din `origin/main` (conține acum PR #112, #115 și, după merge, #117).

---

## 5. Standardul de raportare

Livrarea precedentă a fost prima în care fiecare cifră s-a verificat: hash-uri, mărci temporale, `resolvedBy`, tipurile de wire — toate exacte. Am verificat mai dur decât ai cerut-o tu, scanând toate cele 143 de chei în loc de patru câmpuri, și a ținut.

Două lucruri din raportul acela **nu** s-au putut verifica de aici și sunt marcate ca atare în PR: limita `offset ≤ 2000` cu HTTP 422, care cere rețea, și afirmația că piețele 50-50 poartă `["0.5", "0.5"]` — în captură nu există nicio astfel de piață, toate 100 sunt 1/0. Dacă a doua a fost dedusă din documentație și nu din date, spune-o; o generalizare marcată ca observație e singurul lucru care a alunecat.

Ține același standard: **fiecare valoare din raport, găsibilă cu `grep` în captură.**

---

## 6. De ce asta, acum

Ontologia e închisă și pe main. Polymarket are 24 de module și acum 200 de piețe reale — dar niciun preț, deci niciun backtest.

Iar dacă banda de prețuri nu poate purta o proveniență onestă, atunci ce se construiește peste ea nu e un backtest cu o mică imprecizie, ci o măsurătoare a cărei premisă temporală nu e verificabilă. Mai bine aflăm dintr-o captură pe un singur token.
