# CAMPANIE DE NOAPTE — ANTIGRAVITY: de la 200 de piețe capturate la un backtest care înseamnă ceva

> Destinatar: ANTIGRAVITY · Emis: 2026-09-14, 00:05 · Orizont: până la 06:00
> Șapte etape. Fiecare produce un artefact verificabil pe disc.
> **Verificare la fiecare 50 de minute.** Nu aștepta confirmarea ca să treci mai departe — dacă o etapă e gata, începe următoarea și lasă raportul în urmă.

---

## Regula care guvernează toată noaptea

Nu construi peste nimic nevalidat. Fiecare etapă are o întrebare de „se poate / nu se poate" **înaintea** oricărei construcții, iar un „nu se poate" documentat cu dovada e livrare completă, nu eșec.

De două ori până acum răspunsul „nu există" a fost cel mai valoros lucru livrat: marca temporală de decontare care nu e în niciun endpoint, și cele două câmpuri de proveniență umplute cu momentul rulării. Ambele au schimbat arhitectura. Un „am găsit un câmp care merge" ar fi fost mai rău decât inutil.

**Fiecare valoare din fiecare raport trebuie găsibilă cu `grep` într-un fișier capturat.** Dacă o cifră nu poate fi localizată așa, e inventată, oricât de plauzibilă ar fi.

---

## ETAPA 1 — banda de prețuri (ordin separat, deja livrat)

`ANTIGRAVITY_PRICE_TAPE_WORK_ORDER.md`. O captură `prices-history` pe un singur token, forma reală, unitatea lui `t`, și răspunsul despre ce ar trebui să conțină onest `acquired_at` și `known_as_of`.

**Poartă către etapa 2:** dacă `history[]` nu există sau `t` nu e interpretabil, oprește-te aici și scrie de ce. Restul campaniei presupune o bandă utilizabilă.

---

## ETAPA 2 — cât de adânc ajunge corpusul

Din captura precedentă știm că `offset` se oprește la 2000 cu HTTP 422 și că endpoint-ul indică `/markets/keyset` pentru paginare mai adâncă.

**Întrebarea:** câte piețe rezolvate, cu bandă de prețuri reală, sunt efectiv accesibile?

Nu număra piețe rezolvate. Numără piețe care trec **toate** filtrele pe care le cere `collect_resolved_bundles`:

- `closed = true`
- `enableOrderBook is True` — filtrul care aruncă cel mai mult, verifică pe cele 100 deja capturate
- `clobTokenIds` prezent și decodabil
- `outcomePrices` conținând un `"1"` clar (nu `["0.5","0.5"]`)
- istoric de prețuri nevid la `prices-history`

Livrează `CORPUS_DEPTH_FINDINGS.md`: câte trec fiecare filtru, în cascadă, cu cifra care rămâne la final. **Acea cifră e tavanul oricărui backtest**, și e singurul lucru care spune dacă merită construit unul.

Eșantionează, nu paginat tot. 300–500 de piețe sunt destule ca să estimezi rata; nu oglindi baza lor.

---

## ETAPA 3 — un set de date istoric, cu proveniență

Construiește un set de date real: piețele care trec etapa 2, fiecare cu banda ei completă de prețuri.

```
07_EVALUATION/polymarket/fixtures/historical_dataset_<N>markets.json
07_EVALUATION/polymarket/fixtures/historical_dataset_<N>markets.provenance.json
```

Proveniența trebuie să spună, per piață: URL-urile interogate, momentele UTC, numărul de puncte din bandă, intervalul acoperit. Plus SHA-256 pe fișierul întreg.

**Nu folosi `collect_resolved_bundles`.** Face zeci de cereri necontrolate și ștanțează `datetime.now()` peste tot. Scrie un script de captură separat, în `scratch/`, care nu atinge pachetul de producție.

**Nu inventa `acquired_at` și `known_as_of`.** Lasă-le `null` în captură. Ce ar trebui să conțină e răspunsul de la etapa 1 și decizia lui Owner.

---

## ETAPA 4 — ce spune o replică mecanică

`run_mechanical_control` din `historical_paper_replay.py` execută un control: cumpără la primul punct din bandă, ține până la rezoluție, raportează rezultatul. E linia de bază față de care orice model trebuie să arate mai bine.

Rulează-l pe setul de la etapa 3 — **pe date, nu prin `collect_resolved_bundles`** — și raportează distribuția, nu media. Câte piețe în profit, câte în pierdere, care e coada.

