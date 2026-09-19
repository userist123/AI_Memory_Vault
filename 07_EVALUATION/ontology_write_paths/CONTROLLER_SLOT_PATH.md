# Controlerul poate retrage sloturile ontologiei — analiză (fără modificări de cod)

Ce este acest document: o analiză a căii `MemoryController` → `FileStorageEngine.set()` către cele
16 note `slot-*` din `01_ARCHITECTURE/ontology/slots/`. Nu modifică controlerul. Nu alege o soluție:
decizia e a proprietarului.

Fiecare afirmație de mai jos poate fi reprodusă cu `python -m pytest 20_TESTS/test_controller_slot_path.py -rxX`
(rulează pe o copie temporară a sloturilor, niciodată pe cele reale).

## 1. Corecție față de raportul din PR #166

În #166 am scris că prin această cale se pot rescrie „frontmatter-ul și corpul” notelor `slot-*`. Măsurat pe o
copie temporară, e doar parțial adevărat:

| Operație | Principal | Rezultat pe un `slot-*` (lifecycle `ACTIVE`) | De ce |
|---|---|---|---|
| `update()` (frontmatter sau `content`) | HUMAN, ADMIN, AI_AGENT | **refuzat** | din accident: schema cere `id` de tip uuid, iar `slot-06-procedures` nu e uuid (lipsesc și `category`, `verification`) |
| `propose()` cu id-ul slotului | HUMAN, ADMIN, AI_AGENT | **refuzat** | același motiv (`'slot-06-procedures' is not a 'uuid'`) |
| `archive()` | HUMAN, ADMIN | **reușește**: `lifecycle: ARCHIVED`, `archive_reason` scris | autorizatorul și politica de lifecycle permit |
| `archive()` | AI_AGENT | refuzat | `PermissionError` din autorizator (`security/authorizer.py`, `Operation.ARCHIVE`) |
| `supersede()` | HUMAN, ADMIN, **AI_AGENT** | **reușește**: `lifecycle: SUPERSEDED`, `superseded_by: <alt id>` | autorizatorul, politica și `SupersessionEnforcer` permit |

Deci rândurile tabelelor de concepte nu pot fi rescrise pe această cale azi, dar **un slot poate fi retras**
(scos din `ACTIVE`) de către orice principal, inclusiv un agent AI prin `supersede()`, fără vreun manifest de verdicte.
Refuzul la `update`/`propose` nu e o poartă, iar dacă schema relaxează `id` (a mai fost relaxată o constrângere
uuid, pentru `target_id`), calea de conținut se deschide.

## 2. Traseul exact

Toate căile sunt sub `03_IMPLEMENTATION/packages/`.

| Pas | Fișier : linie | Ce se întâmplă |
|---|---|---|
| API public | `memory/controller.py:1138` `archive()` / `:1172` `supersede()` (și `:1035` `update()`) | punctul de intrare; controlerul se importă ca `memory_controller.controller` |
| Autorizare | `memory/controller.py:244` `_check_auth` → `security/authorizer.py:47` `_policy` (`ARCHIVE` la `:53`, `SUPERSEDE` la `:55`) | `ARCHIVE`: HUMAN, ADMIN. `SUPERSEDE`: HUMAN, ADMIN, AI_AGENT. `UPDATE`: toți trei |
| Găsirea notei | `memory/controller.py` `self.storage.get(note_id)` (în `archive`/`supersede`) | notele `slot-*` sunt indexate: au `id`, iar rădăcina `01_ARCHITECTURE` e scanată de `memory/storage/file_engine.py` (`CONTENT_ROOTS`) |
| Politica de lifecycle | `memory/controller.py:1146-1156` (archive), `:1186-1196` (supersede) → `lifecycle/policy.py:192` și `:204` (tabelele de tranziții), `:243`/`:244` (`_MUTATION_PRINCIPALS`) | `ACTIVE → ARCHIVED` și `ACTIVE → SUPERSEDED` sunt tranziții permise; nu există excepție pentru `type: ontology_definition` |
| Regula de supersession | `memory/controller.py:1180` → `lifecycle/validation/supersession.py` `validate_supersession` | blochează doar auto-supersession, ciclurile și memoria verificată de un om (`verification: verified` sau `source_type: user`) suprascrisă de AI; sloturile au `source_type: design` și nu au `verification`, deci nu sunt protejate |
| Mutația | `memory/controller.py:1159` (`lifecycle = ARCHIVED`), `:1205` (`lifecycle = SUPERSEDED`) | modifică nota în memorie |
| Scrierea | `memory/controller.py:1161` și `:1231` → `memory/storage/file_engine.py:189` `set()` (`:203` `os.replace`) | rescrie fișierul slotului, în locul lui: `_target_path_for` (`:148`) păstrează calea exactă pentru o notă existentă într-o rădăcină de conținut |

