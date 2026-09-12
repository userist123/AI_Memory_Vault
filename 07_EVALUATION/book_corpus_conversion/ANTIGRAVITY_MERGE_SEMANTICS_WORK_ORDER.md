# WORK ORDER — ANTIGRAVITY: ce înseamnă o fuziune, și cele 7 rânduri care o așteaptă

> Destinatar: ANTIGRAVITY (citește tu fișierele, direct; nu delega analiza unui LLM local)
> Emis: 2026-09-12 · Precedent: `disposition_manifest.json`, livrat și verificat integral
> Rezultat așteptat: **o specificație a fuziunii** și **un dosar de decizie** pentru cele 7 rânduri rămase. **Zero scrieri în ontologie. Zero cod de fuziune implementat.**

---

## 0. Unde suntem

Manifestul e corect. L-am verificat rând cu rând: 213 ancore potrivindu-se cu disc, concept și status identice peste tot, niciun `promoted` cu `DELETE`, toate cele 8 ținte `MERGE_INTO` prezente ca rânduri `promoted`, `reason` pe toate.

Cele trei reparații funcționează. Am închis și o breșă deschisă de reparația (b): `--batch ALL --allow-status unverified_source` ștergea 122 de rânduri, dintre care 10 protejate de manifest — 8 fuziuni și cele 2 recuperate din triaj. Acum toate căile converg pe aceleași 112, iar testele cer explicit să nu diveargă.

Din 213 rânduri, 206 au o dispoziție pe care o poate executa cineva. **Șapte nu.** Și toate șapte așteaptă același lucru: o decizie pe care nimeni n-a formulat-o încă suficient de precis cât să fie luată.

Corectură la raportul tău precedent, pentru că a contat: ai scris că țintele `MERGE_INTO` sunt „semantic memory, procedural memory, episodic memory, reinforcement learning, experience replay, information bottleneck". Sunt șase nume, iar patru nu apar nicăieri în fișier. Țintele reale sunt `semantic memory`, `parametric memory`, `buffer`, `working memory`, `essential variables`, `impasse`, `episodic memory`, `consolidation`. **JSON-ul e corect; propoziția care îl descrie nu era.** Cine ar fi aprobat pe baza rezumatului ar fi aprobat altceva.

---

## 1. Problema reală a fuziunii: `occurrences` nu se adună întotdeauna

`occurrences` înseamnă **numărul de secțiuni distincte care definesc termenul**. Este singurul semnal de clasificare din tot sistemul — `confidence` e 1.00 pe aproape tot, deci nu separă nimic. Dacă aritmetica fuziunii e greșită, se corupe exact numărul pe care se sprijină restul.

Am măsurat cărțile-sursă pentru fiecare pereche. **Patru din opt împart o carte între sursă și țintă:**

| sursă | occ | țintă | occ | cărți comune |
|---|---|---|---|---|
| factual memory | 9 | semantic memory | 25 | — |
| stable model | 3 | parametric memory | 11 | — |
| **working model** | 7 | buffer | 11 | `sarfraz22a` |
| contextual memory | 3 | working memory | 65 | — |
| **homeostasis** | 6 | essential variables | 36 | `ashby_design_for_a_brain` |
| **tie impasse** | 5 | impasse | 14 | `laird_soar_cognitive_architecture` |
| experiential memory | 8 | episodic memory | 50 | — |
| **long-term potentiation** | 4 | consolidation | 29 | `squire_kandel_mind_to_molecules` |

Aici e miezul. Două concepte au fost declarate izomorfe **pentru că** aceeași literatură le folosește interschimbabil. Când ambele vin din aceeași carte, e probabil ca **aceeași secțiune să fi fost numărată de două ori, sub două nume**. `homeostasis` și `essential variables` la Ashby nu sunt două idei care se întâmplă să coexiste — sunt aceeași idee, iar secțiunile care o definesc au fost numărate o dată ca una, o dată ca alta.

Adunarea dă `6 + 36 = 42` pentru un concept care poate e definit în 36 de secțiuni, nu 42. Iar 42 e apoi numărul pe care orice clasificare viitoare îl va folosi.

Cele patru perechi fără carte comună nu au această problemă: secțiunile sunt din cărți diferite, deci sunt distincte prin construcție.

**Nu rezolva asta ghicind.** Deschide `staging/` și uită-te la `evidence_quote` pentru cele patru perechi cu carte comună. Dacă citatele se suprapun, ai dovada suprapunerii. Dacă sunt din pasaje diferite, ai dovada că sunt secțiuni distincte și adunarea e corectă.

---

## 2. Livrabilul 1: `MERGE_SEMANTICS.md`