`MECHANICAL_CONTROL_RESULTS.md`. Dacă un control cumpără-și-ține pe partea câștigătoare dă un randament absurd, asta nu e o descoperire, e un semn că banda de prețuri e citită greșit. Spune-o.

---

## ETAPA 5 — auditul advers al scurgerii temporale

Aici e miezul nopții.

Sistemul are `LeakageProof`, ferestre semi-deschise, reguli de graniță, și un contract temporal pe faza 12. Toate testate pe date sintetice pe care le-am scris noi.

**Sarcina: încearcă să faci sistemul să vadă viitorul, pe date reale.**

Construiește, din setul de la etapa 3, cazuri în care o decizie luată la momentul T ar putea atinge informație de după T:

- un punct din bandă exact la T (regula de graniță, semi-deschis — cine îl primește?)
- o rezoluție a cărei `known_as_of` e `null`: e respinsă, sau trece ca „necunoscut deci permis"?
- două piețe pe același eveniment, una rezolvată înaintea celeilalte
- o bandă în care `t` e mai mare decât `endDate` al pieței

Pentru fiecare: **ce face codul efectiv**, cu funcția și linia. Nu ce ar trebui să facă.

`LEAKAGE_ADVERSARIAL_AUDIT.md`. Dacă nu găsești nicio breșă, spune asta cu cazurile încercate enumerate — o apărare care rezistă la un atac documentat valorează mai mult decât una netestată. Dacă găsești una, **nu o repara**; descrie-o exact.

---

## ETAPA 6 — atestarea onestă

Din etapa 1 se știe că nu există marcă temporală de decontare. Deci fiecare rezoluție dintr-un backtest e o atestare umană.

Scrie atestările pentru piețele din setul de la etapa 3, în forma cerută de `resolutions.py`: `market_id`, `status`, `winning_outcome_ids`, `known_at`, `source = manual_attested`, `source_ref`.

Pentru `known_at`, folosește cea mai apărabilă valoare disponibilă și **spune în `source_ref` exact ce este** — de exemplu `closedTime` plus perioada de contestație UMA, cu durata declarată. Nu o prezenta ca decontare. Scopul e ca `ResolutionSet.by_source()` să poată raporta onest că 100% e atestare, și ca cineva care citește peste șase luni să vadă pe ce s-a sprijinit.

`07_EVALUATION/polymarket/fixtures/resolutions_attested_<N>.json`.

---

## ETAPA 7 — raportul de noapte

`OVERNIGHT_CAMPAIGN_REPORT.md`, o pagină:

1. Ce s-a stabilit că **nu se poate**, cu dovada. Aceasta e prima secțiune pentru că e partea care schimbă deciziile.
2. Tavanul real al corpusului, din etapa 2.
3. Rezultatul controlului mecanic, cu distribuția.
4. Breșele de scurgere găsite sau cazurile încercate fără breșă.
5. Fiecare fișier adăugat, pe nume, cu dimensiune și SHA-256.
6. `git diff --stat` pe `03_IMPLEMENTATION/packages/polymarket/` și pe `01_ARCHITECTURE/ontology/slots/` — ambele trebuie să fie goale.

---

## Reguli pentru toată noaptea

- **Niciun modul de producție modificat.** Constatările se raportează; reparațiile sunt decizii de dimineață.
- **Scripturile de captură trăiesc în `scratch/`**, nu în pachet.
- **Fără autentificare.** Dacă ceva cere o cheie, oprește-te și spune care.
- **Rate limit uman.** O pauză între cereri. Dacă primești 429, oprește-te și raportează — nu relua în buclă.
- **Nu atinge ontologia.**
- **Ramură proprie** pentru fiecare etapă, cuttă din `origin/main`. Nu lucra în arborele principal peste modificări nesalvate.
- **Anunță pe nume fiecare fișier adăugat**, inclusiv scripturile din `scratch/`. Un fișier de cod care intră nedeclarat a mai stricat o dată CI-ul aici.
- **Dacă o etapă se blochează**, treci la următoarea și notează blocajul. Nu aștepta.

---

## Ce se verifică la fiecare 50 de minute

La fiecare verificare deschid fișierele, nu rapoartele. Se compară cifra din raport cu valoarea din captură. Tiparul de până acum e clar: **analiza empirică a fost exactă de fiecare dată; propozițiile care o descriu au greșit de trei ori.** Ultima livrare a fost prima fără nicio abatere — hash-uri, mărci temporale, tipuri de wire, toate exacte. Ține standardul acela.

Dacă o cifră nu se verifică, o spun în clar și continuăm de acolo. Nu e o notă; e cum funcționează.
