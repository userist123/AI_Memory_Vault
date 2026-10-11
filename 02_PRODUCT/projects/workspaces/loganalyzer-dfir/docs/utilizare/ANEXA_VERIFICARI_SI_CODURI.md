# Anexă: verificările controlului și codurile, pe înțeles

> Generată automat din textele pe care aplicația le arată pe ecran (`CheckGuides`, `CodeExplainer`). Nu se editează de mână:
> se schimbă textul în aplicație și se regenerează (`LA_UPDATE_DOCS=1 dotnet test --filter UserGuideAppendixTests`).

## Verificările controlului stației

### C01

- **Ce verificăm:** Dacă contul „Guest” (vizitator, fără parolă personală) este dezactivat.
- **De ce contează:** Contul Guest permite intrarea pe stație fără un cont nominal; nu se poate ști cine a lucrat.
- **Cum verifică aplicația:** A citit lista conturilor locale din Windows și a căutat contul care se termină în -501 (Guest are mereu acest număr, chiar dacă e redenumit).
- **Sursa tehnică:** `WMI Win32_UserAccount (LocalAccount = TRUE), SID …-501`
- **Cum se citește rezultatul:** Conform = dezactivat sau inexistent. Neconform = contul Guest este activ.

### C02

- **Ce verificăm:** Ce conturi active au drepturi de administrator pe stație.
- **De ce contează:** Un administrator poate schimba orice: poate șterge jurnale, instala programe, crea conturi. Lista trebuie să fie scurtă și aprobată.
- **Cum verifică aplicația:** A citit membrii grupului local Administrators și a păstrat doar conturile active.
- **Sursa tehnică:** `WMI Win32_Group (SID S-1-5-32-544) și Win32_GroupUser`
- **Cum se citește rezultatul:** Întotdeauna „de verificat”: comparați numele cu lista de administratori aprobați în scris.

### C03

- **Ce verificăm:** Dacă contul Administrator încorporat al Windows (cel creat la instalare) este dezactivat.
- **De ce contează:** Este un cont cunoscut de toată lumea, ținta preferată a celor care ghicesc parole.
- **Cum verifică aplicația:** A căutat contul care se termină în -500 (Administratorul încorporat are mereu acest număr).
- **Sursa tehnică:** `WMI Win32_UserAccount, SID …-500`
- **Cum se citește rezultatul:** Conform = dezactivat. De verificat = activ; poate fi justificat dacă este redenumit și are parolă puternică.

### C04

- **Ce verificăm:** Dacă există conturi active care au voie să aibă parolă goală.
- **De ce contează:** Un cont fără parolă poate fi folosit de oricine se așază la stație.
- **Cum verifică aplicația:** A citit indicatorul „parola nu este obligatorie” (PASSWD_NOTREQD) al fiecărui cont activ.
- **Sursa tehnică:** `WMI Win32_UserAccount.PasswordRequired`
- **Cum se citește rezultatul:** Indicatorul arată doar că parola goală este permisă, nu că parola chiar este goală. Verificați cu titularul contului.

### C05

- **Ce verificăm:** Ce conturi active au parola setată să nu expire niciodată.
- **De ce contează:** O parolă care nu se schimbă niciodată poate fi aflată în timp și folosită ani de zile.
- **Cum verifică aplicația:** A citit indicatorul de expirare a parolei pentru fiecare cont activ.
- **Sursa tehnică:** `WMI Win32_UserAccount.PasswordExpires`
- **Cum se citește rezultatul:** De verificat: fiecare excepție trebuie justificată (de exemplu conturi de serviciu).

### C06

- **Ce verificăm:** Cine are voie să lucreze pe stație de la distanță (Remote Desktop).
- **De ce contează:** Lucrul de la distanță înseamnă acces fără a fi fizic la stație.
- **Cum verifică aplicația:** A citit membrii grupului local Remote Desktop Users.
- **Sursa tehnică:** `WMI Win32_Group (SID S-1-5-32-555)`
- **Cum se citește rezultatul:** De verificat: fiecare nume trebuie să aibă aprobare. Pe o stație izolată grupul ar trebui să fie gol.

### P01

- **Ce verificăm:** Câte caractere trebuie să aibă cel puțin o parolă.
- **De ce contează:** Parolele scurte se ghicesc ușor.
- **Cum verifică aplicația:** A citit politica de parole a stației (aceeași pe care o arată „net accounts”).
- **Sursa tehnică:** `NetUserModalsGet (politica locală de parole)`
- **Cum se citește rezultatul:** Conform = cel puțin 12. De verificat = 8–11. Neconform = sub 8.

