# Confidențialitatea datelor de test

Regula: nimic despre calculatorul sau cazurile reale ale unei persoane nu intră în repository (depozitul este public).

Nu se pun în fișiere urmărite de git:

- numele stației, numele utilizatorului, căi din profilul utilizatorului;
- SID-uri ale conturilor locale, numere de serie ale dispozitivelor (USB, discuri);
- nume de fișiere, servicii, directoare sau identificatori de caz dintr-un incident real;
- conținut copiat din rapoarte primite de la o persoană sau de la o instituție.

În teste și documente se folosesc valori neutre: stație `WS-01`, utilizator `user` sau `analyst`, profil `C:\Users\user\...`,
SID `S-1-5-21-1111111111-2222222222-3333333333-1001`, serii fictive (`AA00BB11CC22DD33EE44FF55`), nume generice de fișiere
(`vendor_helper.cmd`, `setup_x.msi`). Corpusul real rămâne local și se indică prin variabila de mediu `LADFIR_CORPUS`.

## Lista locală și testul de gardă

Testul `PrivateIdentifiersGuardTests` (în `LogAnalyzer.UI.Tests`) citește o listă de identificatori privați dintr-un fișier
LOCAL, în afara repository-ului: `%APPDATA%\ai-memory-vault\private_identifiers.txt` (câte unul pe linie; liniile care
încep cu `#` sunt comentarii; calea poate fi schimbată cu `LA_PRIVATE_IDENTIFIERS`). Testul eșuează dacă un fișier text al proiectului
conține unul dintre ei (fără deosebire între litere mari și mici) și arată doar numărul de ordine al identificatorului, nu valoarea.

Dacă fișierul lipsește (CI, altă stație), testul se încheie fără eroare și scrie în ieșire că garda a fost sărită.
Lista nu se pune niciodată în repository; fiecare persoană care lucrează cu date reale își ține propria listă.
