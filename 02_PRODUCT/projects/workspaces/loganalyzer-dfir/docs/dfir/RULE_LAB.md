# Laboratorul de reguli (master plan 6.8)

**Unde se află:**
- **Cod:** `LogAnalyzer.Dfir.Core/Detection/RuleLab.cs`.
- **Interfață:** tab-ul „Laborator de reguli” din „Investigație completă”.
- **Teste:** `RuleLabTests` (5), `SigmaCorrelationTests` (corelarea Sigma, secțiunea de mai jos).

## Ce face

- **Testează pe acest caz.** O regulă Sigma, YARA sau IOC se încarcă exact cum o încarcă motorul de detecție: același parser, aceleași erori. Apoi rulează pe cronologia cazului, iar o regulă YARA rulează și pe fișierele probelor (cel mult 64 MB fiecare).
  - O regulă pe care motorul ar refuza-o este refuzată cu motivul parserului, de exemplu „modificatorul base64offset nu este acceptat”.
  - Regulile de corelare Sigma 2.x (`correlation:`) se testează ca regulile de bază, împreună cu regulile de bază din același text (documente separate prin `---`; laboratorul nu are alte fișiere). Ce nu este acceptat este refuzat cu motivul, vezi „Corelarea Sigma”.
- **Câmpuri pe care cazul nu le are.** Dacă regula Sigma folosește un câmp pe care niciun eveniment al cazului nu-l are, rezultatul o spune lângă „0 potriviri”. Un caz redeschis își citește cronologia din `timeline.csv`, fără câmpurile parserelor; analistul trebuie să reanalizeze cazul (6.16) și să testeze din nou.
- **Păstrează ca referință / Compară cu referința.** Diferența dintre două versiuni ale regulii se face pe potriviri (regulă + probă + locator): potriviri noi, pierdute și păstrate.
- **Adaugă în regulile cazului.** Se adaugă numai o regulă testată, care s-a încărcat. Ea este scrisă în `Rules/<sigma|yara|ioc>/<nume>` din caz, auditată (`rule.added_to_case`) și înregistrată în custodie, iar următoarea analiză a cazului o folosește.
  - O versiune nouă primește alt nume și nu suprascrie o regulă existentă, ca analizele anterioare să rămână explicabile.
  - Numele care conțin o cale sunt refuzate, nu tăiate.

## Verificat în laborator (Arhipelag, LAB-WS01, cazul cu 50.943 de evenimente, 2026-11-08)

1. Prima versiune, `EventID: 4625` + `TargetUserName: Administrator`, a dat 0 potriviri. Așa s-a găsit lipsa câmpurilor din cazul redeschis; acum laboratorul o spune.
2. Versiunea a doua, doar `EventID: 4625`, comparată cu referința: 1.322 de potriviri noi, 0 pierdute.

## Corelarea Sigma (specificația Sigma 2.x)

**Cod:** `LogAnalyzer.Dfir.Core/Detection/SigmaCorrelation.cs`. Documentele de corelare stau în aceleași foldere `sigma/*.yml` ca regulile de bază, fie în același fișier (separate prin `---`), fie în alt fișier. Se rezolvă după încărcarea tuturor fișierelor și rulează după regulile de bază.

**Formă acceptată**

```yaml
title: Parole încercate în rafală
id: corr-brute
name: brute_force          # opțional
level: high
correlation:
  type: event_count        # event_count | value_count | temporal | temporal_ordered
  rules: [logon_failed]    # nume (name:) sau id ale regulilor de bază
  group-by: [TargetUserName]
  timespan: 5m             # Ns, Nm, Nh, Nd
  condition: { gte: 10 }   # gt, gte, eq (value_count: și field: IpAddress)
```

**Semantică (deterministă)**