### P02

- **Ce verificăm:** După câte parole greșite se blochează contul.
- **De ce contează:** Fără blocare, cineva poate încerca la nesfârșit parole până o nimerește.
- **Cum verifică aplicația:** A citit pragul de blocare din politica de parole.
- **Sursa tehnică:** `NetUserModalsGet (prag și durată de blocare)`
- **Cum se citește rezultatul:** Conform = blocare după cel mult 10 încercări. Neconform = contul nu se blochează niciodată (0) sau pragul e prea mare.

### P03

- **Ce verificăm:** Dacă parolele expiră și trebuie schimbate periodic.
- **De ce contează:** O parolă schimbată periodic limitează timpul în care o parolă aflată poate fi folosită.
- **Cum verifică aplicația:** A citit durata maximă a parolei și istoricul (câte parole vechi nu pot fi refolosite).
- **Sursa tehnică:** `NetUserModalsGet (vârsta maximă, istoric)`
- **Cum se citește rezultatul:** De verificat = parolele nu expiră niciodată.

### P04

- **Ce verificăm:** Dacă UAC este pornit (fereastra care cere confirmare înainte ca un program să primească drepturi de administrator).
- **De ce contează:** Fără UAC, orice program pornit de un administrator — inclusiv unul rău-intenționat — primește control total fără întrebare.
- **Cum verifică aplicația:** A citit valoarea EnableLUA din registru.
- **Sursa tehnică:** `HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System → EnableLUA, ConsentPromptBehaviorAdmin`
- **Cum se citește rezultatul:** Conform = 1 (pornit). Neconform = 0 (oprit). Nedeterminat = valoarea nu a putut fi citită.

### P05

- **Ce verificăm:** Dacă stația intră automat într-un cont la pornire, fără parolă.
- **De ce contează:** Oricine pornește stația ajunge în cont; parola salvată poate fi citită din registru.
- **Cum verifică aplicația:** A citit valorile de pornire automată din registru (fără să citească parola în sine).
- **Sursa tehnică:** `HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon → AutoAdminLogon, DefaultPassword`
- **Cum se citește rezultatul:** Conform = dezactivată. Neconform = pornire automată activă sau parolă salvată.

### P06

- **Ce verificăm:** Dacă stația acceptă conexiuni de la distanță (Remote Desktop).
- **De ce contează:** Remote Desktop permite lucrul pe stație din alt loc.
- **Cum verifică aplicația:** A citit valoarea fDenyTSConnections din registru.
- **Sursa tehnică:** `HKLM\SYSTEM\CurrentControlSet\Control\Terminal Server → fDenyTSConnections`
- **Cum se citește rezultatul:** Conform = 1 (interzis). Pe o stație izolată, permis = neconform; altfel de verificat.

### P07

- **Ce verificăm:** Dacă firewall-ul Windows (bariera care oprește conexiunile nedorite) este pornit.
- **De ce contează:** Fără firewall, programele și serviciile stației sunt expuse în rețea.
- **Cum verifică aplicația:** A întrebat Windows starea firewall-ului pentru fiecare din cele trei profiluri: domeniu, privat, public.
- **Sursa tehnică:** `COM HNetCfg.FwPolicy2.FirewallEnabled`
- **Cum se citește rezultatul:** Conform = activ pe toate trei. Neconform = oprit pe cel puțin unul.

### P08

- **Ce verificăm:** Dacă antivirusul Microsoft Defender este pornit, inclusiv protecția în timp real.
- **De ce contează:** Protecția în timp real oprește programele rău-intenționate în momentul în care apar.
- **Cum verifică aplicația:** A întrebat Defender starea sa.
- **Sursa tehnică:** `WMI root\Microsoft\Windows\Defender MSFT_MpComputerStatus`
- **Cum se citește rezultatul:** Conform = activ și în timp real. Nedeterminat = starea nu a putut fi citită (poate exista alt antivirus: verificați vizual).

### P09