Spune, cu dovada pe text, ce trebuie să facă o fuziune:

1. **Aritmetica `occurrences`**, per pereche, nu global. Dacă răspunsul diferă între cele patru cu carte comună și cele patru fără, spune-o explicit — o regulă unică aplicată la ambele e mai probabil greșită decât simplă.
2. **Ce se întâmplă cu `evidence_quote` al sursei.** Ținta are deja un citat. Se păstrează ambele? Se alege cel mai lung? Un rând de slot are o singură coloană de evidență, deci răspunsul are consecințe asupra formatului.
3. **Ce se întâmplă cu `source_book`.** Ținta e `promoted` și are proveniență. Fuziunea adaugă o carte la ea. Poate o coloană să țină două cărți fără să strice parsarea din `slot_rows.parse_row`? Verifică, nu presupune.
4. **Ce urmă rămâne.** După fuziune, cineva care caută `factual memory` în ontologie nu-l va găsi. E acceptabil? Dacă nu, unde se înregistrează că termenul a existat și unde a plecat?
5. **Reversibilitatea.** Dacă fuziunea e greșită, cum se desface? Dacă răspunsul e „nu se poate", spune-o — e un argument important pentru ca decizia să fie luată încet.

**Nu implementa nimic.** Specificația e livrabilul. Codul vine după ce Owner alege.

---

## 3. Livrabilul 2: `PENDING_DECISIONS.md` — dosarul celor 7 rânduri

Șase `UNDECIDED` plus o despărțire. Pentru fiecare, **nu o recomandare, ci opțiunile cu ce le susține**:

| ancoră | concept |
|---|---|
| `03_ontology.md:40` | symbolic |
| `03_ontology.md:55` | integrated information |
| `03_ontology.md:60` | dual memory |
| `04_relationships.md:32` | shared memory |
| `06_procedures.md:32` | in-context learning |
| `06_procedures.md:49` | component process |
| `10_confidence.md:30` | familiarity *(SPLIT)* |

Pentru fiecare din cele 6: citatul din `staging/`, cartea, `occurrences`, **de ce ai pus `UNSURE`** (ambiguitatea concretă, nu „necesită decizie arhitecturală"), și cele două-trei căi posibile cu ce câștigă și ce pierde fiecare. Dacă recitind textul ambiguitatea ți se pare acum rezolvabilă, spune-o și arată pasajul.

Pentru `familiarity`: cele două concepte au nevoie de nume care nu se ciocnesc. Tu ai propus calificarea lexicală la intrare — `familiarity (feeling of knowing)` și `familiarity buffer`. Scrie exact rândurile de slot care ar rezulta, cu `occurrences` repartizate (3 la confidence din `why_we_forget`, 6 la state din `2504.05840v1`) și citatele corespunzătoare. Nu le scrie în sloturi; scrie-le în document.

---

## 4. Reguli care nu se negociază

- **Nu atinge `01_ARCHITECTURE/ontology/slots/`.** `git diff --stat` gol la final, inclus în raport.
- **Nu rula cu `--apply`.** Nici pe copii.
- **Nu implementa fuziunea.**
- **Nu modifica `disposition_manifest.json` sau `promotion_verdicts.json`.**
- **Nu folosi modele locale.** Citește fișierele tu.
- **Anunță pe nume fiecare fișier de cod adăugat**, în raport.
- **Verifică fiecare afirmație din raportul tău înainte s-o scrii.** Lista greșită de ținte `MERGE_INTO` din livrarea precedentă nu a stricat nimic pentru că fișierul era corect — dar un rezumat fals despre un artefact corect e mai periculos decât un artefact greșit, fiindcă artefactul se verifică și rezumatul se crede.
- **Lucrează pe ramură proprie**, cuttă din `antigravity/r064-purge-manifest`. Fără `stash`, fără `checkout` peste modificări nesalvate.
- **Dacă o cifră de aici nu se verifică**, spune-o cu dovada pe disc și oprește-te. Tabelul cărților comune din secțiunea 1 l-am măsurat eu azi; reprodu-l înainte să construiești pe el.

---

## 5. De ce acum

206 din 213 de rânduri au o dispoziție executabilă. Restul de șapte sunt blocate pe două întrebări nepuse: ce înseamnă o fuziune, și ce sunt de fapt cele șase concepte ambigue.

Atât timp cât rămân nepuse, ele nu sunt rânduri în așteptare, ci rânduri uitate — exact starea din care a pornit tot: 160 de rânduri pe care nimeni nu le-a respins și nimeni nu le-a aprobat, rămase pentru că nimeni nu formulase întrebarea la care trebuia răspuns.
