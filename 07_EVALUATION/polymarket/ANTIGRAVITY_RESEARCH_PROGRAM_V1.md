# PROGRAM DE CERCETARE — ANTIGRAVITY: Corpusul Polymarket v1 și studiul preînregistrat

> Emis: 2026-09-14, 21:00 · **Fără termen** — lucrezi până e gata și raportezi atunci.
> **Înlocuiește** `ANTIGRAVITY_EVENT_MARKETS_MANDATE.md` — întrebarea de acolo devine partea B1 a acestui program.
> Un singur livrabil mare. Structura internă de mai jos este ordinea de lucru, nu o listă de sarcini mici.

---

## 0. Miza

Douăzeci și una de faze au construit un sistem de predicție pe piețe. Ce știm sigur până acum, verificat pe date:

- nu există marcă temporală de decontare în API-ul public; toate rezoluțiile sunt atestări;
- pe 483 de piețe recente, prețul e calibrat în limitele [−7,4, +5,0] puncte, pe **63 de rezultate independente**, nu 483;
- piețele cu istoric de prețuri sunt, în eșantionul recent, aproape numai sport și praguri crypto; piețele-eveniment apar în arhiva fără carnet de ordine;
- trei breșe în apărarea împotriva scurgerii temporale, dintre care două confirmate pe date reale (`known_as_of` null pe 620 din 620 de puncte; 22 de puncte post-închidere trecute de validare).

Nimeni nu a pus încă întrebarea pentru care există tot sistemul: **au prețurile Polymarket o structură exploatabilă, și pe ce corpus s-ar putea măsura asta cinstit?**

Programul acesta răspunde la amândouă. Rezultatul poate fi un corpus și un studiu care justifică continuarea, sau un corpus și un studiu care arată că nu există nimic de exploatat. Ambele sunt livrări complete. Un studiu care găsește „ceva" prin analize alese după ce s-au văzut datele nu este.

---

## PARTEA A — Preînregistrarea (se comite ÎNAINTE de orice analiză pe rezultate)

Fișier: `07_EVALUATION/polymarket/research/PREREGISTRATION.md`, **comis și urcat înainte** de primul script care leagă prețuri de rezultate. Ordinea commit-urilor este verificabilă și o voi verifica.

Trebuie să conțină, pentru fiecare ipoteză: afirmația, metrica exactă, unitatea de independență, testul, pragul, și **ce rezultat înseamnă nul**.

Ipotezele minime:

| # | Ipoteză | Ce se măsoară |
|---|---|---|
| H1 | Calibrarea diferă pe categorie | abatere preț–frecvență pe: sport pre-meci, sport in-play, prag crypto, piață-eveniment |
| H2 | Există bias favorit–outsider | rezultatele ieftine (preț < 0,15) câștigă mai rar decât implică prețul; cele scumpe mai des |
| H3 | Eroarea depinde de orizont | calibrare la T−24h, T−6h, T−1h, T−10min față de închidere; e monotonă? există un orizont greșit evaluat? |
| H4 | Grilele crypto sunt incoerente | la același moment, P(peste 75.000) ≤ P(peste 74.800) trebuie să țină; frecvența și mărimea încălcărilor |
| H5 | Liniile aceluiași meci sunt incoerente | O/U și spread-uri pe același joc trebuie să fie monotone în prag; încălcări la același moment |
| H6 | Lichiditatea explică eroarea | volum / număr de cotații față de eroarea absolută de calibrare |

Obligatoriu în preînregistrare:

- **Unitatea de independență** pentru fiecare ipoteză (joc, activ|zi|oră, eveniment) și regula care o calculează.
- **Corecția pentru comparații multiple**: Holm-Bonferroni pe întreaga familie H1–H6. `ablation.py` o implementează deja; folosește-o, nu o rescrie.
- **Intervalele de încredere**: bootstrap pe clustere, număr de replicări și sămânța fixate dinainte.
- **Regula de oprire**: dimensiunea corpusului se fixează înainte de a vedea rezultatele, nu se extinde până „iese ceva".
- **Ce se consideră descoperire**: efect minim relevant declarat numeric, nu doar un interval care exclude zero.

Orice abatere ulterioară de la preînregistrare merge într-o secțiune `DEVIATIONS` a raportului final, cu motivul și momentul.

---

## PARTEA B — Construirea corpusului v1

Director: `07_EVALUATION/polymarket/research/corpus_v1/`.

