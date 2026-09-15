# WORK ORDER — ANTIGRAVITY: planul de execuție, și de ce scriptul actual nu-l poate executa

> Destinatar: ANTIGRAVITY (citește tu fișierele, direct; nu delega analiza unui LLM local)
> Emis: 2026-09-12 · Precedent: `ROW_DISPOSITION.md`, livrat și verificat integral
> Rezultat așteptat: **un manifest de dispoziție citibil de mașină** plus repararea a două defecte măsurate în `purge_rejected_rows.py`. **Zero scrieri în ontologie.**

---

## 0. Ce am găsit rulând scriptul în gol, pe o copie

`ROW_DISPOSITION.md` e complet și verificat: 213 ancore, toate reale, niciuna lipsă, niciuna dublată. Dispozițiile ies 84 / 112 / 8 / 6 / 2 / 1.

Înainte de a-ți cere următorul pas, am rulat ce ar urma să execute cineva care aprobă planul:

```
purge_rejected_rows.py --batch ALL --slots-dir <copie>
  → total_targets 129 · deleted 112 · refused 10 · not_found 0
```

**112 șterse.** Exact numărul din `ROW_DISPOSITION.md`. Și e o coincidență.

| | dispoziția ta (112) | scriptul (112) |
|---|---|---|
| `REJECT` din lotul A | 28 | 28 |
| lotul B | 62 | 62 |
| lotul C, partea `proposed` | 14 | 14 |
| `MERGE` din lotul A | — (`FUZIONEAZĂ-ÎN`) | **8** |
| `unverified_source` din D1+D2 | **8** | — (refuzate) |

Același total, compoziție diferită pe exact 16 rânduri. Scriptul **șterge cele 8 rânduri `MERGE`** și **refuză cele 8 `unverified_source`** pe care dispoziția le dă la ștergere.

O fuziune nu este o ștergere. Cele 8 rânduri `MERGE` au `occurrences` care trebuie să aterizeze în conceptul-țintă — `factual memory` (9 occ) în `semantic memory`, `experiential memory` (8 occ) în `episodic memory`, și așa mai departe. Șterse pur și simplu, numărătorile dispar și ținta rămâne cu valoarea ei veche. Raportul ar spune totuși „112 deleted", și ar părea corect.

Acesta este exact tiparul care a produs 203 în loc de 213 timp de cinci zile: **mai multe instrumente care dau același număr, din motive diferite.**

---

## 1. Al doilea defect: `--allow-status` înlocuiește, nu extinde

```
--batch ALL --allow-status unverified_source
  → deleted 10 · refused 112
```

Steagul nu adaugă `unverified_source` la mulțimea permisă; o **înlocuiește**. Cu el pornit, cele 112 rânduri `proposed` sunt refuzate. Fără el, cele 10 `unverified_source` sunt refuzate. Nu există rulare care să le acopere pe amândouă.

Asta nu e periculos în sine — două rulări rezolvă problema. E periculos pentru că numele steagului promite altceva, iar cine citește `--allow-status unverified_source` ca „permite și asta" va rula o dată, va vedea „10 deleted" și va crede că a terminat.

---

## 2. Al treilea lucru, deja verificat: catalogul numără 129, are 122

Șapte rânduri apar în câte două loturi — patru în C și D1, trei în C și D2, toate `unverified_source`.

Scriptul **nu** dublează ștergerea: `purge_rows` grupează țintele într-un dicționar cheiat pe `(fișier, nume normalizat)`, deci duplicatele se colapsează. Am verificat. Dar `total_targets` raportează 129, iar oricine raționează pe „129 de rânduri" numără șapte ștergeri care nu există. Cifra a fost repetată zile întregi, inclusiv de mine în ordinul precedent.

---

## 3. Livrabilul principal: `disposition_manifest.json`

`ROW_DISPOSITION.md` este proză. Nimic din ce scrie în ontologie nu poate citi un cuvânt din el — exact situația în care era `PROMOTION_REVIEW.md` înainte să-l transcrii.

Fă aceeași mutare, un nivel mai sus.

`07_EVALUATION/book_corpus_conversion/disposition_manifest.json`:

```json
{
  "schema_version": "ontology-row-disposition.v1",
  "source_disposition": "07_EVALUATION/book_corpus_conversion/ROW_DISPOSITION.md",
  "source_verdicts": "07_EVALUATION/book_corpus_conversion/promotion_verdicts.json",
  "decided_at": "2026-09-12",
  "decided_by": "antigravity",
  "rows": [
    {"slot_file": "03_ontology.md", "line": 33, "concept": "semantic memory",
     "status": "promoted", "disposition": "KEEP_PROMOTED", "reason": "..."},
    {"slot_file": "15_retrieval.md", "line": 43, "concept": "Experience Replay",
     "status": "unverified_source", "disposition": "DELETE", "reason": "..."},
    {"slot_file": "03_ontology.md", "line": 71, "concept": "factual memory",
     "status": "proposed", "disposition": "MERGE_INTO", "merge_into": "semantic memory",
     "reason": "..."}
  ]
}
```

Dispoziții: `KEEP_PROMOTED`, `KEEP_PROPOSED`, `DELETE`, `MERGE_INTO` (cu țintă obligatorie), `SPLIT`, `UNDECIDED`.

