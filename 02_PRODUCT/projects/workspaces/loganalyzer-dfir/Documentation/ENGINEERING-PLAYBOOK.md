# Engineering Playbook — lecții din LogAnalyzer

Decizii și pattern-uri reutilizabile. Fiecare regulă are un motiv și o consecință. Documentul a fost recuperat din LogAnalyzer.UI 137a626 și completat cu lecțiile din platforma DFIR.

## Desktop .NET / WPF

1. **WPF nu este compatibil cu Native AOT și nici cu trimming agresiv.** Pentru export se folosește self-contained + single-file, cu `PublishTrimmed=false`.
2. **SQLCipher în single-file cere `IncludeNativeLibrariesForSelfExtract=true`.** Altfel bibliotecile native lipsesc din bundle.
3. **Self-contained pentru stații fără internet sau fără runtime .NET.**
4. **Fișierele de date (Categories JSON) se verifică explicit în output-ul de publish**, cu un pas dedicat în CI.
5. **`.pdb`-urile proiectelor referite ajung în publish chiar cu `DebugType=none`.** Profilul setează `AllowedReferenceRelatedFileExtensions=.none`.

## Arhitectură

6. **Un proiect per folder, cu soluția `.slnx` ca sursă de adevăr.** Proiectele de la rădăcină cu `Compile Remove` produc coliziuni și dubluri.
7. **Nu duplica servicii între layere.** O singură sursă de adevăr, referită prin ProjectReference. Exemplu: generatoarele de licențe și aplicația folosesc același `LicenseService`. Când aveau implementări separate, Hardware ID-ul diferea.
8. **God objects** (`MainWindow.xaml`, `MainViewModel`) se sparg pe UserControl-uri și ViewModel-uri per tab.

## DFIR

9. **O probă care lipsește nu este o probă goală.** Statusurile sunt explicite: SUCCESS / EMPTY / FAILED / NOT_AVAILABLE / PARTIAL / SKIPPED_BY_DESIGN.
10. **Timestamp-urile nu primesc niciodată „acum” ca valoare implicită.** Un timestamp lipsă rămâne lipsă și se marchează ca atare.
11. **Parserele se validează pe artefacte reale, nu pe mock-uri.** Fațadele cu buffer zero sau `DateTime.Now` trec testele unitare, dar nu produc nimic.
12. **Excepțiile nu se înghit.** Ele devin FAILED / PARTIAL în rezultatul parserului; `catch {}` gol este interzis.
13. **Corpusul real nu intră niciodată în repository.** Testele îl citesc prin `LADFIR_CORPUS` și fixture-uri de tip `targets.json`.

## Securitate aplicată

14. **Licențiere offline**: ținta este semnătura asimetrică (cheia publică inclusă în aplicație, cheia privată offline). Hash-ul cu salt partajat oprește doar copierea ocazională.
15. **DPAPI pentru secrete per utilizator, SQLCipher pentru datele la rest.**
16. **Chain of custody**: jurnal append-only, SHA-256 la intake, probe read-only.
17. **Controalele de aplicație nu înlocuiesc certificarea.** Limita se documentează explicit.

## CI/CD și release

18. **Un build fără teste nu este un gate.** Pipeline-ul complet: restore → build → test → publish → artifact.
19. **Actions fixate pe SHA, `contents: read` și `timeout-minutes` pe fiecare job** (`workflow_security_audit.py`).
20. **Release manual la început.** Automatizarea vine după validarea pe mașini curate.

## Proces

21. **Documentația și codul derivă.** Orice afirmație din documentație se verifică în cod înainte de release.
22. **Când aplicația există în mai multe copii, compară-le pe toate înainte de a consolida.** Folosește hash pe fișier și clasifică diferențele: identic, înlocuit de o versiune nouă, unic.
