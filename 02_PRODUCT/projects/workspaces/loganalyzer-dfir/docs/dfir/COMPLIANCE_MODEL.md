# Compliance Model — LogAnalyzer DFIR

Status: **P8 implementat** pentru evaluarea unei politici față de propria ei bază sau față de o mapare de benchmark furnizată
de proprietar, cu export OSCAL assessment-results. Importul de cataloage/profiluri OSCAL nu este implementat.

Cod: `Dfir.Core/Compliance/ComplianceModel.cs`; folosit de `PolicyWorkbench.Assess` și de pagina „Politici”
(„Conformitate față de politică”, „Conformitate față de benchmark…”).

## Lanțul

```
BENCHMARK (nume, versiune, sursă, SHA-256 al mapării)
  └─ CONTROL (id din benchmark, titlu)
       └─ REQUIREMENT (textul cerinței, așa cum îl scrie proprietarul)
            └─ DETECTION (controalele de politică mapate: registry / audit / service / secpol)
                 └─ EVIDENCE (valoarea citită pe stație, starea, eroarea de citire; SHA-256 al planului de citire)
                      └─ RESULT: Satisfied | NotSatisfied | NotAssessed
```

Reguli:

- Un control de benchmark este **Satisfied** doar dacă este mapat și **toate** controalele de politică mapate au fost citite
  și sunt conforme.
- Orice control mapat neconform → **NotSatisfied** (motivul arată setarea, valoarea observată și cea așteptată).
- Fără mapare, sau cu o setare care nu a putut fi citită (de exemplu audit fără drepturi de administrator) → **NotAssessed**.
  O citire eșuată nu este niciodată numărată ca reușită.
- Singura frază de conformitate pe care o produce aplicația este `ComplianceAssessment.Statement`: „Conform cu X” doar
  dacă toate controalele benchmark-ului sunt mapate, citite și satisfăcute; altfel „NU se poate declara conformitatea cu X”,
  cu numerele exacte. Nu există „CIS compliant” fără o mapare CIS furnizată de proprietar, și nici atunci fără toate
  controalele satisfăcute.

## Maparea benchmark-ului (furnizată de proprietar)

Aplicația nu conține texte de benchmark (CIS, STIG etc.) și nu ghicește corespondențe. Proprietarul scrie maparea:

```yaml
benchmark: { name: Benchmark intern, version: '1.0', source: 'document intern' }
controls:
  - id: '1.1'
    title: Blocare ecran
    requirement: Ecranul se blochează.
    policy_controls: [R1]
```

Chei necunoscute, id-uri duplicate, lipsa numelui/versiunii sau o referință la un control care nu există în politică sunt
erori. Fără mapare, politica este propria ei bază: câte o cerință pentru fiecare control (`BenchmarkLoader.FromPolicy`).

## Probe și OSCAL

`ComplianceAssessment.Save` scrie în `evidence/compliance/`:

- `<timp>-<id>.assessment.json` — evaluarea completă (benchmark, politică și hash-urile lor, hash-ul planului, stația, ora,
  fiecare observație);
- `<timp>-<id>.oscal-ar.json` — OSCAL assessment-results (modelul 1.1.2);
- câte un `.sha256` pentru fiecare.

Structura OSCAL: un `result` cu `reviewed-controls` = controalele benchmark-ului; câte o `observation` per control de
politică citit (metoda `TEST`, subiectul = stația, `relevant-evidence` = fișierul de evaluare și SHA-256-ul lui); câte un
`finding` per control evaluat, cu `target.type = objective-id`, `target-id` = id-ul din benchmark, starea
`satisfied`/`not-satisfied` și legături `related-observations`. Controalele neevaluate nu primesc finding și sunt
enumerate în `remarks`. UUID-urile sunt deterministe (SHA-1 peste nume, biții de versiune 5 și variantă setați, fără UUID
de spațiu de nume).

Abateri cunoscute de la un pachet OSCAL complet:

- nu există un plan de evaluare OSCAL: `import-ap.href` indică politica (`policy:<id>@<versiune>#sha256=…`), lucru scris și
  în `remarks`;
- fișierul **nu a fost validat cu schema oficială NIST** (schema nu este inclusă și nu s-a descărcat nimic); testele verifică
  structura, legăturile finding → observation, formatul și unicitatea UUID-urilor și hash-urile fișierelor.

## Rezultat real (2026-10-05, doar citire)

Cele trei GPO-uri furnizate de proprietar, evaluate față de propria lor bază pe stația de dezvoltare (fără drepturi de
administrator, stație care nu este membră a domeniului pentru care au fost scrise):

| GPO | satisfăcute | nesatisfăcute | neevaluate |
|---|---|---|---|
| Intranet AppLocker W11 | 0 | 27 | 0 |
| Intranet Computer W11 | 81 | 513 | 43 (40 audit fără privilegiu, eroarea 1314; 3 chei HKLM cu acces refuzat) |
| Intranet User W11 | 0 | 7 | 0 |

Rezultatul descrie această stație, nu calitatea politicilor: ele nu sunt aplicate aici.
