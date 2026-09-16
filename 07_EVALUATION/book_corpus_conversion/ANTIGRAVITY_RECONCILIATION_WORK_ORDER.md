# WORK ORDER — ANTIGRAVITY: împăcarea catalogului de purjare cu manifestul de verdicte

> Destinatar: ANTIGRAVITY (citește tu fișierele, direct; nu delega analiza unui LLM local)
> Emis: 2026-09-12 · Precedent: `ANTIGRAVITY_VERDICTS_WORK_ORDER.md`, livrat și verificat
> Rezultat așteptat: **o dispoziție pentru fiecare din cele 213 rânduri de slot**, plus un răspuns argumentat la întrebarea de la secțiunea 4. **Zero scrieri în ontologie.**

---

## 0. De ce există această sarcină

Manifestul pe care l-ai produs face poarta reală: 91 de judecăți, citibile de mașină, verificate rând cu rând. Confirmat independent pe disc — cele 8 ținte `merge_into` există toate ca `promoted`, diferența simetrică e mulțimea vidă, sloturile neatinse.

Dar poarta guvernează **intrarea**, iar cele 201 au intrat acum cinci zile.

Rularea prin poartă, peste o copie a sloturilor, arată exact cât de limitată e: din cele 48 admise prin `PROMOTE`, **41 sunt deja rânduri în fișierele de slot**. Doar 7 sunt noi, și toate 7 sunt termenii Cryptoassets. Cele 41 continuă să scrie `proposed`, cu `promoted_note_id` gol. Judecata că ele aparțin ontologiei stă într-un JSON *lângă* ontologie, nu *în* ea.

Există, în paralel, un catalog de purjare construit **înainte** ca manifestul să existe, pe alt raționament: `30_SCRIPTS/ingestion/purge_batches_catalog.json`, 129 de rânduri în cinci loturi. Două aparate care judecă aceleași rânduri, din două direcții, fără să se fi văzut vreodată.

Sarcina ta este să le pui față în față.

---

## 1. Ce am măsurat deja — pornește de aici, nu de la zero

Verificat pe disc, azi, cu `slot_rows.read_all` și `promotion_verdicts.load_manifest`:

**Sloturile**: 213 rânduri — 43 `promoted`, 160 `proposed`, 10 `unverified_source`.

**Catalogul de purjare, încrucișat cu manifestul:**

| Lot | Rânduri | Verdict în manifest |
|---|---|---|
| A | 36 | 28 `REJECT` + 8 `MERGE` — **acord perfect** |
| B | 62 | 62 × `NO_VERDICT` |
| C | 23 | 23 × `NO_VERDICT` |
| D1 | 5 | 4 × `NO_VERDICT` + **1 × `PROMOTE`** |
| D2 | 3 | 3 × `NO_VERDICT` |
| | **129** | |

Trei lucruri ies din tabelul ăsta, și fiecare e o întrebare, nu o concluzie.

**Lotul A este acordul.** Toate cele 36 de rânduri au verdict explicit de respingere sau fuziune. Două raționamente independente, aceeași listă. Acesta e lotul despre care se poate spune că a fost judecat de două ori.

**85 de rânduri (B + C) sunt puse la ștergere fără ca nimeni să le fi judecat.** Nu sunt respinse — sunt nejudecate, exact categoria pentru care a fost construită poarta. A le șterge pentru că sunt nejudecate este simetricul exact al greșelii de a le fi admis pentru că un script a rulat. Poate fi decizia corectă; nu poate fi decizia *tăcută*.

**O contradicție, una singură.** `Experience Replay` din `15_retrieval.md:43` e în lotul D1 cu motivul „redenumit / duplicat cu experience replay în 16_consolidation.md (proposed)", iar manifestul îi dă `PROMOTE`.

---

## 2. Contradicția nu e o greșeală a niciunuia dintre voi

Am verificat: **exact un singur nume de concept apare în două fișiere de slot** din cele 213 rânduri.

```
experience replay -> 15_retrieval.md:43, 16_consolidation.md:43
```

Catalogul spune „șterge-l pe cel din retrieval, e duplicatul". Manifestul spune „experience replay se promovează". Ambele au dreptate, despre **rânduri diferite**.

Cauza e în formă, nu în judecată: **manifestul cheiază pe numele conceptului, iar un rând de slot e identificat de perechea `(slot_file, concept)`.** Un verdict per nume nu poate exprima „da în consolidation, nu în retrieval". Normalizarea din poartă le colapsează pe amândouă în aceeași cheie.

Asta e singura instanță azi. Devine o clasă întreagă de ambiguitate în momentul în care un al doilea concept ajunge în două sloturi — și `SPLIT`-ul tău pe `familiarity` (confidence vs. state) este fix acel caz, care așteaptă să fie scris.

---

## 3. Livrabilul: dispoziția celor 213 rânduri

`07_EVALUATION/book_corpus_conversion/ROW_DISPOSITION.md` — un tabel cu **un rând pentru fiecare rând de slot**, nu doar pentru cele 129 catalogate:

| `slot_file:line` | concept | status | occ | lot purjare | verdict manifest | dispoziție propusă | temei |
|---|---|---|---|---|---|---|---|

`dispoziție propusă` ia una din: `PĂSTREAZĂ-PROMOVEAZĂ`, `PĂSTREAZĂ-PROPOSED`, `ȘTERGE`, `FUZIONEAZĂ-ÎN(țintă)`, `DESPARTE`, `NEDECIS`.