Invarianți pe care fișierul trebuie să-i respecte, și pe care validatorul tău trebuie să-i verifice:

- **Exact 213 intrări**, câte una pentru fiecare rând de slot de pe disc. Nici una în plus, nici una lipsă. Ancora `(slot_file, line, concept)` trebuie să se potrivească cu ce citește `slot_rows.read_all`.
- **Niciun rând `promoted` nu primește `DELETE`.** Au note id și muchii în graf.
- **`MERGE_INTO` trebuie să numească o țintă care există ca rând `promoted`**, verificată pe disc, nu presupusă.
- **`reason` obligatoriu pe fiecare intrare**, inclusiv pe cele de păstrare. Un rând care rămâne fără ca nimeni să spună de ce este exact rândul pentru care s-a construit poarta.
- **Rândul `familiarity` (`10_confidence.md:30`) apare o singură dată**, cu `SPLIT`. Destinațiile despărțirii se descriu în `reason`, nu prin două intrări — altfel numărul 213 se rupe și nimeni nu observă.

Cheia rămâne `(slot_file, line, concept)` aici, spre deosebire de manifestul de verdicte, și motivul e cel pe care l-ai argumentat tu însuți: **un verdict e despre un concept, o dispoziție e despre un rând.** Cele două `experience replay` sunt un singur concept și două rânduri, iar acesta e nivelul la care distincția trebuie să existe. Nu e o contrazicere a recomandării tale de la ordinul precedent; e consecința ei.

---

## 4. Reparațiile în `purge_rejected_rows.py`

Trei, toate mici, toate testate:

**(a) Refuză `MERGE_INTO` ca ștergere.** Scriptul nu trebuie să poată șterge un rând a cărui dispoziție e fuziune. Dacă îi dai un manifest, verifică dispoziția și refuză explicit, cu un mesaj care spune că fuziunea nu e implementată și că `occurrences` s-ar pierde.

**(b) `--allow-status` trebuie să extindă, nu să înlocuiască.** Acceptă o listă (`--allow-status proposed,unverified_source`) și păstrează `proposed` implicit. Un test care demonstrează că o singură rulare acoperă ambele categorii.

**(c) `total_targets` trebuie să raporteze rânduri distincte.** 122, nu 129. Și un avertisment explicit când intrarea conține duplicate, care să spună care sunt — nu doar un număr mai mic.

Adaugă `--disposition-file`, care citește manifestul de la secțiunea 3 și acționează doar pe intrările `DELETE`. Lasă `--batch` să funcționeze ca acum: nu rupe ce există, dar nu mai construi pe el.

**Nu implementa fuziunea.** Mutarea `occurrences` dintr-un rând în altul este o scriere în ontologie și o decizie de care depinde ce înseamnă numărătorile. Refuz-o curat și spune ce ar trebui construit.

---

## 5. Verificarea, rulată de tine

```bash
T=$(mktemp -d) && cp -r 01_ARCHITECTURE/ontology/slots "$T/slots"
PYTHONIOENCODING=utf-8 python 30_SCRIPTS/ingestion/purge_rejected_rows.py \
  --disposition-file 07_EVALUATION/book_corpus_conversion/disposition_manifest.json \
  --slots-dir "$T/slots" --json
```

Raportează: câte `DELETE`, câte refuzate și din ce motiv, câte `not_found`. **`not_found` trebuie să fie zero** — dacă nu e, ancorele din manifest nu corespund cu ce e pe disc, iar asta invalidează tot livrabilul, nu doar rândurile lipsă.

Apoi `diff -r` între copie și originalul, pentru a arăta exact ce s-ar schimba. Include numărul de linii șterse pe fișier.

---

## 6. Reguli care nu se negociază

- **Nu atinge `01_ARCHITECTURE/ontology/slots/`.** `git diff --stat` gol la final, inclus în raport.
- **Nu rula cu `--apply`**, pe niciun director. Execuția e decizia lui Owner.
- **Nu implementa fuziunea și nu modifica manifestul de verdicte.**
- **Nu folosi modele locale.** Citește fișierele tu.
- **Anunță fiecare fișier de cod pe care îl adaugi**, în raport, pe nume. `test_staging_invariants.py` a intrat în CI nedeclarat; testele erau bune, dar cod care ajunge înăuntru pentru că a rulat un proces, nu pentru că a decis cineva, este fix problema pe care o reparăm.
- **Lucrează pe ramură proprie**, cuttă din `antigravity/r064-row-disposition`. Fără `stash`, fără `checkout` peste modificări nesalvate în arborele principal.
- **Dacă o cifră din acest document nu se verifică**, spune-o cu dovada pe disc și oprește-te acolo. Cele două rulări din secțiunile 0 și 1 le-am făcut eu azi, pe o copie; reproduce-le înainte să construiești pe ele.

---

## 7. De ce contează

Planul există și e complet. Instrumentul care l-ar executa nu-l poate citi, șterge 8 rânduri care ar trebui fuzionate, refuză 8 pe care planul le dă la ștergere, și raportează un total corect din motive greșite.

Atât timp cât rămâne așa, aprobarea lui Owner ar declanșa o execuție care seamănă cu planul la nivel de cifră și diferă de el la nivel de rând. Livrabilul tău închide distanța dintre ce s-a decis și ce s-ar întâmpla.