### B1. Recensământul piețelor-eveniment

Întrebarea din mandatul înlocuit: în era CLOB, **câte rezultate independente de tip eveniment** (politică, macro, afaceri, știință, geopolitică) au istoric de prețuri utilizabil?

- Nu eșantiona după `id`; eșantionul recent e înecat de grile generate automat. Folosește etichete (`tag`), `/events`, ordonare după volum și `/markets/keyset` pentru adâncime.
- Parametrii fiecărei interogări, integral, în proveniență.

### B2. Benzi dense de prețuri

Ai raportat că `interval="all"` decimează banda de ~10–12 ori și îi taie începutul. Afirmația nu a putut fi verificată de aici și are consecințe asupra tuturor prețurilor de intrare.

- **Verifică decimarea controlat**: aceleași 20 de piețe, `interval="all"` față de ferestre explicite `startTs`/`endTs`. Salvează ambele răspunsuri brute ca fixture, astfel încât comparația să poată fi refăcută offline.
- Construiește corpusul **exclusiv** cu metoda care dă banda densă, dacă decimarea se confirmă.
- Punctele de măturare post-închidere (prețuri exact `0.0005`/`0.9995`, în ~70 de secunde după închidere) se **marchează**, nu se șterg.

### B3. Structura de independență

- Chei explicite: `game_key`, `underlying_key` (activ|zi|oră pentru crypto), `event_key` pentru piețe-eveniment.
- **Rata de eroare a regulii**: 100 de piețe alese aleator, grupate manual, comparate cu regula. Raportează acordul.

### B4. Rezoluții atestate

- Format `resolutions.py`: `source = manual_attested`.
- `known_at` = `closedTime` + perioada de contestație UMA, cu durata **declarată în `source_ref`**, nu presupusă tacit.
- Piețele anulate sau invalide, păstrate cu statusul lor, fără `winning_outcome_ids`.

### B5. Dimensiune și stocare

- Cât de mare permite onestitatea — **raportată în unități independente pe categorie**, nu în piețe.
- Fișiere de maximum 20 MB fiecare; peste, pe fragmente. Fiecare fragment cu SHA-256 în `corpus_v1/MANIFEST.json`.

---

## PARTEA C — Suita adversarială pe date reale

Fișier: `20_TESTS/polymarket/test_leakage_real_data.py`, rulând pe fixture-urile corpusului, **fără rețea**.

**Fișierul există deja** (scris de Claude, pe `main`), pe cele 50 de piețe capturate. Conține 3 teste care trec și 3 `xfail(strict=True)`, fiecare verificat că eșuează pe propria aserțiune, nu pe altă eroare:

- `HistoricalMarketBundle.validate` acceptă puncte observate după rezoluție — 0 din 11 piețe refuzate;
- `run_mechanical_control` tranzacționează pe puncte cu `known_as_of` null — 0 din 50 refuzate;
- `build_market_bundle` scrie data rezoluției ca `known_as_of` pe fiecare cotație, deci momentul în care un preț a devenit public se mută odată cu data decontării.

Contrastul pe care îl documentează: vechiul `historical_replay.HistoricalPricePoint` refuză exact aceste lucruri. Noul drum pentru date reale nu.

**Nu îl rescrie. Extinde-l:** adaugă breșa pieței-soră (rezoluția unei piețe folosibilă pentru alta pe același eveniment), care nu e acoperită, și mută testele pe corpusul v1 când există.

- Fiecare apărare care rezistă: test normal care trece.
- Fiecare breșă confirmată: test marcat `pytest.mark.xfail(strict=True, reason=...)`, care descrie exact comportamentul greșit. Astfel CI rămâne verde, iar ziua în care cineva repară breșa, testul trece neașteptat și forțează eliminarea marcajului.
- Minimum cele trei breșe deja găsite: `known_as_of` null acceptat, puncte după închidere acceptate de `HistoricalMarketBundle`, rezoluția unei piețe-soră folosibilă ca informație pentru alta pe același eveniment.

**Nu repara breșele.** Niciun fișier din `03_IMPLEMENTATION/packages/polymarket/` nu se modifică. Testele le documentează; reparația e decizia lui Owner.

---

## PARTEA D — Analiza

Exact cum spune preînregistrarea.