- **Ce verificăm:** Cât de vechi sunt semnăturile antivirusului (lista virușilor cunoscuți).
- **De ce contează:** Un antivirus cu semnături vechi nu recunoaște virușii noi.
- **Cum verifică aplicația:** A citit data ultimei actualizări a semnăturilor.
- **Sursa tehnică:** `MSFT_MpComputerStatus.AntivirusSignatureLastUpdated`
- **Cum se citește rezultatul:** Conform = cel mult 7 zile. Pe stația izolată se actualizează offline, după procedură: comparați data cu registrul actualizărilor.

### P10

- **Ce verificăm:** Ce foldere, programe sau tipuri de fișiere sunt excluse de la verificarea antivirusului.
- **De ce contează:** Atacatorii își ascund des programele în locuri excluse.
- **Cum verifică aplicația:** A citit listele de excluderi din setările Defender.
- **Sursa tehnică:** `MSFT_MpPreference ExclusionPath / ExclusionProcess / ExclusionExtension`
- **Cum se citește rezultatul:** De verificat: fiecare excludere trebuie justificată (de exemplu de furnizorul unei aplicații).

### P11

- **Ce verificăm:** Dacă discul de sistem este criptat cu BitLocker.
- **De ce contează:** Fără criptare, cine scoate discul din stație poate citi toate datele.
- **Cum verifică aplicația:** A întrebat Windows starea de protecție a discului C:.
- **Sursa tehnică:** `WMI root\cimv2\Security\MicrosoftVolumeEncryption Win32_EncryptableVolume.ProtectionStatus`
- **Cum se citește rezultatul:** Conform = protecție activă (1). Neconform = discul nu este protejat.

### P12

- **Ce verificăm:** Dacă versiunea veche (1) a protocolului de partajare a fișierelor, SMBv1, este oprită.
- **De ce contează:** SMBv1 are vulnerabilități cunoscute folosite de viruși care se răspândesc singuri (WannaCry).
- **Cum verifică aplicația:** A citit setările SMBv1 ale serverului și ale clientului din registru.
- **Sursa tehnică:** `HKLM\SYSTEM\CurrentControlSet\Services\LanmanServer\Parameters → SMB1; Services\mrxsmb10 → Start`
- **Cum se citește rezultatul:** Conform = dezactivat. Neconform = activ.

### P13

- **Ce verificăm:** Dacă Windows păstrează parolele în memorie într-o formă care poate fi citită (WDigest).
- **De ce contează:** Un program rău-intenționat ar putea extrage parolele în clar.
- **Cum verifică aplicația:** A citit valoarea UseLogonCredential din registru.
- **Sursa tehnică:** `HKLM\SYSTEM\CurrentControlSet\Control\SecurityProviders\WDigest → UseLogonCredential`
- **Cum se citește rezultatul:** Conform = 0 sau lipsă (implicit sigur). Neconform = 1.

### P14

- **Ce verificăm:** Dacă folosirea stick-urilor și discurilor USB este blocată prin politică.
- **De ce contează:** Stick-urile sunt principala cale prin care datele ies de pe o stație izolată sau prin care intră viruși.
- **Cum verifică aplicația:** A citit pornirea componentei USBSTOR și politica „interzice orice acces la stocare amovibilă”.
- **Sursa tehnică:** `HKLM\SYSTEM\CurrentControlSet\Services\USBSTOR → Start; HKLM\SOFTWARE\Policies\Microsoft\Windows\RemovableStorageDevices → Deny_All`
- **Cum se citește rezultatul:** Conform = blocat. De verificat = permis: atunci trebuie folosite doar suporturi aprobate și înregistrate.

### A01

- **Ce verificăm:** Dacă stația înregistrează în jurnal tot ce are nevoie un control (autentificări, conturi, programe, USB etc.).
- **De ce contează:** Ce nu se înregistrează nu lasă urme, deci nu poate fi verificat.
- **Cum verifică aplicația:** A citit politica de audit avansat a stației și a comparat-o cu cele 10 categorii necesare controlului.
- **Sursa tehnică:** `auditpol /get /category:* (AuditQuerySystemPolicy)`
- **Cum se citește rezultatul:** Conform = toate categoriile necesare sunt auditate. Neconform = lipsesc categorii; sunt numite în constatare.

### A02

- **Ce verificăm:** Cât de mare poate fi jurnalul Security înainte să se suprascrie.
- **De ce contează:** Un jurnal mic se umple în câteva zile și evenimentele vechi dispar.
- **Cum verifică aplicația:** A citit dimensiunea maximă și modul jurnalului.
- **Sursa tehnică:** `Configurația jurnalului Security (EventLogConfiguration)`
- **Cum se citește rezultatul:** Conform = cel puțin 128 MB. Mod Circular = suprascrie cele mai vechi evenimente.

