# Laboratorul de reguli (master plan 6.8)

**Unde se află:**
- **Cod:** `LogAnalyzer.Dfir.Core/Detection/RuleLab.cs`.
- **Interfață:** tab-ul „Laborator de reguli” din „Investigație completă”.
- **Teste:** `RuleLabTests` (5).

## Ce face

- **Testează pe acest caz.** O regulă Sigma, YARA sau IOC se încarcă exact cum o încarcă motorul de detecție: același parser, aceleași erori. Apoi rulează pe cronologia cazului, iar o regulă YARA rulează și pe fișierele probelor (cel mult 64 MB fiecare).
  - O regulă pe care motorul ar refuza-o este refuzată cu motivul parserului, de exemplu „modificatorul base64offset nu este acceptat”.
  - Regulile de corelare Sigma (`correlation:`) sunt refuzate explicit, nu degradate în tăcere.
- **Câmpuri pe care cazul nu le are.** Dacă regula Sigma folosește un câmp pe care niciun eveniment al cazului nu-l are, rezultatul o spune lângă „0 potriviri”. Un caz redeschis își citește cronologia din `timeline.csv`, fără câmpurile parserelor; analistul trebuie să reanalizeze cazul (6.16) și să testeze din nou.
- **Păstrează ca referință / Compară cu referința.** Diferența dintre două versiuni ale regulii se face pe potriviri (regulă + probă + locator): potriviri noi, pierdute și păstrate.
- **Adaugă în regulile cazului.** Se adaugă numai o regulă testată, care s-a încărcat. Ea este scrisă în `Rules/<sigma|yara|ioc>/<nume>` din caz, auditată (`rule.added_to_case`) și înregistrată în custodie, iar următoarea analiză a cazului o folosește.
  - O versiune nouă primește alt nume și nu suprascrie o regulă existentă, ca analizele anterioare să rămână explicabile.
  - Numele care conțin o cale sunt refuzate, nu tăiate.

## Verificat în laborator (Arhipelag, LAB-WS01, cazul cu 50.943 de evenimente, 2026-11-08)

1. Prima versiune, `EventID: 4625` + `TargetUserName: Administrator`, a dat 0 potriviri. Așa s-a găsit lipsa câmpurilor din cazul redeschis; acum laboratorul o spune.
2. Versiunea a doua, doar `EventID: 4625`, comparată cu referința: 1.322 de potriviri noi, 0 pierdute.

## Limite

- **Ce nu există încă:** fluxul de aprobare a regulilor pentru toată stația (pachete de reguli semnate) și raportul de fals pozitive pe mai multe cazuri. Pachetele semnate pentru stație sunt cele din 6.12.
- **Corelarea:** se face cu regulile de secvență ale aplicației, nu cu regulile Sigma de corelare.
