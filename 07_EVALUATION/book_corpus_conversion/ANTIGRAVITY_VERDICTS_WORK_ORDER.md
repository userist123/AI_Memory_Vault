# WORK ORDER — ANTIGRAVITY: conversia PROMOTION_REVIEW.md în manifestul de verdicte

> Destinatar: ANTIGRAVITY (citește tu fișierele, direct; nu delega analiza unui LLM local)
> Emis: 2026-09-12 · Ramură de referință: `r064-verdicts-gate` (PR #112)
> Rezultat așteptat: **un singur fișier JSON**, plus un raport scurt cu ce nu s-a putut mapa.

---

## 0. Contextul pe care trebuie să-l ai înainte să scrii o linie

Commit-ul `3de7fff3e` a rulat merge-ul peste corpusul reconciliat și a scris **201 concepte** în cele 16 fișiere de slot, toate `proposed`, într-un singur pas. Scriptul a făcut exact ce fusese construit să facă. Nimic nu era stricat.

**160 dintre ele erau încă acolo cinci zile mai târziu, judecate de nimeni.** Când cineva le-a citit în sfârșit (tu, în `PROMOTION_REVIEW.md`): 28 erau sintagme, nu concepte; 8 dublau concepte deja promovate; 1 era două concepte diferite sub același cuvânt; 6 cereau o decizie arhitecturală pe care n-o luase nimeni. **47% din ce recurase destul de des cât să ajungă la pragul de recenzie nu avea ce căuta într-o ontologie.**

Fișierele de slot erau zonă de lucru pentru scriptul de merge și ontologie canonică pentru tot restul. Nimeni n-a scris o poartă pentru că nimeni nu credea că există o graniță.

Poarta există acum: `30_SCRIPTS/ingestion/promotion_verdicts.py`, consumată de `merge_candidate_concepts(..., verdicts_file=...)`.

**Ea nu judecă calitatea.** Un manifest care marchează o sintagmă `PROMOTE` lasă sintagma să treacă și și-a făcut treaba. Ce elimină este rândul *neexaminat*: cel care a intrat pentru că a rulat un script, nu pentru că a decis cineva. Aceia erau 160 din 213.

Sarcina ta este să transformi judecățile tale — care există deja, în proză — în forma pe care poarta o poate citi.

---

## 1. Sursa: exact ce citești

`07_EVALUATION/book_corpus_conversion/PROMOTION_REVIEW.md`, **Tabelul 2**, care începe la linia 53 cu antetul:

```
| # | Concept | Slot | Occ. | Sursă (rânduri & cărți) | Verdict Recomandat | Justificare Grounded pe Text |
```

și conține rândurile #1–#91.

**Nu reciti cărțile. Nu reevalua conceptele. Nu schimba verdictele.** Acesta este un exercițiu de transcriere fidelă, nu de re-judecare. Dacă în timpul transcrierii găsești un verdict pe care îl consideri greșit, **nu-l corecta în JSON** — notează-l separat, la secțiunea 6, și lasă decizia lui Owner.

---

## 2. Ținta: `07_EVALUATION/book_corpus_conversion/promotion_verdicts.json`

```json
{
  "schema_version": "polymarket-promotion-verdicts.v1",
  "source_review": "07_EVALUATION/book_corpus_conversion/PROMOTION_REVIEW.md",
  "decided_at": "2026-09-12",
  "decided_by": "antigravity",
  "verdicts": [
    {"concept": "generalization", "verdict": "PROMOTE", "reason": "Definește metrica de certitudine epistemică și transfer out-of-distribution ..."},
    {"concept": "architecture",   "verdict": "REJECT",  "reason": "Etichetă descriptivă ultra-generică pentru un sistem compus ipotetic ..."},
    {"concept": "factual memory", "verdict": "MERGE",   "reason": "Izomorf cu declarative memory, deja promovat", "merge_into": "declarative memory"}
  ]
}
```

### Maparea verdictelor — exactă, fără interpretare

| În `PROMOTION_REVIEW.md` | În manifest | Câte așteptăm |
|---|---|---|
| `PROMOVEAZĂ` | `PROMOTE` | 48 |
| `FUZIONEAZĂ` | `MERGE` (+ `merge_into` obligatoriu) | 8 |
| `DESPARTE` | `SPLIT` | 1 |
| `RESPINGE` | `REJECT` | 28 |
| `NESIGUR` | `UNSURE` | 6 |
| | **total** | **91** |

Dacă numărătoarea ta finală nu dă exact aceste cifre, **oprește-te și raportează diferența** înainte de a livra fișierul. O nepotrivire înseamnă ori că tabelul de sinteză din capul documentului nu corespunde tabelului detaliat, ori că ai sărit un rând — ambele sunt informație, nu erori de rotunjit.

---

## 3. Patru reguli pe care poarta le impune, și de ce

**`PROMOTE` singur admite.** `UNSURE` deliberat nu. O decizie pe care nimeni n-a putut s-o ia nu e o decizie de a continua, iar tratarea ei ca atare este exact modul în care „ne uităm mai târziu" devine rând permanent în ontologie. Cele 6 `NESIGUR` ale tale rămân afară până când Owner decide arhitectura.

**Un concept absent din manifest e reținut sub `NO_VERDICT` și numărat**, niciodată aruncat în tăcere. O rulare care le-ar fi sărit fără urmă ar arăta identic cu una în care fuseseră judecate — fix mecanismul prin care cele 160 au devenit invizibile.

**`reason` e obligatoriu pe fiecare intrare, inclusiv pe aprobări.** Un verdict fără motiv este un vot, iar eșecul care se repară aici a fost exact acela: rânduri intrate fără ca cineva să spună de ce. Nu scrie `"reason": "ok"`. Reia propoziția-cheie din coloana *Justificare Grounded pe Text* — condensată e în regulă, goală nu.

**`MERGE` trebuie să numească ținta.** Fără `merge_into`, rândul dispare și `occurrences` ale lui nu aterizează nicăieri. Toate cele 8 ținte sunt, conform recenziei tale, strict din cele 43 deja promovate — **verifică fiecare țintă că există efectiv într-un fișier de slot cu `status=promoted`**, nu presupune.

---

## 4. Formatarea numelor — greșeala făcută de două ori aici

Conceptele apar ca ``**`architecture`**`` în tabelele de recenzie și simple în fișierele de slot. O comparație care ratează diferența raportează zero suprapunere între două liste cu aceleași concepte. S-a întâmplat de două ori într-o singură zi în acest repository, o dată de Claude: „0 din 91 de concepte există deja în sloturi", răspunsul real fiind 84 din 91.

Poarta normalizează (`strip` pe `*` și backtick, lowercase, colaps de spații), deci poți scrie numele în oricare formă. **Dar tu scrie-le curate** — fără asteriscuri, fără backticks — pentru ca fișierul să fie citibil de om, nu doar de parser.

---

## 5. Validarea — rulează asta, nu-mi spune că e corect

```bash
python -c "import sys; sys.path.insert(0,'30_SCRIPTS/ingestion'); from promotion_verdicts import load_manifest; m=load_manifest('07_EVALUATION/book_corpus_conversion/promotion_verdicts.json'); print('intrari unice:', len(m)); print('distributie:', m.counts())"
```

`load_manifest` refuză: versiunea greșită de schemă, manifestul gol, un verdict necunoscut, un `reason` gol, un `MERGE` fără țintă, două verdicte diferite pentru același concept. Dacă una dintre ele sare, ai o problemă reală în transcriere.

### Apoi rularea peste staging — citește avertismentul înainte

**`merge_candidate_concepts.py` nu are `--dry-run`. Scrie.** Singurul lucru care îl ține departe de ontologie este `--slots-dir`. Deci nu îl rula niciodată pe directorul real în această sarcină; dă-i o copie.

Nu există `staging/corpus_reconciled.json` — agregatul a fost consumat și nu mai e pe disc. Sursa reală sunt cele 21 de fișiere per-carte din `staging/` (ignorând `*_rej.json` și `*_report.json`). Verificat acum, pe disc: **21 de fișiere, 264 de rânduri brute, 0 duplicate, 201 concepte unice, dintre care 133 cu `occurrences >= 3`.**

```bash
python -c "
import sys, json, glob, os, shutil, tempfile
sys.path.insert(0, '30_SCRIPTS/ingestion')
from merge_candidate_concepts import combine_across_books, find_duplicate_rows, merge_candidate_concepts
rows = []
for p in sorted(glob.glob('staging/*.json')):
    b = os.path.basename(p)
    if b.endswith('_rej.json') or 'report' in b: continue
    d = json.load(open(p, encoding='utf-8'))
    if isinstance(d, list): rows += d
assert not find_duplicate_rows(rows), 'randuri duplicate in staging'
agg = combine_across_books(rows)
json.dump(agg, open('scratch/corpus_aggregate.json','w',encoding='utf-8'))
tmp = tempfile.mkdtemp()
shutil.copytree('01_ARCHITECTURE/ontology/slots', tmp + '/slots')
print(json.dumps(merge_candidate_concepts('scratch/corpus_aggregate.json', tmp + '/slots', verdicts_file='07_EVALUATION/book_corpus_conversion/promotion_verdicts.json'), indent=2, ensure_ascii=False))
"
```

Raportul întoarce `gated: true`, `withheld_by_verdict` și numărul de `NO_VERDICT`. **Numărul `NO_VERDICT` este cel care contează.** Manifestul acoperă 91 de concepte; agregatul conține 201. Restul de ~110 vor apărea ca `NO_VERDICT` și **așa trebuie să fie** — nu le inventa verdicte ca să faci cifra zero. Raportează câte sunt.

**Și o nepotrivire pe care nu o pot explica, verific-o tu:** 133 de concepte au `occurrences >= 3` în agregat, 43 erau deja promovate, ceea ce lasă 90 — nu 91, cât evaluează recenzia ta. Diferența e de un singur concept. Găsește-l și spune care e; s-ar putea să fie un rând evaluat de două ori, sau unul intrat sub prag. Nu-l trata ca eroare de rotunjire.

---

## 6. Ce raportezi înapoi

Un fișier scurt, `07_EVALUATION/book_corpus_conversion/VERDICTS_MANIFEST_REPORT.md`:

1. Distribuția reală obținută vs. cele 48/8/1/28/6 așteptate. Dacă diferă, care rând și de ce.
2. Cele 8 ținte `merge_into`, fiecare cu fișierul de slot și linia unde ținta apare ca `promoted`. Dacă vreuna nu există acolo, spune-o — e o eroare în recenzie, nu în transcriere.
3. Numărul de `NO_VERDICT` din dry-run, și câte dintre ele au `occurrences >= 3` (ar trebui să fie zero; dacă nu, recenzia a ratat concepte peste prag).
4. Orice verdict pe care, recitind, îl consideri acum greșit — **listat, nu aplicat**.
5. Ieșirea brută a celor două comenzi de mai sus, copiată integral.

---

## 7. Reguli de lucru care nu se negociază

- **Nu atinge `01_ARCHITECTURE/ontology/slots/`.** Nici o scriere, nici una. Sloturile rămân bit-cu-bit identice la finalul acestei sarcini; verifică cu `git diff --stat 01_ARCHITECTURE/ontology/slots/` și include rezultatul (trebuie să fie gol) în raport.
- **Nu rula merge-ul cu `--slots-dir` pe directorul real.** Nu există `--dry-run`; scriptul scrie. Copia temporară din secțiunea 5 este singura protecție. Adoptarea porții și execuția merge-ului real sunt decizia lui Owner, nu a ta.
- **Nu folosi modele locale.** Citește fișierele tu.
- **Nu construi peste ce există deja.** `promotion_verdicts.py`, `slot_rows.py` și `merge_candidate_concepts.py` sunt scrise și testate (1960 de teste trec). Nu rescrie un parser propriu pentru fișierele de slot — importă-l pe cel existent.
- **Lucrează pe o ramură proprie**, cuttă din `origin/main`, niciodată în arborele principal care e permanent murdar. Fără `git stash`, fără `git checkout` peste modificări nesalvate.
- **Dacă găsești o afirmație din acest document care e falsă** — o linie care nu există, o comandă care nu rulează, o cifră care nu se verifică — **spune-o explicit, cu dovada pe disc**, și nu continua pe baza ei. Corectarea făcută de tine la cartea Chan (unde verificarea mea de densitate tematică a dat 1.04 față de un prag de 1.00 și a ratat un fals cu 0.04) a fost corectă, iar a mea greșită.

---

## 8. De ce contează transcrierea asta

Fără manifest, poarta e cod care trece testele și nu apără nimic. Cele 91 de judecăți ale tale există deja în proză — reale, argumentate pe text, dar necitibile de nimic care scrie în ontologie. Manifestul este singurul lucru care le transformă din document în constrângere.

Și mai e ceva: după ce el există, întrebarea „de ce e conceptul ăsta în ontologie?" are un răspuns cu nume, dată și motiv, pentru fiecare rând. Acum n-are, pentru 160 dintre ele.