Ce protejează rândurile tabelelor și **nu** stă pe această cale: `UngatedCanonicalWrite` din
`merge_candidate_concepts.py`, `promote_candidate_concept.py` și `purge_rejected_rows.py` (manifest de verdicte sau de
dispoziție) și poarta din `apply_row_disposition.py`. Controlerul nu importă niciuna dintre ele.

## 3. Cine, ce, azi

- **AI_AGENT**: poate `supersede()` un slot (nu `archive()`).
- **HUMAN, ADMIN**: pot `archive()` și `supersede()` un slot.
- Nimeni nu poate `update()`/`propose()` peste un slot, dar doar din cauza formatului `id` (vezi §1).
- Efectul: cele 16 sloturi (`ACTIVE`, `source_type: design`) pot înceta să fie `ACTIVE`; căutarea și indexul le tratează apoi ca pe orice notă retrasă.
  Verificare a intrărilor: `grep -rl "slot-[0-9][0-9]-" 01_ARCHITECTURE/knowledge` arată câte note trimit la ele (notele `Promoted_*.md` emit
  relații `part_of` către `slot-06-procedures` etc.).

## 4. Testele

`20_TESTS/test_controller_slot_path.py`:

- `test_controller_refuses_to_retire_an_ontology_slot` este `xfail(strict=True)` cu motivul
  „controlerul permite rescrierea sloturilor pe lângă poarta de verdicte”. Afirmă starea dorită (controlerul refuză). Azi pică, deci e `xfail`.
  Când cineva închide calea, testul trece neașteptat (XPASS), suita devine roșie și marcajul trebuie scos.
- Testele `test_today_*` demonstrează calea deschisă (supersede de AI_AGENT, archive de HUMAN/ADMIN, refuzul accidental la update/propose).
  Ele sunt menite să pice când calea se închide și se șterg atunci, împreună cu marcajul `xfail`.
- Un test de control arată că demonstrațiile rulează pe copie și nu ating sloturile reale.

## 5. Variante de închidere (nu aleg)

**A. Sloturile marcate imutabile prin controler.** `archive`, `supersede`, `update` și `propose` refuză orice notă
`type: ontology_definition` (sau id cu prefixul `slot-`).
- Câștig: închide toate operațiile pe o singură regulă, indiferent de principal; independentă de forma `id`, deci nu depinde de schemă.
- Se strică: nu mai există niciun mod de a retrage sau de a înlocui un slot prin controler (ar rămâne editarea manuală sau un script cu poartă proprie);
  controlerul primește o regulă specifică unui tip de conținut, ceea ce e o excepție codată în el.

**B. Scrierea sloturilor permisă doar unui principal dedicat.** Un principal nou (de exemplu un curator de ontologie) în `security/authorizer.py`
și `lifecycle/policy.py`; `archive`/`supersede`/`update` pe `slot-*` cer acest principal și, opțional, un manifest.
- Câștig: păstrează fluxul de retragere și lasă urme în audit; se poate cere manifestul de verdicte și aici, deci poarta devine aceeași cu a scripturilor.
- Se strică: extinde enumerările `Principal`/`PrincipalRole` și matricea autorizatorului (suprafață mai mare de testat); securitatea depinde de cine poate
  purta noul principal; cere o decizie despre cum se identifică un „slot” (același criteriu ca la A).

**C. Sloturile mutate în afara stocării de note.** Sloturile nu mai au `id` de notă sau ies din rădăcinile scanate de `FileStorageEngine`, deci
controlerul nu le mai vede.
- Câștig: nu mai e nimic de păzit în controler; separă definiția ontologiei de memoria de note.
- Se strică: notele `Promoted_*.md` și relațiile din graf trimit la ID-urile `slot-*` (ținte de tip `part_of`), care ar rămâne orfane; `VaultIndex` și
  testele care citesc sloturile (`test_ontology_scaffold.py` și altele) trebuie mutate; e o migrare de date, nu o schimbare de cod local. Atinge și
  scripturile care scriu sloturile (căile din `WRITE_PATHS.md`).

Nimic din controler nu a fost modificat. Bugetul grafului și comportamentul căutării sunt neatinse.
