# Politica de securitate

## Raportarea vulnerabilităților

Nu deschide issue-uri publice pentru vulnerabilități. Contactează direct maintainerul și include versiunea afectată, pașii de reproducere și impactul estimat.

## Modelul de securitate al aplicației

- **Date la rest**: bază de date criptată cu SQLCipher; secretele sunt protejate cu Windows DPAPI, per utilizator Windows.
- **Integritatea probelor**:
  - SHA-256 la intake;
  - probele brute sunt marcate read-only;
  - chain of custody append-only (CSV + JSONL) și jurnal de audit per caz;
  - golurile de probă sunt raportate explicit (NOT_AVAILABLE / FAILED / PARTIAL), nu ascunse.
- **Licențiere offline**:
  - cheile sunt legate de Hardware ID-ul stației;
  - schema actuală (hash cu salt inclus în binar) oprește doar copierea ocazională;
  - trecerea la semnătură asimetrică este planificată (`Documentation/MVP-DECISIONS.md`, decizia 9);
  - instrumentele de emitere (`LogAnalyzer.LicenseManager`, `LogAnalyzer.KeyGen`, `Generate-LicenseKey.ps1`) nu se livrează clienților;
  - fișierele `license.lic` nu se includ în pachete (CI-ul verifică acest lucru).
- **CI/CD**:
  - actions fixate pe SHA;
  - permisiuni `contents: read`;
  - artefacte păstrate 30 de zile;
  - scanare de secrete înainte de release, cu ghidurile vault-ului (`30_SCRIPTS/verification/`).

## Limitări cunoscute

- Controalele oferă integritate și trasabilitate la nivel de aplicație. Nu reprezintă certificare ORNISS, ISO/IEC 27037, Common Criteria sau un mecanism juridic automat de inalterabilitate.
- Executabilul nu este semnat Authenticode, așa că SmartScreen poate afișa un avertisment la prima rulare.
- DPAPI leagă secretele de contul Windows curent. Mutarea pe alt cont sau pe altă stație cere reactivare.
- Hardware ID-ul depinde de procesor și de placa de bază. Dacă una dintre ele se schimbă, licența trebuie reemisă.

## Versiuni suportate

| Versiune | Suport |
|----------|--------|
| 1.0.0-mvp | MVP: remedieri pe baza efortului rezonabil |
