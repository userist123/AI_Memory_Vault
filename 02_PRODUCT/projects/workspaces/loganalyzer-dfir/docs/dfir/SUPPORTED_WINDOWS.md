# LogAnalyzer - versiuni Windows susținute și lista de laborator

Sursa: `WINDOWS_TOOLING_COMPAT.md` (partea 3). Lista de versiuni este cea decisă de proprietar (deciziile 15 și 32, `CONTRACT_AUDIT_STAGE1.md` §8). Nimic de aici nu a fost
testat încă pe mașini reale: coloana "Stare" spune ce s-a verificat.

## Pachetul este self-contained

Ambele ediții (`LogAnalyzer.exe` = P2/P3, `LogAnalyzer.Classified.exe` = P1) se publică `win-x64`, `SelfContained`, single-file
(`Properties/PublishProfiles/win-x64-singlefile.pubxml`). Nu cer .NET instalat. Ele NU includ componentele sistemului de operare:
`wevtapi.dll`, `esent.dll`, `FirewallAPI.dll`, WMI, Windows PowerShell 5.1. Acestea există pe versiunile de mai jos.

`LogAnalyzer.exe --self-test [--self-test-out=<fișier>]` încarcă toate assembly-urile, creează și citește o bază SQLCipher,
randează un PDF cu QuestPDF și rulează de două ori pipeline-ul determinist pe un corpus mic încorporat (rezultate identice).
Cod de ieșire 0 = totul a mers. CI rulează comanda pe executabilul publicat, fără .NET în PATH și cu traficul de ieșire blocat.

## Versiuni

| Nivel | Versiuni | Stare |
|---|---|---|
| Țintă completă | Windows 11 23H2 / 24H2 / 25H2; Windows 10 LTSC 2019 / 2021 (inclusiv IoT); Windows Server 2022 / 2025 (Desktop Experience) | declarată; neverificată în laborator |
| Țintă extinsă (best-effort) | Windows Server 2016 / 2019 (Desktop Experience); Windows 10 22H2 ("legacy", suportul Microsoft Home/Pro a încetat la 14.10.2025); Windows 10 LTSB 2016 | declarată; neverificată |
| Nesusținute | Server Core / Nano (WPF cere Desktop Experience, de confirmat), Windows 10 sub 22H2, Server 2012 / 2012 R2 | - |

CI rulează doar pe `windows-latest` (self-test al executabilului publicat). Restul matricei cere laborator.

## Ce nu depinde de PATH sau de `wmic`

- Nu există nicio utilizare `wmic` în aplicație sau în scriptul livrat.
- Export evtx: `EvtExportLog` (wevtapi) în proces; canalul absent se raportează "indisponibil pe acest sistem", nu ca eroare.
- Politica de audit (citire): `AuditQuerySystemPolicy`, independent de limbă. Activarea auditului de eșec (doar ediția neclasificată,
  la cererea operatorului) rămâne `auditpol.exe /set` cu calea completă.
- Izolare stație / blocare IoC: Windows Firewall COM (`HNetCfg.FwPolicy2`); `netsh.exe` rămâne rezervă explicită (`NetshRunner`).
- Colectarea de audit: `Scripts\AuditCollector.ps1` livrat lângă executabil (`#Requires -Version 5.1`), pornit din
  `%WINDIR%\System32\WindowsPowerShell\v1.0\powershell.exe`; lipsa scriptului sau a PowerShell se raportează explicit.

## Încă pe procese externe (de înlocuit în pașii următori)

`esentutl /vss` (SRUM, Amcache), `reg save` (BAM, ShimCache), `MpCmdRun.exe` (verdict Defender). Ele rulează din `System32` cu cale completă
și rămân în ambele ediții până la înlocuire; pentru P1 (fără procese) înlocuirea este necesară. Vezi lista P2/P3 din
`WINDOWS_TOOLING_COMPAT.md` 3.8.

## Lista de laborator (de bifat de proprietar pe fiecare versiune)

Pentru fiecare: Windows 10 22H2, 11 24H2, 11 25H2, Server 2016, 2019, 2022, 2025. Mașină curată, fără .NET, fără rețea.

1. Copiază pachetul ediției; rulează `LogAnalyzer.exe --self-test --self-test-out=%TEMP%\st.txt`; verifică `SELF-TEST PASSED`.
2. Aceeași comandă pentru `LogAnalyzer.Classified.exe`.
3. Pornește aplicația ca utilizator obișnuit și ca administrator; colectare Quick; verifică în raport rândurile de acoperire
   (canale lipsă, Prefetch gol/dezactivat, Defender absent) - niciun canal nu trebuie omis tăcut.
4. Hash-ul unui `.evtx` exportat cu aplicația = hash-ul exportului `wevtutil epl` pentru același canal oprit (sau aceleași înregistrări).
5. Ediția neclasificată: izolare stație și ridicare; `Get-NetFirewallRule -DisplayName DFIR_EMERGENCY_ISOLATION` o vede, apoi dispare.
6. Colectare de audit pe PC: `AuditCollector.ps1` produce `CollectionManifest.csv`, cod de ieșire 0 sau 2; modulele lipsă = UNAVAILABLE.
7. Ediția clasificată: Procmon/Wireshark arată zero conexiuni de rețea și zero procese copil.
