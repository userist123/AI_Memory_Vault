# Cercetare: ce mai lipsește aplicației (2026-11-09)

Două cercetări pe web, cu sursele de mai jos. Fiecare propunere a fost verificată în cod, ca să rămână doar lipsurile reale. Sursele au fost citite din rezultatele căutării; ce nu s-a putut confirma este marcat NEVERIFICAT.

## A. Controlul stației față de STIG Windows 11 (categoria I)

Controlul are deja 90 de verificări. Printre ele sunt BitLocker, Secure Boot și TPM, Defender (activ, semnături, excluderi, ultima scanare), WDigest, protecția LSA, Credential Guard, VBS, SMBv1, jurnalizarea PowerShell și PowerShell 2.0, AppLocker și WDAC, AutoPlay/AutoRun, camera și microfonul, Asistența la distanță, WinRM, blocarea ecranului, sursa de timp și versiunea de Windows.

Lipsesc regulile STIG de categoria I de mai jos. Ele sunt implementate ca P40–P47:

| Cod | Verificare | Regula STIG (Windows 11 V2R3) |
|---|---|---|
| P40 | Acces anonim la conturi și partajări | WN11-SO-000145, -000150, -000165 |
| P41 | Hash LM și NTLMv2 obligatoriu | WN11-SO-000195, -000205 |
| P42 | Drepturi periculoase (Act as part of OS, Create token, Debug) | WN11-UR-000015, -000045, -000065 |
| P43 | AlwaysInstallElevated | WN11-CC-000315 |
| P44 | DEP și SEHOP | WN11-00-000145, -000150 |
| P45 | WinRM fără Basic și fără trafic necriptat | WN11-CC-000330, -000345 (și -000335, -000350, categoria II) |
| P46 | LLMNR oprit | fără regulă STIG găsită (NEVERIFICAT); recomandare de întărire |
| P47 | IIS instalat | WN11-00-000100 |

Identificatorii STIG au fost confirmați pe versiunea V2R3 (https://stigaview.com/products/win11/v2r3/onepage). Există versiuni mai noi (V2R7); înainte de un export oficial, ei se reverifică pe pachetul DISA curent.

Ce nu se poate citi din Windows: parola BIOS și locul în care se păstrează cheia de recuperare BitLocker. Acestea rămân câmpuri completate de mână de controlor.

## B. Raportul pentru evaluatori

- Export ca listă de verificare STIG (.cklb / XCCDF), alături de PDF-ul semnat. Fiecare regulă primește starea (Open / Not a Finding / Not Applicable / Not Reviewed), detaliile și severitatea. Acesta este cel mai valoros pas pentru compatibilitatea cu instrumentele evaluatorilor.
- „Nu se aplică” cu motiv scris, niciodată omis în tăcere. Exemple: Credential Guard și LAPS pe o stație fără domeniu.
- Referința la procedurile de securitate (PrOpSec) și la certificatul de acreditare al stației, cu număr și dată de expirare. Ordinul ORNISS 16/2014 aprobă INFOSEC 2 (https://legeaz.net/monitorul-oficial-262-2014/ordinul-guvernului-romaniei-orniss-16-2014), iar Ordinul ORNISS 18/2014 este ghidul PrOpSec (https://legeaz.net/monitorul-oficial-242-2014/ordinul-guvernului-romaniei-orniss-18-2014/anexa-ghid). Un model oficial de raport nu a fost găsit (NEVERIFICAT).

### Listă de verificare STIG (.cklb) — implementat

„Exportă pentru ofițer” scrie acum și un fișier `.cklb` (STIG Viewer 3, JSON) pentru STIG Windows 11 V2R3, cu 52 de reguli. Starea unei reguli nu depășește ce a verificat aplicația:

- **Evaluate una câte una** (P40–P45, P47): regula primește „Not a Finding” sau „Open” doar din valoarea exactă citită (de exemplu RestrictAnonymous = 0 dă „Open” pentru SO-000150, dar SO-000145 rămâne „Not a Finding”). O valoare necitită dă „Not Reviewed”.
- **Preluate din verificarea întreagă** (C01, P07, P13, P15, P24): același criteriu ca regula; CONFORM → „Not a Finding”, NECONFORM → „Open”, DE VERIFICAT/NEDETERMINAT → „Not Reviewed”. Excepție: la P07 (firewall) un NECONFORM rămâne „Not Reviewed”, fiindcă un firewall terț ar putea îndeplini regula.
- **Doar dovadă** (restul, inclusiv P08 și N07): întotdeauna „Not Reviewed”, cu detaliile și dovada verificării atașate, fiindcă verificarea aplicației diferă de criteriul STIG sau acoperă mai multe reguli (P08 verifică doar Defender; N07 acceptă și setarea din afara politicii). „Nu se aplică” la verificare dă „Not Applicable”; o verificare care nu a rulat dă „Not Reviewed”.

Limite: ID-urile de regulă SV nu sunt completate (nu au putut fi verificate pe sursă); V2R3 nu este versiunea curentă (există V2R7), iar data lansării nu este verificată; textul de verificare și de remediere al STIG nu este inclus; fișierul nu este semnat. Evaluatorul decide starea finală în STIG Viewer.

## C. Analiza probelor față de uneltele de referință

Există deja în cod:
- detecții pentru Kerberoasting și AS-REP;
- semnale BYOVD;
- MFT de bază, ShellBags, UserAssist, BAM, Coș de reciclare;
- PCA, MPLog;
- cache-ul de imagini RDP, istoricul PowerShell;
- accesul la LSASS (GrantedAccess).

Lipsuri, în ordinea valorii:

1. **Pachet de detecții pentru Active Directory.** Lipsesc abuzul ADCS (4886/4887 pe autoritatea de certificare, urmat de 4768) și releul NTLM (4624 tip 3 NTLM cu IP-ul care nu corespunde stației; logări anonime pe serverele CA). Surse: https://www.decryptiondigest.com/blog/how-to-detect-active-directory-certificate-services-adcs-attacks , https://nccgroup.com/au/research-blog/detecting-and-hunting-for-the-petitpotam-ntlm-relay-attack
2. **Oprirea EDR-ului prin filtre WFP.** Evenimentele 5441/5447 cu filtre noi de ieșire și pachete blocate ale proceselor de securitate. Sursa: https://detection.fyi/sigmahq/sigma/windows/builtin/security/win_security_hktl_edr_silencer
3. **Istoricul detecțiilor Defender (DetectionHistory).** Dă amenințarea, calea, ora și amprentele. Sursa: https://www.sans.org/blog/uncovering-windows-defender-real-time-protection-history-with-dhparser
4. **Unelte de administrare la distanță abuzate (RMM).** AnyDesk, ScreenConnect și altele, după instalarea serviciului, procese și jurnale. Sursa: https://redcanary.com/threat-detection-report/trends/rmm-tools
5. **Sesiunile RDP reconstruite.** Evenimentele 1149, 21, 22, 24 și 25, legate prin ID-ul sesiunii.
6. **Artefacte mici.** Filele nesalvate din Notepad (TabState) și MUICache.
7. **Lucrări mari (L).** Indexul Windows Search (ESE sau SQLite pe Windows 11), $I30 și $LogFile, copiile umbră VSS citite offline.
