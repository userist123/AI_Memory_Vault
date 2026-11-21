# Cronologie: marcaje, note, vederi salvate și comparare (master plan 6.9)

Codul este în `LogAnalyzer.Dfir.Core/Case/TimelineNotebook.cs`. Interfața este tab-ul „Cronologie” din „Investigație completă”. Testele sunt `TimelineNotebookTests` (7) și `TimelineNotesViewModelTests`.

## Ce face

- **Filtre:** text (rezumat, cale, proces, utilizator, IP, DNS sau ID-ul exact al evenimentului), sursă și interval în UTC. „Doar marcate” arată numai evenimentele marcate. Limitele intervalului sunt incluse. O dată fără oră înseamnă începutul zilei pentru „de la” și sfârșitul ei pentru „până la”. „Până la 10:30” cuprinde tot minutul 10:30. Un eveniment fără oră UTC nu intră într-un interval, pentru că nu se ghicește când a avut loc.
- **Dată greșită:** dacă o dată nu poate fi citită, se renunță doar la interval și pagina o spune. Sursa și „doar marcate” rămân aplicate.
- **Marcaje și note:** se pun pe evenimentul ales. Un marcaj poate avea o etichetă. Nota este a analistului, nu o probă: evenimentul nu se modifică. Coloana „★” arată ★ pentru eveniment marcat și ✎ pentru eveniment cu note.
- **Vederi salvate:** filtrul curent se salvează cu un nume, iar „Aplică vederea” îl reface. Dacă sursa salvată nu mai există după o reanaliză, pagina o spune și arată toate sursele.
- **Compararea a două vederi:** pagina arată câte evenimente are fiecare vedere, câte sunt comune și câte apar doar într-una.

## Identitatea evenimentului

Cheia unui eveniment este SHA-256 din:
- ID-ul probei;
- locatorul din probă (înregistrare, rând, cadru);
- sursa;
- ID-ul evenimentului;
- ora UTC, sau textul brut al orei când nu există oră UTC;
- rezumatul, numai când parserul nu a dat un locator.

Un marcaj supraviețuiește unei reanalize care renumerotează sau reordonează rândurile. Două evenimente fără locator, cu aceeași probă, sursă, ID, oră și rezumat, au aceeași cheie și deci același marcaj. Parserele verificate dau locatori unici pe înregistrare.

## Jurnalul

- **Unde se scrie:** în `Logs/timeline_notes.jsonl`, un `HashChain` în care doar se adaugă.
- **Copia în audit:** fiecare scriere se copiază în auditul cazului ca `timeline.note.*`, un prefix propriu.
  - Revizuirea din 2026-11-08 a găsit că acțiunea existentă `timeline.export_masked` era numărată ca notă. Un export mascat bloca astfel notițele, cu o alarmă falsă de trunchiere.
  - Testul `Other_timeline_audit_actions_such_as_a_masked_export_do_not_count_as_notes` acoperă cazul.
- **Jurnal compromis:** un jurnal lipsă sau mai scurt decât copia din audit este marcat compromis. Din acel moment nu se mai scrie nimic în el.
- **Caz doar pentru citire:** refuză orice scriere.
- **Ștergerea unei vederi:** o scoate din listă, dar rămâne în jurnal. O salvare cu același nume înlocuiește vederea, iar ambele versiuni rămân în jurnal.

## Verificat în laborator (2026-11-08)

- **Arhipelag, LAB-WS01, cazul cu rafala de parole S1:**
  - filtru 4625, cu 1.347 de evenimente;
  - marcaj și notă pe încercarea asupra contului Administrator (★✎);
  - vederea „Parole greșite 4625” salvată;
  - „Doar marcate” arată 1 eveniment;
  - comparația „Marcate” cu „Parole greșite 4625”: comune 1, doar în a doua 1.346.
- **Continent, LAB-WS01, același caz:** vederea și marcajul salvate din Arhipelag apar identic.
- **Insula, LAB-WS02:** marcaj cu etichetă pe un serviciu USB; panoul arată cine l-a marcat și când.

## Limite

- Notele analistului ajung și în auditul cazului, cu textul lor. La un export pentru terți se maschează la fel ca restul auditului.
- Nu există încă benzi grafice pe mai multe piste sau o bandă de incertitudine desenată. Incertitudinea orei rămâne în coloanele și fișa evenimentului (`TimeFacts`).