### A03

- **Ce verificăm:** Dacă jurnalul Security mai conține evenimente din toată perioada controlată.
- **De ce contează:** Dacă cel mai vechi eveniment e mai nou decât începutul perioadei, activitatea de dinainte nu mai poate fi verificată.
- **Cum verifică aplicația:** A citit data celui mai vechi eveniment din jurnal și a comparat-o cu începutul perioadei.
- **Sursa tehnică:** `Jurnalul Security, primul eveniment`
- **Cum se citește rezultatul:** Conform = acoperă perioada. Neconform = jurnalul începe după începutul perioadei.

### A04

- **Ce verificăm:** Dacă jurnalul System mai conține evenimente din toată perioada controlată.
- **De ce contează:** La fel ca pentru Security.
- **Cum verifică aplicația:** La fel ca pentru Security, pe jurnalul System.
- **Sursa tehnică:** `Jurnalul System, primul eveniment`
- **Cum se citește rezultatul:** Conform = acoperă perioada. Neconform = jurnalul începe după începutul perioadei.

### A05

- **Ce verificăm:** Dacă cineva a golit jurnalele în perioada controlată.
- **De ce contează:** Golirea jurnalelor șterge urmele; aproape întotdeauna trebuie explicată.
- **Cum verifică aplicația:** A căutat evenimentele de golire a jurnalelor.
- **Sursa tehnică:** `Security 1102, System 104`
- **Cum se citește rezultatul:** Conform = nicio golire. Neconform = cel puțin o golire; contul care a făcut-o apare în probe.

### A06

- **Ce verificăm:** Dacă politica de audit (ce se înregistrează) a fost schimbată în perioada controlată.
- **De ce contează:** Cine reduce auditul poate acționa fără urme.
- **Cum verifică aplicația:** A căutat evenimentele de modificare a politicii de audit.
- **Sursa tehnică:** `Security 4719`
- **Cum se citește rezultatul:** De verificat: fiecare schimbare trebuie să fie aprobată; în probe codul GUID arată categoria schimbată.

### A07

- **Ce verificăm:** Dacă ora stației a fost schimbată manual de o persoană.
- **De ce contează:** Cu ora schimbată, toate orele din jurnale sunt greșite.
- **Cum verifică aplicația:** A căutat schimbările de oră, fără corecțiile automate ale Windows.
- **Sursa tehnică:** `Security 4616 (fără svchost.exe și conturile sistemului)`
- **Cum se citește rezultatul:** Conform = nicio schimbare manuală. De verificat = schimbări făcute de utilizatori.

### U01

- **Ce verificăm:** Cine a fost adăugat sau scos din grupul administratorilor în perioada controlată.
- **De ce contează:** Primirea drepturilor de administrator trebuie aprobată.
- **Cum verifică aplicația:** A căutat adăugările și eliminările din grupul local Administrators.
- **Sursa tehnică:** `Security 4732, 4733 (grup S-1-5-32-544)`
- **Cum se citește rezultatul:** Conform = nicio modificare. De verificat = modificări; comparați cu aprobările.

### U02

- **Ce verificăm:** Ce conturi au fost create, șterse, activate, dezactivate sau au avut parola resetată.
- **De ce contează:** Fiecare operație pe conturi trebuie să aibă o cerere aprobată.
- **Cum verifică aplicația:** A căutat evenimentele de gestionare a conturilor.
- **Sursa tehnică:** `Security 4720, 4722, 4724, 4725, 4726`
- **Cum se citește rezultatul:** Conform = nicio operație. De verificat = operații; comparați cu cererile aprobate.

### U03

- **Ce verificăm:** Cine s-a conectat la stație de la distanță (Remote Desktop) și de unde.
- **De ce contează:** Pe o stație izolată nu ar trebui să existe acces de la distanță.
- **Cum verifică aplicația:** A numărat autentificările de tip 10 (la distanță) și sesiunile cu adresă de rețea.
- **Sursa tehnică:** `Security 4624 (tip de logon 10); TerminalServices-LocalSessionManager 21, 25`
- **Cum se citește rezultatul:** Conform = niciuna. Pe stația izolată = neconform; altfel de verificat.

### U04

