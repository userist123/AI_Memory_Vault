# Triajul executabilelor (master plan 6.7)

**Unde se află:**
- **Cod:** `LogAnalyzer.Dfir.Core/Detection/PeTriage.cs`, apelat de pipeline pentru fiecare probă care începe cu „MZ”.
- **Interfață:** tab-ul „Executabile” din „Investigație completă”.
- **Ieșire:** `Analysis/pe_triage.json`, înregistrat în custodie și recitit la deschiderea cazului.
- **Teste:** `PeTriageTests` (10).

## Ce citește (static, fără să ruleze fișierul)

- **Antetul:** arhitectura (x86/x64/ARM64), executabil sau bibliotecă, subsistemul și momentul compilării. Momentul compilării îl scrie linkerul, poate fi falsificat sau înlocuit de un hash (build-uri reproductibile), iar fișa o spune.
- **Secțiunile:** dimensiunile, entropia (măsurată pe cel mult 16 MB pe secțiune; intervalele identice se măsoară o singură dată) și drepturile X/W.
- **Importurile:** bibliotecile și funcțiile, cu „imphash (ordinale netraduse)”. Imphash-ul diferă de pefile/VirusTotal la importurile prin ordinal din oleaut32, ws2_32 și wsock32, pe care pefile le traduce în nume.
- **Overlay-ul:** datele de după sfârșitul imaginii, fără semnătura Authenticode, dar nu mai puțin decât antetele.
- **Semnătura:** prezența semnăturii încorporate și verdictul Authenticode al stației care analizează (încorporată sau din catalogul stației; fără revocare și fără rețea).
  - Un program adus de pe altă stație poate apărea „fără semnătură digitală”, chiar dacă pe stația lui era semnat prin catalog: hash-ul lui nu este în catalogul acestei stații.
  - Exemplu din laborator: un `notepad.exe` de pe alt build Windows.

## Indicatori (observații, nu verdicte)

Fiecare indicator are motivul lui:

| Cod | Ce înseamnă |
|---|---|
| PE-HIGH-ENTROPY | conținut comprimat sau criptat |
| PE-WX-SECTION | secțiune care poate fi și scrisă și executată |
| PE-FEW-IMPORTS | foarte puține importuri |
| PE-NOTABLE-IMPORTS | importuri folosite și la injecție sau furt de date |
| PE-FUTURE-TIMESTAMP | momentul compilării este în viitor |
| PE-OVERLAY | date după sfârșitul imaginii |
| PE-TLS | funcții TLS |
| PE-IMPORTS-TRUNCATED | tabelă de importuri citită parțial |
| PE-UNSIGNED | fără semnătură încorporată, ceea ce nu înseamnă rău intenționat |

Lista importurilor notabile este în `Analysis/Data/pe_notable_imports.json`, încorporată comprimat. Astfel DLL-ul publicat nu poartă ca text simplu numele pe care le caută listele de detecție; testul `The_compiled_dfir_assembly_does_not_carry_the_command_patterns_of_the_detection_lists` verifică asta.

## Limite de siguranță

Cititorul nu aruncă excepții. Un fișier malformat dă un raport cu „Problem”, iar în pipeline o lacună vizibilă. Limitele sunt:

| Ce se limitează | Limita |
|---|---|
| dimensiunea fișierului | 256 MB |
| secțiuni | 96 |
| biblioteci importate | 512 |
| funcții pe bibliotecă | 2.048 |
| funcții în total | 10.000 |
| lungimea unui nume | 128 de caractere |

O listă de importuri folosită de mai multe biblioteci se citește o singură dată.

**Revizuirea independentă din 2026-11-08:**
- 12.000 de mutații au dat zero excepții;
- a găsit o tabelă de importuri construită special care producea un milion de funcții și un raport de 273 MB, acum limitată și semnalată;
- executabilele malformate dispăreau fără lacună;
- a găsit diferența de imphash;
- a găsit costul entropiei și limita de jos a overlay-ului.

Toate sunt corectate și au teste.

## Verificat în laborator (Arhipelag, LAB-WS01, 2026-11-08)

Investigația a fost făcută din folderul `C:\LA\pe-test`:
- `adb.exe`: x86, semnătură încorporată validă;
- `notepad.exe`: alt build, fără semnătură în catalogul stației;
- `notepad_cu_date_adaugate.exe`: overlay de 20.000 de octeți, compilat „1979” (build reproductibil).