- **Ferestre glisante.** Câte o fereastră ancorată la fiecare eveniment: `[t, t + timespan]`, ambele capete incluse (un eveniment exact la `timespan` după ancoră este în fereastră). Evenimentele se ordonează după (ora UTC, apoi id probă, apoi locator).
- **Grupare.** `group-by` compară valorile fără să țină seama de majuscule. Ca în implementările Sigma, un eveniment fără valoare pentru un câmp din `group-by` nu intră în niciun grup. Câte evenimente au rămas pe dinafară se spune într-o notă. Un caz redeschis nu are câmpurile parserelor: fără nota asta, toate conturile ar fi căzut într-un singur grup. Fără `group-by` există un singur grup.
- **event_count.** Numărul de evenimente distincte potrivite de regulile referite în fereastră, per grup, trebuie să satisfacă condiția. Un eveniment potrivit de două reguli referite se numără o dată.
- **value_count.** Numărul de valori distincte (fără majuscule) ale `condition.field` în fereastră. Evenimentele fără câmpul respectiv nu se numără și apar ca lipsă (gap).
- **temporal.** Fiecare regulă referită s-a potrivit cel puțin o dată în fereastră, în orice ordine (cel puțin două reguli). Nu are `condition`.
- **temporal_ordered.** Regulile s-au potrivit în ordinea din listă; fereastra începe la primul eveniment al lanțului, iar fiecare pas este strict mai târziu decât cel dinainte (la ore egale ordinea nu se poate dovedi, deci nu se potrivește).
- **Un rezultat per rulare maximală.** Ferestrele satisfăcătoare care se suprapun, în același grup, se unesc într-un singur rezultat cu toate evenimentele care le susțin. Fereastra raportată merge de la primul la ultimul eveniment susținător; numărul este maximul dintre ferestre (event_count, value_count) sau numărul evenimentelor susținătoare (temporal, temporal_ordered).
- **Rezultatul.** `DetectionResult` cu tipul `Correlation`, identitatea regulii (`SIGMA-CORR:<id|name|fișier>`, versiune, SHA-256 al documentului), clasificare „corelat”. Proba și locatorul sunt ale primului eveniment susținător; `Match` conține grupul, fereastra, numărul și referințele `probă|locator` ale tuturor evenimentelor (cel mult 200, apoi „listă tăiată”).
- **Ora necunoscută.** Evenimentele fără oră sunt excluse din corelare, iar excluderea apare ca lacună (`EvidenceGap`, stare Partial) lângă rezultate, niciodată pe tăcute.
- **Limite.** Cel mult 10.000 de evenimente luate în calcul per grup (cele mai vechi) și 1.000 de rezultate per corelare; peste ele apare o lacună.
- **Regulile de bază raportează mereu.** `generate` se acceptă (true/false) dar nu are efect: potrivirile regulilor de bază nu sunt ascunse niciodată, ca o potrivire să nu dispară din raport doar fiindcă o corelare o folosește.
- **Reguli de bază expirate.** Dacă o regulă referită a depășit timpul la regex, apare o lacună: corelarea poate fi incompletă.

**Refuzat la încărcare (eroare de încărcare, lacună `Failed`, regula nu se aplică)**

- `lt`, `lte` și `eq: 0`: „mai puțin de N” sau absența nu este dovadă pe un caz care poate fi incomplet.
- Orice alt operator, `aliases`, un tip necunoscut, `timespan` lipsă sau cu altă formă decât Ns/Nm/Nh/Nd, chei necunoscute în `correlation`, `condition` la temporal, `field` în afara value_count.
- O regulă referită care nu există sau este ambiguă (același nume sau id la mai multe reguli diferite).
- **Înlănțuirea:** o corelare care referă altă corelare este refuzată.

## Limite

- **Ce nu există încă:** fluxul de aprobare a regulilor pentru toată stația (pachete de reguli semnate) și raportul de fals pozitive pe mai multe cazuri. Pachetele semnate pentru stație sunt cele din 6.12.
- **Corelarea:** regulile Sigma de corelare au semantica din secțiunea „Corelarea Sigma”; regulile de secvență ale aplicației rămân separate.

- **Limite de încărcare (revizuire 2026-11-08):** `timespan` are cel mult 366 de zile. Un document de corelare acceptă doar cheile cunoscute: `action`, `scope` și celelalte chei necunoscute sunt refuzate. Separatorul `---` poate fi urmat de un comentariu. În laboratorul de reguli, o notă parțială (evenimente fără câmp, o limită atinsă) apare ca ATENȚIE și nu refuză regula.
- **Reziduu acceptat:** două reguli de bază fără `id` în același fișier au același RuleId (numele fișierului). Amprentele lor diferă. Dați fiecărei reguli un `id`.