- Fiecare ipoteză: efect, interval bootstrap pe clustere, valoare p ajustată Holm, verdict față de efectul minim declarat.
- **Rezultatele nule primele**, apoi cele pozitive.
- H4 și H5 sunt cele mai probabile să dea ceva concret, fiindcă nu depind de rezultate, ci de coerența prețurilor la același moment. Dacă găsești încălcări, cuantifică-le **după costuri**: spread, pas minim de preț, lichiditatea disponibilă la acel moment. O incoerență care nu poate fi tranzacționată nu e o descoperire.
- Tabelele în CSV, lângă raport. Fără imagini.

---

## PARTEA E — Reproductibilitate (condiție de acceptare, nu recomandare)

Din ultimele două livrări, **niciunul din cele 33 de scripturi anunțate nu există în acest checkout**, deși toate ieșirile s-au verificat până la ultimul octet. Programul acesta nu poate fi acceptat în forma aceea.

- Toate scripturile în `07_EVALUATION/polymarket/research/`, **comise** — nu în `scratch/`, care e ignorat de git.
- Separare strictă: `fetch_*.py` (cer rețea) și `analyze_*.py` (lucrează doar pe fixture-uri).
- Un singur punct de intrare offline: `python 07_EVALUATION/polymarket/research/run_all.py` reconstruiește **toate** tabelele și cifrele raportului din fixture-uri.
- **Criteriu de acceptare:** rulez `run_all.py` fără rețea. Fiecare cifră din raport trebuie să iasă identic. O singură cifră diferită și programul se întoarce.

---

## PARTEA F — Raportul final

`07_EVALUATION/polymarket/research/RESEARCH_REPORT_V1.md`:

1. **Ce s-a stabilit că nu se poate sau nu există.** Prima secțiune, fiindcă e cea care schimbă deciziile.
2. Corpusul: unități independente pe categorie, perioadă, metoda benzii, rezultatul verificării decimării.
3. Rezultatele H1–H6, nule întâi, cu intervale și p ajustat.
4. Suita adversarială: ce rezistă, ce e breșă, cu testul corespunzător.
5. `DEVIATIONS`: fiecare abatere de la preînregistrare.
6. Recomandarea: continuă sistemul de predicție, reorientează-l, sau oprește-l — argumentată doar din rezultatele de mai sus.
7. Lista fișierelor, fiecare cu SHA-256, **toate prezente pe disc**.

---

## Reguli permanente pentru această linie de cercetare

Se aplică oricui livrează aici — Antigravity, Claude sau alt agent — și tuturor livrărilor viitoare, nu doar acestui program.

1. **Nicio ieșire de script nu poate fi citată ca verificată dacă scriptul nu e comis** în același commit sau într-unul anterior, pe aceeași ramură. Un script care există doar într-un director local al agentului (de exemplu `~/.gemini/antigravity/brain/…/scratch/`) nu contează ca sursă.
2. **Nicio descompunere nu se descrie calitativ fără cifrele ei.** Dacă un tabel intră în raport, intră cu numere. Se aplică în special rezultatelor care contrazic verdictul principal.
3. **Scripturile care cer rețea nu se pot reproduce la nivel de octet** — datele live se schimbă. Pentru ele se comite răspunsul brut capturat; reproductibilitatea se cere de la analiza care îl citește.

## Reguli

- Fără autentificare. Rate limit uman; la 429 te oprești, aștepți, raportezi, nu reiei în buclă.
- Niciun modul din `03_IMPLEMENTATION/packages/polymarket/` modificat. Ontologia neatinsă.
- Ramură proprie din `origin/main`; suita completă verde înainte de fiecare push; `30_SCRIPTS/verification/validate_repository_layout.py` trebuie să dea `PASS`.
- Fiecare cifră din raport găsibilă într-un fixture sau reprodusă de `run_all.py`.

---

## Cum voi verifica

1. Commit-ul preînregistrării precede orice commit de analiză.
2. `run_all.py` offline reproduce fiecare cifră.
3. SHA-256 din `MANIFEST.json` corespund fișierelor.
4. Recalculez independent cel puțin H1, H2 și H4 din fixture-uri, fără să mă uit la codul tău.
5. Numărul de unități independente pe categorie, verificat pe chei.
6. Testele `xfail(strict=True)` chiar eșuează din motivul declarat.
7. Suita și validatorul de layout, verzi.

Dacă programul se oprește într-un punct din motive legitime — de exemplu B1 arată că nu există piețe-eveniment cu bandă — continuă cu ce se poate măsura pe restul corpusului și spune clar ce nu s-a putut face și de ce.
