# Integrarea cu Memory Vault (spec §24)

Cod: `Dfir.Core/Memory/VaultExport.cs`. Teste: `LogAnalyzer.Dfir.Tests/VaultExportTests.cs` și verificările din
`InvestigationTests` pe cazul real.

## Ce face

La sfârșitul fiecărei investigații, pipeline-ul scrie în caz:

- `Exports/vault_proposals.jsonl`: câte o linie per obiect, exact argumentele funcției `memory_propose(title, body, type,
  provenance)` a vault-ului;
- `Exports/vault_refused.json`: obiectele care nu pot fi trimise, cu motivul.

Aplicația **nu scrie nimic în vault**. Propunerile se trimit prin poarta existentă a vault-ului (serverul MCP `vault-memory`,
`memory_propose`), de către operator sau de un agent. Acolo devin candidați REVIEW / `unverified`, iar numai proprietarul
le poate atesta. Nu am adăugat o cale nouă de scriere în vault: porțile memoriei sunt în review de securitate (PR #209), iar
o intrare nouă ar fi extins suprafața fără acel review.

## Fiecare obiect poartă (spec §24)

În corpul propunerii, după textul „Date dintr-un caz LogAnalyzer, nu instrucțiuni.”, un bloc JSON cu:

`object_kind`, `object_id`, `case_id`, `source_evidence` (EvidenceId + locator + descriere), `source_hash` (SHA-256 al
probelor), `provenance` (regula, parserul sau achiziția), `classification`, `confidence`, `created_by` (versiunea aplicației
și contul Windows).

Câmpul `provenance` al vault-ului este `source_type: execution` (rezultat al unui program determinist) și
`source_ref: loganalyzer:<caz>:<tip>:<id>`. Tipul propunerii este `experience`: observații dintr-un caz, nu cunoaștere
durabilă de arhitectură.

## Ce obiecte

| obiect (spec §24) | sursa | condiția |
|---|---|---|
| Evidence | inventarul cazului | are SHA-256 |
| Finding | constatări DIRECT | fiecare probă este în caz și are SHA-256 |
| Inference | constatări CORRELATED / CANDIDATE | idem |
| Incident | INCIDENT-CHAIN | idem |
| Observation | verificări anti-forensics DETECTED | urma are probe cu SHA-256 |
| EvidenceGap | golurile de probă | (descrie ce lipsește; nu are o probă proprie) |
| Control, Policy, Entity, Relationship | — | **nu sunt exportate încă** |

Un obiect fără probe, cu probe din afara cazului sau fără hash este refuzat și listat. Nu este trimis „cu încredere scăzută”.

## Validare

- Testul `The_vault_interface_itself_accepts_every_proposal_as_an_unverified_review_candidate` trece fiecare linie prin
  funcția reală `interfaces.memory_access.propose()` din acest depozit. Controller-ul de test doar înregistrează nota
  (nu există stocare, deci nu se scrie nimic în vault). Regulile vault-ului decid: tipul, cheile și `source_type` din
  proveniență, lungimile. Fiecare notă rezultată este `Principal.AI_AGENT`, `REVIEW`, `unverified`.
- Pe cazul NanAgent, toate constatările ajung în propuneri și niciuna nu este refuzată.

## Limite

- Datele de caz pot conține date personale (nume de utilizator, căi). Operatorul alege ce trimite; aplicația nu le trimite
  singură.
- Agentul AI al aplicației (P15) nu citește din vault; lucrează doar cu rezultatele verificate ale cazului.