Reguli de completare:

- Cele 43 `promoted` nu se ating și nu se propun spre ștergere. Au note id și muchii în graf; ștergerea lasă orfani. Listează-le ca `PĂSTREAZĂ-PROMOVEAZĂ` și treci mai departe.
- Cele 10 `unverified_source` **nu sunt un `proposed` mai slab.** Extractorul care le-a produs a fost înlocuit, iar înlocuitorul nu le-a putut reproduce din textul sursă: lanțul de proveniență e rupt, deci rândul nu poate fi nici re-derivat, nici apărat. Nu le topi în restul. Dispoziție proprie, temei propriu. `UNVERIFIED_SOURCE_TRIAGE.md` există deja — reia-l, nu-l reface.
- Acolo unde catalogul și manifestul spun același lucru, spune-o scurt. Acolo unde diferă, **temeiul e obligatoriu și explicit**.
- Pentru cele 85 din B și C: pentru fiecare, spune dacă ești de acord cu ștergerea **și pe ce bază**, dat fiind că sunt nejudecate. Dacă temeiul tău e „sub prag de occurrences", scrie pragul și valoarea. Dacă e „artefact de extragere", citează fragmentul.

---

## 4. Întrebarea de arhitectură — răspunde argumentat, nu o implementa

**Trebuie manifestul să cheieze pe `(slot_file, concept)` în loc de `concept`?**

Argumentează în ambele direcții, cu ce e pe disc:

- *Pentru*: `experience replay` e deja nereprezentabil, iar `SPLIT`-ul pe `familiarity` cere prin definiție două destinații pentru un nume.
- *Împotriva*: un concept care există în două sloturi poate fi el însuși defectul, iar o cheie care îl face exprimabil îl și legitimează. Un singur caz în 213 rânduri nu e neapărat o clasă.

Spune care variantă o recomanzi și **ce s-ar strica** dacă se alege cealaltă. Decizia e a lui Owner; `promotion_verdicts.py` nu se modifică în această sarcină.

---

## 5. Comenzile de verificat, rulate de tine

```bash
PYTHONIOENCODING=utf-8 python -c "
import sys, json, collections
sys.path.insert(0,'30_SCRIPTS/ingestion')
from promotion_verdicts import load_manifest
from slot_rows import read_all, count_by_status
rows = read_all('01_ARCHITECTURE/ontology/slots')
m = load_manifest('07_EVALUATION/book_corpus_conversion/promotion_verdicts.json')
cat = json.load(open('30_SCRIPTS/ingestion/purge_batches_catalog.json', encoding='utf-8'))
print('sloturi:', len(rows), count_by_status(rows))
for b, items in cat.items():
    c = collections.Counter((m.get(i['concept']).verdict if m.get(i['concept']) else 'NO_VERDICT') for i in items)
    print(b, len(items), dict(c))
"
```

Dacă cifrele tale nu dau 213 / 43-160-10 și 36-62-23-5-3, **oprește-te și spune ce ai obținut.** O nepotrivire înseamnă că fișierele s-au schimbat între timp, iar asta e informație mai importantă decât livrabilul.

Notă practică: pe Windows, `print` pe un slot cu diacritice crapă cu `UnicodeEncodeError` fără `PYTHONIOENCODING=utf-8`. Am pățit-o azi.

---

## 6. Reguli care nu se negociază

- **Nu atinge `01_ARCHITECTURE/ontology/slots/`.** `git diff --stat` pe el trebuie să fie gol la final; include ieșirea în raport.
- **Nu rula `purge_rejected_rows.py` fără `--dry-run`**, și nici măcar cu, pe directorul real. Execuția purjării este decizia lui Owner.
- **Nu propune dispoziții pentru rânduri `promoted`.**
- **Nu modifica manifestul.** Dacă recitind un verdict îl consideri acum greșit, listează-l separat — nu îl rescrie.
- **Nu folosi modele locale.** Citește fișierele tu.
- **Lucrează pe ramură proprie**, cuttă din `antigravity/r064-manifest-conversion` (conține poarta și manifestul). Fără `git stash`, fără `checkout` peste modificări nesalvate în arborele principal, care e permanent murdar.
- **Anunță orice fișier de cod pe care îl adaugi.** `test_staging_invariants.py` a apărut în livrarea precedentă fără să fie menționat în raport. Testele erau corecte și trec — dar cod care intră în CI fără să fie declarat este exact forma problemei pe care o reparăm: ceva care ajunge înăuntru pentru că a rulat un proces, nu pentru că a decis cineva.
- **Dacă o afirmație din acest document e falsă**, spune-o cu dovada pe disc și nu continua pe baza ei. Corectarea ta la aritmetica 90 vs. 91 — `reservoir sampling`, seed din 2026-09-07, absent din staging — a fost corectă, iar presupunerea mea greșită.

---

## 7. Ce face livrabilul posibil

După `ROW_DISPOSITION.md`, întrebarea „ce facem cu cele 213 rânduri?" încetează să mai fie o întrebare deschisă și devine cinci liste pe care cineva le poate aproba sau respinge, fiecare rând cu un temei atașat.

Acum nu e așa. Catalogul propune ștergerea a 129 de rânduri dintr-un raționament, manifestul judecă 91 de concepte din altul, cele două se suprapun parțial și nimeni nu a pus hârtiile una lângă alta. Atât timp cât rămâne așa, orice execuție — purjare sau promovare — se face pe încredere, nu pe evidență.