- **Ce verificăm:** Dacă au existat serii de parole greșite (5 sau mai multe într-o oră pentru același cont).
- **De ce contează:** Poate însemna că cineva încearcă să ghicească parola.
- **Cum verifică aplicația:** A grupat autentificările eșuate pe cont și pe oră.
- **Sursa tehnică:** `Security 4625`
- **Cum se citește rezultatul:** Conform = nicio serie. De verificat = serii; întrebați titularul contului dacă și-a uitat parola.

### U05

- **Ce verificăm:** Cine a lucrat pe stație cu drepturi de administrator.
- **De ce contează:** Lucrul zilnic nu ar trebui făcut cu drepturi de administrator.
- **Cum verifică aplicația:** A numărat sesiunile care au primit drepturi speciale, fără conturile sistemului.
- **Sursa tehnică:** `Security 4672`
- **Cum se citește rezultatul:** De verificat: comparați cu lista administratorilor aprobați.

### U06

- **Ce verificăm:** Dacă au rulat scripturi PowerShell care folosesc comenzi des abuzate (descărcare, cod ascuns, oprirea antivirusului).
- **De ce contează:** Aceste comenzi sunt folosite frecvent de atacatori.
- **Cum verifică aplicația:** A căutat în textul scripturilor înregistrate cuvinte-cheie: DownloadString, FromBase64String, Invoke-Expression/IEX, Set-MpPreference, -EncodedCommand.
- **Sursa tehnică:** `Microsoft-Windows-PowerShell/Operational 4104`
- **Cum se citește rezultatul:** De verificat: un specialist IT trebuie să se uite la textul scriptului din probe.

### D01

- **Ce verificăm:** Ce dispozitive de stocare USB au fost conectate la stație.
- **De ce contează:** Pe o stație izolată, doar suporturile aprobate și înregistrate au voie să fie folosite.
- **Cum verifică aplicația:** A citit lista dispozitivelor USB din registru și conectările de discuri din jurnal, cu producător, model și număr de serie.
- **Sursa tehnică:** `HKLM\SYSTEM\CurrentControlSet\Enum\USBSTOR; Microsoft-Windows-Partition/Diagnostic 1006`
- **Cum se citește rezultatul:** Întotdeauna de verificat: comparați seriile cu registrul suporturilor. Lipsa urmelor nu dovedește că nu s-au folosit suporturi.

### N01

- **Ce verificăm:** Dacă stația izolată s-a conectat la vreo rețea în perioada controlată.
- **De ce contează:** O stație izolată conectată la rețea poate transmite sau primi date fără control.
- **Cum verifică aplicația:** A căutat profiluri de rețea folosite în perioadă, evenimente de conectare și interfețe de rețea active acum.
- **Sursa tehnică:** `HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\NetworkList\Profiles; NetworkProfile 10000; WLAN-AutoConfig 8001`
- **Cum se citește rezultatul:** Neconform = s-a conectat (cine, când, ce s-a transferat trebuie investigat). Lipsa urmelor = de verificat, nu dovadă.

### N02

- **Ce verificăm:** La ce rețele s-a conectat stația vreodată (istoric).
- **De ce contează:** Arată dacă stația a fost vreodată în altă rețea decât cea permisă.
- **Cum verifică aplicația:** A citit lista profilurilor de rețea salvate.
- **Sursa tehnică:** `HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\NetworkList\Profiles`
- **Cum se citește rezultatul:** De verificat: fiecare rețea din listă trebuie să fie una permisă.

### S01

- **Ce verificăm:** Ce programe au fost instalate sau dezinstalate în perioada controlată.
- **De ce contează:** Pe stație trebuie să existe doar software aprobat.
- **Cum verifică aplicația:** A căutat evenimentele Windows Installer.
- **Sursa tehnică:** `Application 11707, 11724, 1033, 1034`
- **Cum se citește rezultatul:** Conform = nicio operație. De verificat = operații; comparați cu lista software-ului aprobat.

### S02

- **Ce verificăm:** Ce servicii noi (programe permanente în fundal) și task-uri programate au fost create.
- **De ce contează:** Sunt metodele obișnuite prin care un program rău-intenționat rămâne activ după repornire.
- **Cum verifică aplicația:** A căutat instalările de servicii (fără drivere, listate separat în cronologie) și crearea de task-uri programate.
- **Sursa tehnică:** `System 7045; Security 4698`
- **Cum se citește rezultatul:** Conform = niciunul. De verificat = fiecare nume necunoscut trebuie explicat.

