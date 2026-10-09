# Reverificarea integrității, invalidarea exactă și ancora capătului lanțului (WP3b)

Completează WP3a (lanțuri hash pentru custodie și audit). Cod: `LogAnalyzer.Dfir.Core/Case/CaseWorkspace.Integrity.cs`.

## Ce face

- **Rezultatele în custodie.** Fiecare fișier scris de pipeline sau de un raport/export (Analysis/*, Exports/*, Control/*) are o intrare `output.written` în
  `Logs/chain_of_custody.jsonl` (SHA-256, producător, versiune, probele din care provine), prin `CaseWorkspace.RecordOutput`. Conținutul fișierelor nu se schimbă.
  Excepție: `Analysis/integrity_recheck.json` și `Analysis/invalidations.json` sunt rezultatul reverificării și se rescriu la fiecare deschidere; nu sunt înregistrate.
- **Indexul de dependențe.** `Analysis/dependencies.json` leagă fiecare `FindingId` și fiecare propunere Vault (identificată prin `source_ref`) de probele pe care se sprijină,
  inclusiv probele-părinte (`ParentEvidenceId`) și constatările din care e construit un lanț de incident. Un caz fără acest fișier primește un index derivat din `findings.json`.
- **Reverificarea la deschidere.** `CaseWorkspace.Open` apelează `Recheck()`: rehash pe flux (fără a citi fișierul întreg în memorie, anulabil) al fiecărei probe față de
  `evidence_index.jsonl` și al fiecărui rezultat înregistrat, plus `VerifyChains`. Fișier lipsă = `LIPSĂ`, hash diferit = `MODIFICAT`. Nimic nu se repară, nu se mută, nu se șterge.
  Rezultatul: obiectul `RecheckResult`, `Analysis/integrity_recheck.json`, intrarea de audit `case.recheck`. Un caz creat înainte de WP3 apare ca „lanț neverificabil (caz creat înainte de WP3)”, niciodată ca valid.
- **Invalidare exactă.** O probă MODIFICATĂ sau LIPSĂ marchează `INVALIDATED` (cu motiv și id de probă), în `Analysis/invalidations.json`, exact constatările și propunerile Vault care depind de ea.
  `findings.json` nu se rescrie; celelalte constatări rămân neatinse. `INVALIDATED` este un marcaj separat, nu o valoare nouă a câmpului `Status` (vocabularul de stări standard rămâne neschimbat).
  `VaultExport.Release` (folosit de pipeline) refuză, cu motiv, propunerile invalidate.
- **Ancora capătului lanțului.** Capetele curente ale ambelor lanțuri (`Seq` + hash) se scriu în `Exports/export_manifest.json`, `Control/*/report_manifest.json`, `Analysis/schema_manifest.json`
  și se arată în rezultatul reverificării. `CaseWorkspace.CheckAnchor` verifică o ancoră păstrată în afara cazului.

## Limita, spusă deschis

- Un lanț hash dovedește că intrările existente nu au fost modificate, șterse din mijloc sau reordonate. **Nu** dovedește că nu s-au șters intrări de la sfârșit.
  Ancora detectează asta doar dacă o **copie a ei este păstrată în afara folderului cazului** (alt suport, tichet, listă tipărită) și se compară ulterior.
  O ancoră ținută doar în caz poate fi rescrisă de cine rescrie și lanțul.
- Ancora nu acoperă intrările adăugate după ea: un sfârșit tăiat doar până la intrări mai noi decât ancora nu se vede.
- Cine poate rescrie și `evidence_index.jsonl`, și fișierul probei, și ambele lanțuri, și toate copiile ancorei poate falsifica un caz; nu există semnătură digitală aici.
- Reverificarea raportează; ea nu dovedește autenticitatea probei dinainte de achiziție.
- Dependențele sunt cele declarate de reguli (`SupportingEvidence`); o legătură pe care regula nu a declarat-o nu poate fi invalidată.
