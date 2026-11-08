# Pachete offline de informații despre amenințări (master plan 6.12)

**Unde se află:**
- **Cod:** `LogAnalyzer.Dfir.Core/Detection/ThreatIntelPackages.cs`. Pe stație îl leagă `LogAnalyzer.App/Services/ThreatIntelFlows.cs`.
- **Interfață:** pagina „Politici”, secțiunea „Pachete de informații despre amenințări (offline, semnate)”.
- **Teste:** `ThreatIntelPackagesTests` (12, unul cu pipeline-ul real), `ThreatIntelRealCmsTests` (semnătură CMS reală) și `ThreatIntelPackagesViewModelTests`.

## Modelul de încredere

Un pachet CERT (liste IOC `ioc/*.csv`, reguli YARA `yara/*.yar`, reguli Sigma `sigma/*.yml`) ajunge pe stație pe un mediu aprobat. Ofițerul de securitate al stației îl semnează pentru stație.

Pachetul conține:
- `ti_package.json`: format, identificator, versiune, amprenta versiunii anterioare, data emiterii, valabilitatea și lista fișierelor cu SHA-256;
- `ti_package.json.p7s`: CMS detașat, calculat peste octeții exacți ai manifestului.

Pachetul se acceptă numai dacă sunt îndeplinite toate condițiile:
1. manifestul este valid (identificator din litere, cifre, `. _ -`; căi doar `ioc|yara|sigma/<nume>.<ext>`);
2. semnătura corespunde manifestului;
3. certificatul semnatarului are nerepudiere, lanț până la o ancoră importată și nu este revocat (aceleași reguli ca la registrul de medii semnat);
4. semnatarul este pe lista ofițerilor de securitate ai stației;
5. fiecare fișier are amprenta din manifest și **nu există niciun alt fișier**;
6. versiunea continuă cea mai nouă versiune aprobată vreodată (anti-revenire).

## Ciclul

1. **Pune în așteptare:** se copiază doar fișierele listate, iar verificarea se face pe copie. Un pachet refuzat nu rămâne nicăieri, iar refuzul intră în istoric.
2. **Aprobă și activează:** o face un administrator, cu motiv. Copia din așteptare se verifică din nou, pentru că s-ar fi putut schimba între timp. O aprobare făcută de aceeași persoană care a pus pachetul în așteptare este permisă (stație cu un singur analist), dar se notează.
3. **Revino la versiunea:** o face un administrator, cu motiv, și se poate reveni doar la o versiune aprobată anterior pe stație. Revenirea nu coboară pragul anti-revenire: o versiune veche nu se poate importa din nou.
4. **Folosirea în analiză:** la fiecare rulare se reverifică totul: pointerul activ față de istoric, semnătura, semnatarul, amprentele și absența fișierelor nelistate.
   - Motorul de detecție citește un instantaneu privat, scris din octeții verificați în timpul copierii, niciodată folderul depozitului.
   - Un pachet care nu se mai verifică nu se folosește. Analiza primește atunci o lacună „Pachetul de informații despre amenințări”.
   - Un pachet expirat se folosește, cu o lacună parțială care o spune.
   - Rularea înregistrează `pachet_ti = <id> v<n>` în manifestul rulării (6.16), așa că o schimbare de pachet explică diferențele dintre rulări.

## Depozitul

- **Locație:** `%ProgramData%\LogAnalyzer\threat_intel` (`Staging/`, `Versions/`, `active.json`, `history.jsonl`).
- **Drepturi:** la fiecare folosire se verifică drepturile folderului, adică proprietar și scriere doar pentru Administrators și SYSTEM. Dacă aplicația rulează ca administrator, folderul se restricționează. Un depozit cu drepturi neconfirmate nu se folosește deloc.
- **Istoricul:** este un `HashChain` sigilat cu cheia de mașină (DPAPI, doar administratori), ca auditul autentificării. Tăierea capătului istoricului rupe lanțul. Fără cheie (aplicația nu rulează ca administrator), pagina spune „cheia de sigilare … nu poate fi citită”, nu „compromis”.

## Revizuire independentă (2 runde, 2026-11-08)

**Runda 1** a găsit două defecte HIGH:
- un fișier de reguli nelistat, pus în folderul versiunii, era încărcat fără verificare;
- depozitul nu avea ACL.

A mai găsit:
- trunchierea istoricului;
- TOCTOU între verificare și încărcare;
- un pachet nesemnat cu `files: null` care arunca excepție;
- un ID de pachet care arunca excepție la mutarea folderului;
- autorizarea făcută doar în interfață;
- expirarea neverificată la încărcare.

**Runda 2** a găsit:
- ACL aplicat doar folderului nou;
- instantanee nesterse;
- un mesaj fals de „compromis” pentru un proces fără drepturi de administrator.

Toate sunt corectate și au teste.

**Reziduu acceptat (decizia sesiunii principale, de confirmat cu proprietarul):** cine are drept de scriere de administrator poate restaura o stare veche, sigilată, completă (istoric, sigiliu și pointer), sau poate șterge tot depozitul. Sigiliul nu are contor monoton. Remediul ar fi o ancoră externă, de exemplu în auditul autentificării, a celei mai mari versiuni aprobate.

## Verificat în laborator (2026-11-08)

- **Insula, LAB-WS02:** un pachet TEST cu semnătură invalidă e refuzat cu motivul „semnătura nu corespunde manifestului” și liniile verificării. Nu e pus în așteptare și nu e folosit.
- **Calea pozitivă:** e verificată cu semnătură CMS reală în `ThreatIntelRealCmsTests`. Laboratorul nu are încă un ofițer de securitate cu certificat de test, deci nu e verificată și în laborator.