## Ce înseamnă rezultatele

- CONFORM: ce s-a verificat corespunde regulii.
- NECONFORM: ce s-a găsit încalcă regula. Se trece în procesul-verbal și se cere remedierea.
- DE VERIFICAT: aplicația arată faptele, dar doar organizația poate spune dacă erau aprobate. Comparați cu documentele (registre, aprobări).
- NEDETERMINAT: sursa necesară nu a putut fi citită, deci verificarea nu s-a putut face. Nu înseamnă nici bine, nici rău.

## Codurile care apar în probe

| Cod | Tip | Ce este | De ce contează | Când apare / valori |
|---|---|---|---|---|
| auditpol | comandă Windows | Comanda Windows care arată ce categorii de activitate înregistrează stația în jurnalul Security. | Este sursa oficială a politicii de audit efective. | Necesită drepturi de administrator. |
| Configurația jurnalului | setare Windows | Dimensiunea maximă a jurnalului și ce face când se umple. | Un jurnal mic pierde repede evenimentele vechi. |  |
| Security 1102 | eveniment Windows | Jurnalul Security (registrul de securitate al stației) a fost golit de cineva. | După golire, tot ce s-a întâmplat înainte pe stație nu mai poate fi verificat. Este una dintre cele mai importante urme la un control. | Apare doar când cineva golește jurnalul manual sau printr-un program; câmpul „de” arată contul care a făcut-o. |
| System 104 | eveniment Windows | Un jurnal de evenimente (altul decât Security sau chiar System) a fost golit. | La fel ca la 1102: urmele de dinainte se pierd. |  |
| Security 4719 | eveniment Windows | Politica de audit a fost modificată (ce anume înregistrează stația în jurnal). | Cine reduce auditul poate face lucruri care nu mai lasă urme. | Codul GUID arată ce categorie s-a schimbat; %%8449/%%8451 = s-a activat, %%8448/%%8450 = s-a dezactivat. |
| Security 4616 | eveniment Windows | Ora sistemului a fost schimbată. | Cu ora schimbată, orele din toate jurnalele devin greșite; poate fi folosită pentru a ascunde când s-a întâmplat ceva. | Windows își corectează singur ora (svchost.exe, cont SYSTEM) — acelea nu sunt listate. Listate sunt doar schimbările făcute de o persoană. |
| Win32_UserAccount | interogare Windows (WMI) | Lista conturilor locale ale stației, cu starea lor (activ, dezactivat, parolă obligatorie, expirare). | Este lista reală a conturilor, chiar dacă unele sunt ascunse din ecranul de pornire. |  |
| S-1-5-32-544 | identificator de securitate (SID) | Administrators: grupul administratorilor locali: control total. | SID-ul identifică sigur contul sau grupul, chiar dacă a fost redenumit. |  |
| Win32_Group | interogare Windows (WMI) | Grupurile locale ale stației și membrii lor. | Arată cine are drepturi de administrator sau de acces la distanță. |  |
| S-1-5-32-555 | identificator de securitate (SID) | Remote Desktop Users: cine are voie să lucreze de la distanță. | SID-ul identifică sigur contul sau grupul, chiar dacă a fost redenumit. |  |
| Partition 1006 | eveniment Windows | Un disc a fost conectat la stație; jurnalul păstrează producătorul, modelul, seria și capacitatea. | Este cea mai bună urmă a unui stick USB sau disc extern conectat, cu numărul de serie de comparat cu registrul suporturilor. | BusType 7 = USB, 12 = card SD, 13 = card MMC. |
| Enum\\USBSTOR | cheie de registru | Lista tuturor dispozitivelor de stocare USB conectate vreodată la stație, cu seria lor. | Arată ce stick-uri au fost folosite, chiar dacă jurnalele s-au pierdut. | Ștergerea acestei liste este o urmă de ascundere. |
| NetworkProfile 10000 | eveniment Windows | Stația s-a conectat la o rețea (numele rețelei este în eveniment). | Pe o stație izolată, orice conectare la rețea este o abatere. |  |
| WLAN 8001 | eveniment Windows | Stația s-a conectat la o rețea fără fir (Wi-Fi); SSID = numele rețelei. | Pe o stație izolată, o conectare Wi-Fi este o abatere. |  |
| NetworkList\\Profiles | cheie de registru | Lista rețelelor la care stația s-a conectat vreodată, cu data primei și ultimei conectări. | Pe o stație izolată arată dacă a fost vreodată conectată la o rețea. | Profilurile pot fi șterse manual; lipsa lor nu dovedește nimic. |
| NetUserModalsGet | funcție Windows | Citește politica de parole a stației (aceeași pe care o arată comanda „net accounts”). | Arată regulile efective pentru parole și blocarea conturilor. |  |
| Policies\\System\\EnableLUA | valoare de registru | Pornește sau oprește UAC (fereastra care cere confirmare înainte ca un program să primească drepturi de administrator). | Cu UAC oprit, orice program pornit de un administrator are control total, fără întrebare. | 1 = activ (corect), 0 = oprit. |
| Policies\\System\\ConsentPromptBehaviorAdmin | valoare de registru | Cum întreabă UAC un administrator înainte de a da drepturi complete. | 0 înseamnă că nu se mai întreabă nimic. | 0 = fără întrebare, 1/3 = cere parola, 2/4 = cere confirmare, 5 = cere confirmare doar pentru programe din afara Windows (implicit). |
| Winlogon\\AutoAdminLogon | valoare de registru | Pornire automată în cont, fără să se tasteze parola. | Oricine pornește stația intră în cont. | 1 = activă (abatere), 0 sau lipsă = dezactivată. |
| Winlogon\\DefaultPassword | valoare de registru | Parolă salvată în registru pentru pornirea automată. | Parola stă în clar și poate fi citită de oricine are acces la registru. | Aplicația verifică doar dacă există, nu o citește. |
| Terminal Server\\fDenyTSConnections | valoare de registru | Permite sau interzice conectarea de la distanță (Remote Desktop). | Remote Desktop deschis permite lucrul pe stație din altă parte. | 1 = interzis (corect pe stația izolată), 0 = permis. |
| HNetCfg.FwPolicy2 | interfață Windows | Starea firewall-ului Windows pe fiecare profil de rețea. | Arată dacă firewall-ul este pornit. |  |
| MSFT_MpComputerStatus | interogare Microsoft Defender | Starea antivirusului Defender: pornit, protecție în timp real, data semnăturilor. | Arată dacă stația este protejată acum. | Dacă există alt antivirus, Defender poate apărea oprit; se verifică vizual. |
| ExclusionPath | setare Defender | Un folder pe care antivirusul NU îl verifică. | Atacatorii își pun programele în foldere excluse ca să nu fie detectate. |  |
| ExclusionProcess | setare Defender | Un program pe care antivirusul NU îl verifică. | Programul exclus poate face orice fără să fie oprit de antivirus. |  |
| ExclusionExtension | setare Defender | Un tip de fișier pe care antivirusul NU îl verifică. | Toate fișierele de acel tip scapă de verificare. |  |
| MSFT_MpPreference | interogare Microsoft Defender | Setările Defender, inclusiv excluderile. | Excluderile sunt locuri pe care antivirusul nu le verifică. |  |
| Win32_EncryptableVolume | interogare BitLocker | Starea criptării BitLocker a discului. | Arată dacă datele sunt protejate dacă discul este scos din stație. | ProtectionStatus 1 = protejat, 0 = neprotejat. |
| LanmanServer\\Parameters\\SMB1 | valoare de registru | Versiunea veche (1) a protocolului de partajare a fișierelor. | SMBv1 are vulnerabilități cunoscute folosite de viruși (de exemplu WannaCry). | 0 sau lipsă = dezactivat. |
| Services\\mrxsmb10\\Start | valoare de registru | Pornirea componentei client SMBv1. | Vezi SMB1. | 4 = dezactivat; 2 sau 3 = poate porni. |
| WDigest\\UseLogonCredential | valoare de registru | Dacă Windows păstrează parolele în memorie în formă care poate fi citită. | Cu valoarea 1, un program rău-intenționat poate extrage parolele în clar (tehnică Mimikatz). | 0 sau lipsă = sigur (implicit pe Windows 10/11). |
| Services\\USBSTOR\\Start | valoare de registru | Pornirea componentei Windows care citește stick-urile și discurile USB. | Dacă este oprită, stick-urile USB nu mai pot fi folosite pe stație. | 4 = blocat (stick-urile nu funcționează), 3 = permis (implicit). |
| RemovableStorageDevices\\Deny_All | valoare de registru | Politica „Toate clasele de stocare amovibilă: interzice orice acces”. | Blochează toate suporturile amovibile. | 1 = blocat. |
| Application 11707 | eveniment Windows | Windows Installer: un program a fost instalat cu succes. | Software-ul instalat trebuie să fie pe lista aprobată. |  |
| Application 11724 | eveniment Windows | Windows Installer: un program a fost dezinstalat cu succes. | Dezinstalarea poate ascunde un program folosit anterior. |  |
| Application 1033 | eveniment Windows | Windows Installer: instalare încheiată (cu codul de rezultat). | Software-ul instalat trebuie să fie pe lista aprobată. | Codul de stare 0 = reușit. |
| Application 1034 | eveniment Windows | Windows Installer: dezinstalare încheiată. | Dezinstalarea poate ascunde un program folosit anterior. |  |
| System 7045 | eveniment Windows | Un serviciu nou (program care rulează permanent în fundal) a fost instalat. | Serviciile noi sunt o metodă frecventă de a păstra un program rău-intenționat activ după repornire. | Calea executabilului (ImagePath) arată ce program rulează. |
| Security 4698 | eveniment Windows | A fost creat un task programat (o comandă care pornește singură la o oră sau la pornire). | Task-urile programate sunt o metodă frecventă prin care un program rău-intenționat repornește singur. | Programele legitime (actualizări) creează și ele task-uri; comparați numele cu software-ul aprobat. |
| Security 4732 | eveniment Windows | Un membru a fost adăugat într-un grup local (de exemplu Administrators). | Adăugarea în Administrators dă control total asupra stației. | SID-ul S-1-5-32-544 înseamnă grupul Administrators. |
| Security 4733 | eveniment Windows | Un membru a fost eliminat dintr-un grup local. | Schimbă drepturile contului. |  |
| Security 4720 | eveniment Windows | A fost creat un cont de utilizator nou. | Orice cont nou trebuie să aibă aprobare. |  |
| Security 4722 | eveniment Windows | Un cont de utilizator a fost activat. | Un cont dezactivat reactivat poate fi folosit din nou pentru acces. |  |
| Security 4724 | eveniment Windows | Parola altui cont a fost resetată (de obicei de un administrator). | Cine resetează parola altcuiva poate intra în contul acelei persoane. |  |
| Security 4725 | eveniment Windows | Un cont de utilizator a fost dezactivat. | Normal la plecarea unei persoane; de verificat dacă nu era prevăzut. |  |
| Security 4726 | eveniment Windows | Un cont de utilizator a fost șters. | Ștergerea unui cont poate ascunde cine a lucrat pe stație. |  |
| Security 4624 | eveniment Windows | Cineva s-a autentificat cu succes pe stație. | Arată cine a lucrat pe stație și cum (la tastatură, de la distanță, deblocare ecran). | Tipul de logon spune cum: 2 = la tastatură, 10 = de la distanță (RDP), 7 = deblocare ecran. |
| TerminalServices 21 | eveniment Windows | S-a deschis o sesiune de lucru; adresa arată de unde (LOCAL = la tastatură). | O adresă de rețea înseamnă lucru de la distanță (Remote Desktop). |  |
| TerminalServices 25 | eveniment Windows | O sesiune de la distanță a fost reconectată. | Arată lucru de la distanță pe stație. |  |
| Tip de logon 10 | cod de autentificare | Autentificare de la distanță (Remote Desktop). | Pe o stație izolată, lucrul de la distanță nu ar trebui să existe. |  |
| Security 4625 | eveniment Windows | O încercare de autentificare a EȘUAT (parolă greșită, cont inexistent sau blocat). | Multe eșecuri la rând pot însemna că cineva încearcă să ghicească parola. | Câteva eșecuri izolate sunt de obicei parole tastate greșit. |
| Security 4672 | eveniment Windows | O sesiune a primit drepturi speciale (de administrator). | Arată cine a lucrat cu drepturi de administrator, adică putea schimba orice pe stație. | Apare la fiecare autentificare a unui cont de administrator. |
| PowerShell 4104 | eveniment Windows | Textul unui script PowerShell care a rulat pe stație. | PowerShell este folosit și de atacatori; anumite comenzi (descărcare, cod ascuns în Base64, dezactivarea antivirusului) merită verificate. | Apare doar dacă „Script Block Logging” este activ. |
