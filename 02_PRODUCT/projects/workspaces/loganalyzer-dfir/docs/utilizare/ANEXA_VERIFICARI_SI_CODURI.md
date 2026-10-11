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

### P15

- **Ce verificăm:** Dacă pornirea securizată (Secure Boot) este activă și în ce mod pornește stația (UEFI modern sau BIOS vechi).
- **De ce contează:** Fără ea, cineva poate porni stația de pe un stick cu alt sistem de operare și poate ocoli toate protecțiile Windows.
- **Cum verifică aplicația:** A citit din registru starea Secure Boot și tipul de firmware al plăcii de bază.
- **Sursa tehnică:** `HKLM\SYSTEM\CurrentControlSet\Control\SecureBoot\State → UEFISecureBootEnabled; HKLM\SYSTEM\CurrentControlSet\Control → PEFirmwareType`
- **Cum se citește rezultatul:** Conform = pornită. Neconform = oprită sau BIOS vechi. De verificat = informația lipsește (stație veche); verificați în setările UEFI.

### P16

- **Ce verificăm:** După cât timp de inactivitate se blochează automat ecranul.
- **De ce contează:** Dacă utilizatorul pleacă de la stație, oricine poate folosi contul lui cât timp ecranul rămâne deschis.
- **Cum verifică aplicația:** A citit timpul de inactivitate stabilit prin politică, în secunde.
- **Sursa tehnică:** `HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System → InactivityTimeoutSecs`
- **Cum se citește rezultatul:** Conform = între 1 secundă și 900 (15 minute). Neconform = 0 (dezactivat) sau peste 15 minute. De verificat = politica nu este setată; ecranul poate fi blocat altfel (screensaver cu parolă), pe care aplicația nu îl citește.

### P17

- **Ce verificăm:** Dacă stația are o listă albă de programe (AppLocker) și dacă ea blochează sau doar notează.
- **De ce contează:** Cu o listă albă blocată, pe stație rulează doar programele aprobate; fără ea poate porni orice program adus pe un stick.
- **Cum verifică aplicația:** A citit colecțiile de reguli AppLocker din politică, modul fiecăreia (1 = blochează, 0 = doar notează) și modul de pornire al serviciului care le aplică.
- **Sursa tehnică:** `HKLM\SOFTWARE\Policies\Microsoft\Windows\SrpV2 → EnforcementMode; HKLM\SYSTEM\CurrentControlSet\Services\AppIDSvc → Start`
- **Cum se citește rezultatul:** Conform = toate colecțiile blochează. Neconform = lista cere blocare, dar serviciul care o aplică este dezactivat. De verificat = lipsește, este doar în mod audit sau fără mod; App Control (WDAC) nu este verificat de aplicație.

### P18

- **Ce verificăm:** Dacă stația pornește singură conținutul unui stick sau CD introdus (AutoPlay / AutoRun).
- **De ce contează:** Un stick poate conține un program care pornește singur la introducere, fără să-l deschidă cineva.
- **Cum verifică aplicația:** A citit cele două setări de politică care opresc pornirea automată pentru suporturi amovibile și CD-uri.
- **Sursa tehnică:** `HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\Explorer → NoDriveTypeAutoRun, NoAutorun`
- **Cum se citește rezultatul:** Conform = oprită (255 sau cel puțin suporturi amovibile și CD, ori NoAutorun = 1). Neconform = setată, dar insuficient. De verificat = nesetată; se aplică comportamentul implicit al Windows.

### P19

- **Ce verificăm:** Dacă accesul la cameră și la microfon este blocat prin politică pentru toate aplicațiile.
- **De ce contează:** O cameră sau un microfon pornit pe o stație clasificată poate înregistra imagini și conversații din încăpere.
- **Cum verifică aplicația:** A citit permisiunea „Deny” (blocat) sau „Allow” (permis) pentru cameră și microfon din setările de confidențialitate ale Windows.
- **Sursa tehnică:** `HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\webcam → Value și microphone → Value`
- **Cum se citește rezultatul:** Conform = ambele „Deny”. De verificat = altfel. Aplicația nu poate spune dacă stația are fizic cameră sau microfon; verificați și fizic (acoperită, deconectată sau dezactivată în BIOS).

### P20

- **Ce verificăm:** Dacă funcțiile Windows Recall (poze periodice ale ecranului) și Copilot (asistent) sunt oprite prin politică.
- **De ce contează:** Recall păstrează pe disc imagini ale ecranului, adică copii ale documentelor care nu sunt în nicio evidență.
- **Cum verifică aplicația:** A citit politicile care opresc Recall și Copilot (pentru Copilot și valoarea utilizatorului care a pornit aplicația).
- **Sursa tehnică:** `HKLM\SOFTWARE\Policies\Microsoft\Windows\WindowsAI → DisableAIDataAnalysis, AllowRecallEnablement; WindowsCopilot → TurnOffWindowsCopilot`
- **Cum se citește rezultatul:** Conform = ambele oprite. Neconform = cel puțin una permisă explicit. De verificat = nesetat; funcția poate lipsi pe această versiune de Windows. Calea politicii Copilot nu are încă o sursă oficială citată.

### P21

- **Ce verificăm:** Dacă procesul Windows care păstrează parolele (LSA) rulează protejat.
- **De ce contează:** Un program rău-intenționat poate încerca să citească parolele din memoria acestui proces; protecția îl împiedică.
- **Cum verifică aplicația:** A citit valoarea RunAsPPL din registru.
- **Sursa tehnică:** `HKLM\SYSTEM\CurrentControlSet\Control\Lsa → RunAsPPL`
- **Cum se citește rezultatul:** Conform = 1 sau 2 (protejat). Neconform = 0 (oprit). De verificat = nesetat; pe versiuni recente de Windows poate fi activ implicit, dar aplicația nu poate confirma.

### P22

- **Ce verificăm:** Dacă Credential Guard (izolarea parolelor într-o zonă protejată a memoriei) rulează.
- **De ce contează:** Chiar dacă un program rău-intenționat rulează pe stație, nu poate citi parolele izolate.
- **Cum verifică aplicația:** A cerut Windows lista serviciilor de securitate bazate pe virtualizare care rulează acum.
- **Sursa tehnică:** `WMI root\Microsoft\Windows\DeviceGuard, Win32_DeviceGuard → SecurityServicesRunning`
- **Cum se citește rezultatul:** Conform = rulează (1 în listă). De verificat = nu rulează; cerința oficială este formulată pentru stațiile din domeniu. Nedeterminat = nu s-a putut citi (poate cere administrator sau ediția Windows nu o oferă).

### P23

- **Ce verificăm:** Dacă Windows înregistrează textul scripturilor PowerShell și transcrierea sesiunilor.
- **De ce contează:** Fără această înregistrare, un script rău-intenționat poate rula fără să lase vreo urmă pe stație.
- **Cum verifică aplicația:** A citit cele două politici PowerShell: înregistrarea blocurilor de script și transcrierea.
- **Sursa tehnică:** `HKLM\SOFTWARE\Policies\Microsoft\Windows\PowerShell\ScriptBlockLogging → EnableScriptBlockLogging; ...\PowerShell\Transcription → EnableTranscripting`
- **Cum se citește rezultatul:** Conform = ambele pornite. De verificat = scripturile sunt înregistrate, transcrierea nu. Neconform = înregistrarea scripturilor nu este pornită prin politică.

### P24

- **Ce verificăm:** Dacă versiunea veche PowerShell 2.0 mai este instalată.
- **De ce contează:** PowerShell 2.0 nu înregistrează scripturile și poate fi folosit ca să se ocolească protecțiile versiunilor noi.
- **Cum verifică aplicația:** A căutat în registru motorul PowerShell 2.0.
- **Sursa tehnică:** `HKLM\SOFTWARE\Microsoft\PowerShell\1\PowerShellEngine → PowerShellVersion`
- **Cum se citește rezultatul:** Conform = nu s-a găsit. Neconform = versiunea 2.x este instalată. De verificat = cheia există cu altă versiune; confirmați în Funcții Windows opționale. Metoda de detectare trebuie confirmată în laborator.

### P25

- **Ce verificăm:** Ce date pot rămâne pe disc după oprire: fișierul de paginare, hibernarea și copia memoriei de la o cădere a sistemului.
- **De ce contează:** Toate trei pot păstra pe disc fragmente de documente care au fost doar deschise, nu salvate, și pot fi citite ulterior.
- **Cum verifică aplicația:** A citit trei setări: ștergerea fișierului de paginare la oprire, hibernarea permisă și tipul copiei memoriei la cădere.
- **Sursa tehnică:** `HKLM\SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management → ClearPageFileAtShutdown; ...\Control\Power → HibernateEnabled; ...\Control\CrashControl → CrashDumpEnabled`
- **Cum se citește rezultatul:** Conform = paginarea se șterge, hibernarea este oprită, nu se salvează copia memoriei. De verificat = cel puțin una lasă date pe disc; organizația decide dacă este acceptabil. Nedeterminat = o setare nu s-a putut citi.

### P26

- **Ce verificăm:** Dacă la pornirea stației apare un mesaj de avertizare și ce spune el.
- **De ce contează:** Mesajul trebuie să anunțe că stația tratează informații clasificate, la nivelul maxim aprobat, și că utilizarea este monitorizată.
- **Cum verifică aplicația:** A citit titlul și textul mesajului de la pornire.
- **Sursa tehnică:** `HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System → LegalNoticeCaption, LegalNoticeText`
- **Cum se citește rezultatul:** Neconform = nu există niciun mesaj. De verificat = există; citiți titlul afișat și comparați-l cu nivelul de clasificare din documentele stației.

### P27

- **Ce verificăm:** De unde își ia stația ora și dacă este configurat un server de timp.
- **De ce contează:** Orele din jurnale sunt probă doar dacă ceasul stației este de încredere; un ceas lăsat liber poate fi greșit.
- **Cum verifică aplicația:** A citit tipul de sincronizare și serverul de timp din configurarea serviciului de timp al Windows.
- **Sursa tehnică:** `HKLM\SYSTEM\CurrentControlSet\Services\W32Time\Parameters → Type, NtpServer`
- **Cum se citește rezultatul:** Întotdeauna „de verificat”: aplicația arată setările, nu măsoară abaterea ceasului. Comparați ora stației cu ceasul de referință la control.

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

### A08

- **Ce verificăm:** Dacă stația notează ce fișiere se copiază pe stick-uri și pe alte suporturi amovibile.
- **De ce contează:** Fără această urmă nu se poate afla ce fișiere au ieșit pe un stick; este principala dovadă într-un caz de scurgere de date.
- **Cum verifică aplicația:** A citit politica de audit pentru subcategoria „Removable Storage” (acces la fișiere pe suporturi amovibile) și, ca informație, valoarea HotPlugSecureOpen.
- **Sursa tehnică:** `auditpol: subcategoria Removable Storage; HKLM\SYSTEM\CurrentControlSet\Control\Storage → HotPlugSecureOpen`
- **Cum se citește rezultatul:** Conform = auditarea succeselor este pornită. Neconform = nu se înregistrează nimic despre copieri. Auditarea pornită nu garantează lista completă a fișierelor pe toate versiunile de Windows; verificați cu o copiere de probă.

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

### DM01

- **Ce verificăm:** Ce conturi de persoane (nu de calculatoare) au un nume de serviciu (SPN) atașat.
- **De ce contează:** Oricine are un cont în domeniu poate cere pentru aceste conturi un „bilet” Kerberos și poate încerca apoi, pe calculatorul lui, să ghicească parola (Kerberoasting). O parolă scurtă cade în câteva ore.
- **Cum verifică aplicația:** A citit din Active Directory conturile de utilizator active care au atributul servicePrincipalName, fără contul krbtgt.
- **Sursa tehnică:** `LDAP: servicePrincipalName pe obiectele user active`
- **Cum se citește rezultatul:** Conform = niciun cont. De verificat = există conturi, dar niciunul privilegiat; parola lor trebuie să fie foarte lungă. Neconform = un cont privilegiat (administrator) are SPN.

### DM02

- **Ce verificăm:** Ce conturi pot primi un bilet Kerberos fără să dovedească întâi că știu parola.
- **De ce contează:** Pentru aceste conturi oricine poate obține, fără nicio parolă, date din care încearcă apoi să ghicească parola (AS-REP roasting).
- **Cum verifică aplicația:** A citit indicatorul „Do not require Kerberos preauthentication” al conturilor active.
- **Sursa tehnică:** `LDAP: userAccountControl, indicatorul DONT_REQ_PREAUTH (0x400000)`
- **Cum se citește rezultatul:** Conform = niciun cont. Neconform = cel puțin un cont; indicatorul trebuie scos dacă nu există o justificare scrisă.

### DM03

- **Ce verificăm:** Ce conturi din domeniu au voie să aibă parolă goală.
- **De ce contează:** Un cont fără parolă poate fi folosit de oricine îi știe numele.
- **Cum verifică aplicația:** A citit indicatorul „parola nu este obligatorie” al conturilor active.
- **Sursa tehnică:** `LDAP: userAccountControl, indicatorul PASSWD_NOTREQD (0x20)`
- **Cum se citește rezultatul:** Conform = niciun cont. Neconform = conturi cu indicatorul; nu înseamnă că parola chiar e goală, dar trebuie verificat și indicatorul scos.

### DM04

- **Ce verificăm:** Ce conturi au parola păstrată într-o formă care poate fi transformată înapoi în text.
- **De ce contează:** Cine ajunge la baza de date a domeniului poate citi direct aceste parole.
- **Cum verifică aplicația:** A citit indicatorul „stocare a parolei cu criptare reversibilă” al conturilor active.
- **Sursa tehnică:** `LDAP: userAccountControl, indicatorul ENCRYPTED_TEXT_PWD_ALLOWED (0x80)`
- **Cum se citește rezultatul:** Conform = niciun cont. Neconform = conturi cu indicatorul; după scoaterea lui parola trebuie schimbată.

### DM05

- **Ce verificăm:** Ce conturi folosesc doar criptarea veche DES la autentificare.
- **De ce contează:** DES se sparge ușor cu calculatoarele de azi; biletele acestor conturi pot fi falsificate sau citite.
- **Cum verifică aplicația:** A citit indicatorul „folosește doar chei DES” al conturilor active.
- **Sursa tehnică:** `LDAP: userAccountControl, indicatorul USE_DES_KEY_ONLY (0x200000)`
- **Cum se citește rezultatul:** Apare doar când există conturi: Neconform; indicatorul trebuie scos și parola schimbată.

### DM10

- **Ce verificăm:** Cine este membru (direct sau prin alte grupuri) al grupului privilegiat „Domain Admins”.
- **De ce contează:** Membrii acestui grup pot controla tot domeniul sau o parte importantă din el. Lista trebuie să fie scurtă și aprobată în scris.
- **Cum verifică aplicația:** A citit membrii grupului, inclusiv pe cei primiți prin alte grupuri, și a verificat la fiecare dacă este activ, dacă a mai fost folosit recent și dacă parola îi expiră.
- **Sursa tehnică:** `LDAP: membrii recursivi ai grupului Domain Admins (-512); userAccountControl, lastLogonTimestamp`
- **Cum se citește rezultatul:** De verificat = comparați numele cu lista aprobată. Neconform = există membri dezactivați, nefolosiți de mult sau cu parolă care nu expiră; trebuie scoși sau corectați.

### DM11

- **Ce verificăm:** Cine este membru (direct sau prin alte grupuri) al grupului privilegiat „Enterprise Admins”.
- **De ce contează:** Membrii acestui grup pot controla tot domeniul sau o parte importantă din el. Lista trebuie să fie scurtă și aprobată în scris.
- **Cum verifică aplicația:** A citit membrii grupului, inclusiv pe cei primiți prin alte grupuri, și a verificat la fiecare dacă este activ, dacă a mai fost folosit recent și dacă parola îi expiră.
- **Sursa tehnică:** `LDAP: membrii recursivi ai grupului Enterprise Admins (-519); userAccountControl, lastLogonTimestamp`
- **Cum se citește rezultatul:** De verificat = comparați numele cu lista aprobată. Neconform = există membri dezactivați, nefolosiți de mult sau cu parolă care nu expiră; trebuie scoși sau corectați.

### DM12

- **Ce verificăm:** Cine este membru (direct sau prin alte grupuri) al grupului privilegiat „Schema Admins”.
- **De ce contează:** Membrii acestui grup pot controla tot domeniul sau o parte importantă din el. Lista trebuie să fie scurtă și aprobată în scris.
- **Cum verifică aplicația:** A citit membrii grupului, inclusiv pe cei primiți prin alte grupuri, și a verificat la fiecare dacă este activ, dacă a mai fost folosit recent și dacă parola îi expiră.
- **Sursa tehnică:** `LDAP: membrii recursivi ai grupului Schema Admins (-518); userAccountControl, lastLogonTimestamp`
- **Cum se citește rezultatul:** De verificat = comparați numele cu lista aprobată. Neconform = există membri dezactivați, nefolosiți de mult sau cu parolă care nu expiră; trebuie scoși sau corectați.

### DM13

- **Ce verificăm:** Cine este membru (direct sau prin alte grupuri) al grupului privilegiat „Group Policy Creator Owners”.
- **De ce contează:** Membrii acestui grup pot controla tot domeniul sau o parte importantă din el. Lista trebuie să fie scurtă și aprobată în scris.
- **Cum verifică aplicația:** A citit membrii grupului, inclusiv pe cei primiți prin alte grupuri, și a verificat la fiecare dacă este activ, dacă a mai fost folosit recent și dacă parola îi expiră.
- **Sursa tehnică:** `LDAP: membrii recursivi ai grupului Group Policy Creator Owners (-520); userAccountControl, lastLogonTimestamp`
- **Cum se citește rezultatul:** De verificat = comparați numele cu lista aprobată. Neconform = există membri dezactivați, nefolosiți de mult sau cu parolă care nu expiră; trebuie scoși sau corectați.

### DM14

- **Ce verificăm:** Cine este membru (direct sau prin alte grupuri) al grupului privilegiat „Administrators”.
- **De ce contează:** Membrii acestui grup pot controla tot domeniul sau o parte importantă din el. Lista trebuie să fie scurtă și aprobată în scris.
- **Cum verifică aplicația:** A citit membrii grupului, inclusiv pe cei primiți prin alte grupuri, și a verificat la fiecare dacă este activ, dacă a mai fost folosit recent și dacă parola îi expiră.
- **Sursa tehnică:** `LDAP: membrii recursivi ai grupului Administrators (S-1-5-32-544); userAccountControl, lastLogonTimestamp`
- **Cum se citește rezultatul:** De verificat = comparați numele cu lista aprobată. Neconform = există membri dezactivați, nefolosiți de mult sau cu parolă care nu expiră; trebuie scoși sau corectați.

### DM15

- **Ce verificăm:** Cine este membru (direct sau prin alte grupuri) al grupului privilegiat „Account Operators”.
- **De ce contează:** Membrii acestui grup pot controla tot domeniul sau o parte importantă din el. Lista trebuie să fie scurtă și aprobată în scris.
- **Cum verifică aplicația:** A citit membrii grupului, inclusiv pe cei primiți prin alte grupuri, și a verificat la fiecare dacă este activ, dacă a mai fost folosit recent și dacă parola îi expiră.
- **Sursa tehnică:** `LDAP: membrii recursivi ai grupului Account Operators (S-1-5-32-548); userAccountControl, lastLogonTimestamp`
- **Cum se citește rezultatul:** De verificat = comparați numele cu lista aprobată. Neconform = există membri dezactivați, nefolosiți de mult sau cu parolă care nu expiră; trebuie scoși sau corectați.

### DM16

- **Ce verificăm:** Cine este membru (direct sau prin alte grupuri) al grupului privilegiat „Server Operators”.
- **De ce contează:** Membrii acestui grup pot controla tot domeniul sau o parte importantă din el. Lista trebuie să fie scurtă și aprobată în scris.
- **Cum verifică aplicația:** A citit membrii grupului, inclusiv pe cei primiți prin alte grupuri, și a verificat la fiecare dacă este activ, dacă a mai fost folosit recent și dacă parola îi expiră.
- **Sursa tehnică:** `LDAP: membrii recursivi ai grupului Server Operators (S-1-5-32-549); userAccountControl, lastLogonTimestamp`
- **Cum se citește rezultatul:** De verificat = comparați numele cu lista aprobată. Neconform = există membri dezactivați, nefolosiți de mult sau cu parolă care nu expiră; trebuie scoși sau corectați.

### DM17

- **Ce verificăm:** Cine este membru (direct sau prin alte grupuri) al grupului privilegiat „Print Operators”.
- **De ce contează:** Membrii acestui grup pot controla tot domeniul sau o parte importantă din el. Lista trebuie să fie scurtă și aprobată în scris.
- **Cum verifică aplicația:** A citit membrii grupului, inclusiv pe cei primiți prin alte grupuri, și a verificat la fiecare dacă este activ, dacă a mai fost folosit recent și dacă parola îi expiră.
- **Sursa tehnică:** `LDAP: membrii recursivi ai grupului Print Operators (S-1-5-32-550); userAccountControl, lastLogonTimestamp`
- **Cum se citește rezultatul:** De verificat = comparați numele cu lista aprobată. Neconform = există membri dezactivați, nefolosiți de mult sau cu parolă care nu expiră; trebuie scoși sau corectați.

### DM18

- **Ce verificăm:** Cine este membru (direct sau prin alte grupuri) al grupului privilegiat „Backup Operators”.
- **De ce contează:** Membrii acestui grup pot controla tot domeniul sau o parte importantă din el. Lista trebuie să fie scurtă și aprobată în scris.
- **Cum verifică aplicația:** A citit membrii grupului, inclusiv pe cei primiți prin alte grupuri, și a verificat la fiecare dacă este activ, dacă a mai fost folosit recent și dacă parola îi expiră.
- **Sursa tehnică:** `LDAP: membrii recursivi ai grupului Backup Operators (S-1-5-32-551); userAccountControl, lastLogonTimestamp`
- **Cum se citește rezultatul:** De verificat = comparați numele cu lista aprobată. Neconform = există membri dezactivați, nefolosiți de mult sau cu parolă care nu expiră; trebuie scoși sau corectați.

### DM20

- **Ce verificăm:** Ce conturi active nu au mai fost folosite de mult timp (implicit 90 de zile).
- **De ce contează:** Un cont uitat, dar activ, poate fi folosit de un fost angajat sau de un atacator fără ca titularul să observe.
- **Cum verifică aplicația:** A comparat data ultimei autentificări a fiecărui cont activ cu data colectării.
- **Sursa tehnică:** `LDAP: lastLogonTimestamp (se actualizează cu o întârziere de până la 14 zile)`
- **Cum se citește rezultatul:** Conform = niciun cont. De verificat = conturi nefolosite; dezactivați-le dacă nu mai sunt necesare.

### DM21

- **Ce verificăm:** Când a fost schimbată ultima dată parola contului krbtgt (contul care semnează toate biletele Kerberos din domeniu).
- **De ce contează:** Cine a aflat vreodată această parolă poate crea bilete false pentru orice cont („golden ticket”), până când parola este schimbată.
- **Cum verifică aplicația:** A citit data ultimei schimbări a parolei contului krbtgt.
- **Sursa tehnică:** `LDAP: pwdLastSet pe contul krbtgt`
- **Cum se citește rezultatul:** Conform = schimbată în ultimele 180 de zile. Neconform = mai veche. Schimbarea se face de două ori, la distanță de timpul de replicare, de un administrator al domeniului.

### DM22

- **Ce verificăm:** Ce calculatoare sau conturi (în afara controlerelor de domeniu) au voie să folosească în numele altora biletele oricui se conectează la ele.
- **De ce contează:** Dacă un astfel de calculator este compromis, atacatorul poate prelua conturile tuturor celor care se conectează la el, inclusiv ale administratorilor.
- **Cum verifică aplicația:** A citit indicatorul „de încredere pentru delegare” al conturilor active, fără controlerele de domeniu.
- **Sursa tehnică:** `LDAP: userAccountControl, indicatorul TRUSTED_FOR_DELEGATION (0x80000)`
- **Cum se citește rezultatul:** Conform = niciun cont. Neconform = conturi cu delegare neconstrânsă; trebuie înlocuită cu delegare constrânsă.

### DM23

- **Ce verificăm:** Ce conturi au fost cândva administratori și au păstrat drepturile speciale de protecție după ce au fost scoase din grupuri.
- **De ce contează:** Aceste conturi rămân cu setări speciale moștenite; de obicei sunt urme ale unor drepturi date temporar și uitate.
- **Cum verifică aplicația:** A căutat conturile active cu adminCount = 1 care nu mai sunt în niciun grup privilegiat.
- **Sursa tehnică:** `LDAP: adminCount; membrii recursivi ai grupurilor privilegiate`
- **Cum se citește rezultatul:** Apare doar când există conturi: De verificat; cereți administratorului să confirme că drepturile nu mai sunt necesare și să curețe contul.

### DM24

- **Ce verificăm:** Ce calculatoare active nu s-au mai conectat la domeniu de mult timp (implicit 90 de zile).
- **De ce contează:** Un calculator dispărut (pierdut, furat, scos din uz) care rămâne activ în domeniu poate fi folosit pentru acces.
- **Cum verifică aplicația:** A comparat data ultimei conectări a fiecărui calculator activ (fără controlerele de domeniu) cu data colectării.
- **Sursa tehnică:** `LDAP: lastLogonTimestamp pe obiectele computer`
- **Cum se citește rezultatul:** Conform = niciunul. De verificat = calculatoare nefolosite; aflați unde sunt fizic și dezactivați-le dacă nu mai există.

### DM25

- **Ce verificăm:** Ce calculatoare din domeniu rulează sisteme de operare care nu mai primesc actualizări de securitate (de exemplu Windows 7, Windows Server 2008 sau 2012).
- **De ce contează:** Pe aceste sisteme defectele de securitate cunoscute nu se mai repară niciodată.
- **Cum verifică aplicația:** A citit sistemul de operare declarat de fiecare calculator activ.
- **Sursa tehnică:** `LDAP: operatingSystem pe obiectele computer`
- **Cum se citește rezultatul:** Apare doar când există calculatoare: Neconform; fiecare trebuie actualizat, izolat sau scos din uz, cu o decizie scrisă.

### DM30

- **Ce verificăm:** Câte caractere trebuie să aibă cel puțin o parolă în domeniu, câte parole vechi nu pot fi refolosite și când expiră parola.
- **De ce contează:** Parolele scurte se ghicesc ușor.
- **Cum verifică aplicația:** A citit politica de parole a domeniului.
- **Sursa tehnică:** `LDAP: minPwdLength, pwdHistoryLength, maxPwdAge pe obiectul domeniului`
- **Cum se citește rezultatul:** Conform = cel puțin 12 caractere. De verificat = 8–11. Neconform = sub 8.

### DM31

- **Ce verificăm:** După câte parole greșite se blochează un cont din domeniu.
- **De ce contează:** Fără blocare, cineva poate încerca la nesfârșit parole până o nimerește.
- **Cum verifică aplicația:** A citit pragul de blocare din politica domeniului.
- **Sursa tehnică:** `LDAP: lockoutThreshold pe obiectul domeniului`
- **Cum se citește rezultatul:** Conform = blocare după cel mult 10 încercări. Neconform = conturile nu se blochează niciodată (0) sau pragul e prea mare.

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

### N03

- **Ce verificăm:** Dacă stația are adaptor Wi-Fi, dacă serviciul Wi-Fi poate porni și câte rețele Wi-Fi sunt salvate.
- **De ce contează:** Un adaptor Wi-Fi poate crea o legătură cu exteriorul și strică izolarea stației.
- **Cum verifică aplicația:** A căutat adaptoarele de rețea fără fir, a citit modul de pornire al serviciului Wi-Fi și a numărat fișierele de profil Wi-Fi salvate (fără să le deschidă).
- **Sursa tehnică:** `Adaptoare de rețea Windows (tip 802.11); HKLM\SYSTEM\CurrentControlSet\Services\WlanSvc → Start; ProgramData\Microsoft\Wlansvc\Profiles\Interfaces`
- **Cum se citește rezultatul:** Conform = nu există adaptor Wi-Fi. Neconform = pe stația izolată există un adaptor Wi-Fi pornit. De verificat = adaptor prezent dar oprit, sau stație neizolată; cereți aprobarea scrisă sau scoaterea plăcii.

### N04

- **Ce verificăm:** Dacă stația are componente Bluetooth și dacă serviciul Bluetooth poate porni.
- **De ce contează:** Prin Bluetooth se pot trimite fișiere fără cablu, fără ca stația să fie conectată la vreo rețea.
- **Cum verifică aplicația:** A căutat dispozitivele Bluetooth cunoscute de Windows și a citit modul de pornire al serviciului Bluetooth.
- **Sursa tehnică:** `WMI Win32_PnPEntity (clasa Bluetooth); HKLM\SYSTEM\CurrentControlSet\Services\bthserv → Start`
- **Cum se citește rezultatul:** Conform = nu există Bluetooth. Neconform = pe stația izolată Bluetooth există și serviciul poate porni. De verificat = există dar serviciul este oprit; opriți-l sau cereți aprobarea scrisă.

### EM01

- **Ce verificăm:** Dacă din cutia poștală au plecat 50 de mesaje sau mai multe într-o singură oră.
- **De ce contează:** Un cont de e-mail furat este folosit des pentru a trimite rapid mesaje înșelătoare către mulți destinatari, în numele titularului.
- **Cum verifică aplicația:** A numărat, pe fiecare oră, mesajele trimise de cutia poștală în perioada aleasă.
- **Sursa tehnică:** `Urmărirea mesajelor serverului de e-mail (Exchange: message trace)`
- **Cum se citește rezultatul:** Conform = nicio oră cu 50 sau mai multe mesaje. Neconform = cel puțin o oră; întrebați titularul și, dacă nu el le-a trimis, schimbați parola și închideți sesiunile.

### EM02

- **Ce verificăm:** De pe câte adrese IP diferite s-au trimis mesaje din cutia poștală.
- **De ce contează:** Mesaje trimise din locuri diferite în același timp pot arăta că altcineva folosește contul.
- **Cum verifică aplicația:** A grupat mesajele trimise după adresa IP a calculatorului care le-a trimis.
- **Sursa tehnică:** `Urmărirea mesajelor: adresa IP a clientului`
- **Cum se citește rezultatul:** Conform = cel mult 3 adrese. De verificat = mai multe; comparați adresele cu locurile de unde lucrează titularul.

### EM03

- **Ce verificăm:** Dacă în cutia poștală există reguli care trimit mesajele altcuiva, le șterg sau le mută în foldere puțin folosite.
- **De ce contează:** Cine fură un cont creează astfel de reguli ca să copieze corespondența sau ca titularul să nu vadă răspunsurile.
- **Cum verifică aplicația:** A citit regulile active ale cutiei poștale și le-a păstrat pe cele care redirecționează, șterg, mută în foldere ascunse sau au nume de 1–2 caractere.
- **Sursa tehnică:** `Exchange: Get-InboxRule`
- **Cum se citește rezultatul:** Conform = nicio regulă riscantă. De verificat = reguli interne; întrebați titularul. Neconform = o regulă trimite mesaje în afara organizației.

### EM04

- **Ce verificăm:** Dacă toată corespondența cutiei poștale este copiată automat către altă adresă.
- **De ce contează:** O redirecționare automată scoate din organizație fiecare mesaj primit, fără ca cineva să observe.
- **Cum verifică aplicația:** A citit setările de redirecționare ale cutiei poștale.
- **Sursa tehnică:** `Exchange: ForwardingSmtpAddress, ForwardingAddress, DeliverToMailboxAndForward`
- **Cum se citește rezultatul:** Conform = nicio redirecționare. De verificat = către o adresă internă. Neconform = către o adresă din afara organizației.

### EM05

- **Ce verificăm:** Câte mesaje au fost trimise către adrese din afara organizației și către ce domenii.
- **De ce contează:** Arată unde ar fi putut ajunge informațiile trimise pe e-mail.
- **Cum verifică aplicația:** A comparat destinatarii mesajelor trimise cu domeniile interne declarate.
- **Sursa tehnică:** `Urmărirea mesajelor: destinatari; domeniile interne introduse de controlor`
- **Cum se citește rezultatul:** Apare doar când există astfel de mesaje: De verificat; comparați domeniile cu partenerii cunoscuți ai organizației.

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

### S03

- **Ce verificăm:** Ce versiune de Windows rulează stația și când s-a instalat ultima actualizare.
- **De ce contează:** O versiune care nu mai primește actualizări de securitate rămâne cu defecte cunoscute; pe o stație izolată actualizările se aduc manual, deci data lor trebuie să corespundă procedurii.
- **Cum verifică aplicația:** A citit numele, versiunea și numărul de build al Windows din registru și cea mai recentă dată de instalare a unei actualizări din lista Windows.
- **Sursa tehnică:** `HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion → DisplayVersion, CurrentBuild, UBR; WMI Win32_QuickFixEngineering → InstalledOn`
- **Cum se citește rezultatul:** Întotdeauna „de verificat”: aplicația arată faptele, dar nu are tabelul oficial al versiunilor încă suportate. Compară cu registrul actualizărilor offline al organizației. Numele din registru poate spune „Windows 10” și pe Windows 11; un build de la 22000 în sus este Windows 11.

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
| Storage\\HotPlugSecureOpen | valoare de registru | Setare folosită de unele versiuni de Windows pentru a înregistra accesul la fișiere pe suporturi amovibile. | Fără ea, auditarea pe suporturi amovibile poate să nu noteze fișierele copiate. | 1 = pornită; lipsa înseamnă comportamentul implicit al versiunii. |
| Win32_UserAccount | interogare Windows (WMI) | Lista conturilor locale ale stației, cu starea lor (activ, dezactivat, parolă obligatorie, expirare). | Este lista reală a conturilor, chiar dacă unele sunt ascunse din ecranul de pornire. |  |
| S-1-5-32-544 | identificator de securitate (SID) | Administrators: grupul administratorilor locali: control total. | SID-ul identifică sigur contul sau grupul, chiar dacă a fost redenumit. |  |
| Win32_Group | interogare Windows (WMI) | Grupurile locale ale stației și membrii lor. | Arată cine are drepturi de administrator sau de acces la distanță. |  |
| S-1-5-32-555 | identificator de securitate (SID) | Remote Desktop Users: cine are voie să lucreze de la distanță. | SID-ul identifică sigur contul sau grupul, chiar dacă a fost redenumit. |  |
| Partition 1006 | eveniment Windows | Un disc a fost conectat la stație; jurnalul păstrează producătorul, modelul, seria și capacitatea. | Este cea mai bună urmă a unui stick USB sau disc extern conectat, cu numărul de serie de comparat cu registrul suporturilor. | BusType 7 = USB, 12 = card SD, 13 = card MMC. |
| Enum\\USBSTOR | cheie de registru | Lista tuturor dispozitivelor de stocare USB conectate vreodată la stație, cu seria lor. | Arată ce stick-uri au fost folosite, chiar dacă jurnalele s-au pierdut. | Ștergerea acestei liste este o urmă de ascundere. |
| PASSWD_NOTREQD | indicator de cont | Contul are voie să aibă parolă goală. | Un cont fără parolă poate fi folosit de oricine. | Indicatorul nu dovedește că parola chiar este goală; se verifică. |
| NetworkProfile 10000 | eveniment Windows | Stația s-a conectat la o rețea (numele rețelei este în eveniment). | Pe o stație izolată, orice conectare la rețea este o abatere. |  |
| WLAN 8001 | eveniment Windows | Stația s-a conectat la o rețea fără fir (Wi-Fi); SSID = numele rețelei. | Pe o stație izolată, o conectare Wi-Fi este o abatere. |  |
| NetworkList\\Profiles | cheie de registru | Lista rețelelor la care stația s-a conectat vreodată, cu data primei și ultimei conectări. | Pe o stație izolată arată dacă a fost vreodată conectată la o rețea. | Profilurile pot fi șterse manual; lipsa lor nu dovedește nimic. |
| Services\\WlanSvc\\Start | valoare de registru | Modul de pornire al serviciului Wi-Fi (WLAN AutoConfig). | Fără serviciu pornit, stația nu se poate conecta la rețele fără fir. | 4 = dezactivat, 3 = la cerere, 2 = automat; lipsa înseamnă că serviciul nu există. |
| Services\\bthserv\\Start | valoare de registru | Modul de pornire al serviciului Bluetooth. | Bluetooth permite trimiterea de fișiere fără cablu. | 4 = dezactivat, 3 = la cerere, 2 = automat. |
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
| State\\UEFISecureBootEnabled | valoare de registru | Dacă pornirea securizată (Secure Boot) este activă. | Fără ea, stația poate fi pornită de pe un stick cu alt sistem de operare. | 1 = activă, 0 = oprită; lipsa cheii înseamnă de obicei BIOS vechi sau stație fără UEFI. |
| Control\\PEFirmwareType | valoare de registru | Tipul de firmware al plăcii de bază. | BIOS-ul vechi nu are protecțiile pornirii moderne. | 1 = BIOS vechi (Legacy), 2 = UEFI (modern). |
| System\\InactivityTimeoutSecs | valoare de registru | După câte secunde de inactivitate se blochează ecranul. | Ecranul deschis lângă o stație nesupravegheată poate fi folosit de oricine. | Cel mult 900 (15 minute) este corect; 0 = dezactivat. |
| Services\\AppIDSvc\\Start | valoare de registru | Modul de pornire al serviciului care aplică lista albă de programe (AppLocker). | Dacă este dezactivat, regulile AppLocker nu mai sunt aplicate. | 4 = dezactivat, 3 = la cerere (normal), 2 = automat. |
| SrpV2\\EnforcementMode | valoare de registru | Modul unei colecții de reguli AppLocker (lista albă de programe). | Doar modul de blocare oprește programele neaprobate. | 1 = blochează, 0 = doar notează în jurnal (audit). |
| Explorer\\NoDriveTypeAutoRun | valoare de registru | Pentru ce tipuri de unități este oprită pornirea automată. | Un stick poate porni singur un program la introducere. | 255 = toate unitățile; trebuie să includă cel puțin suporturile amovibile (4) și CD-urile (32). |
| Explorer\\NoAutorun | valoare de registru | Oprește comenzile de pornire automată din fișierul autorun.inf. | Fișierul autorun.inf al unui stick poate porni un program. | 1 = oprit (corect), 0 sau lipsă = permis. |
| webcam\\Value | valoare de registru | Permisiunea implicită de acces la cameră pentru toate aplicațiile. | O cameră permisă poate face imagini din încăpere. | Deny = blocat, Allow = permis. |
| microphone\\Value | valoare de registru | Permisiunea implicită de acces la microfon pentru toate aplicațiile. | Un microfon permis poate înregistra conversații. | Deny = blocat, Allow = permis. |
| WindowsAI\\DisableAIDataAnalysis | valoare de registru | Politica care oprește funcția Recall (poze periodice ale ecranului). | Recall păstrează pe disc imagini ale ecranului, deci copii ale documentelor. | 1 = oprit (corect), 0 = permis. |
| WindowsAI\\AllowRecallEnablement | valoare de registru | Politica care permite sau nu activarea funcției Recall. | Vezi DisableAIDataAnalysis. | 0 = indisponibilă (corect), 1 = poate fi activată. |
| WindowsCopilot\\TurnOffWindowsCopilot | valoare de registru | Politica care oprește asistentul Windows Copilot. | Copilot poate trimite conținutul ecranului către un serviciu din afară. | 1 = oprit (corect), 0 = permis. |
| Lsa\\RunAsPPL | valoare de registru | Protecția procesului care păstrează parolele (LSA). | Îngreunează furtul parolelor din memorie. | 1 sau 2 = protejat. |
| ScriptBlockLogging\\EnableScriptBlockLogging | valoare de registru | Înregistrarea textului scripturilor PowerShell în jurnal. | Fără ea, un script rău-intenționat nu lasă urme. | 1 = pornită (corect), lipsa sau 0 = oprită. |
| Transcription\\EnableTranscripting | valoare de registru | Salvarea unei transcrieri a fiecărei sesiuni PowerShell. | Păstrează tot ce s-a tastat și tot ce s-a afișat. | 1 = pornită (corect). |
| PowerShellEngine\\PowerShellVersion | valoare de registru | Versiunea motorului PowerShell găsit sub această cheie. | Versiunea 2.0 nu înregistrează scripturile și poate ocoli protecțiile noi. | 2.0 = versiunea veche prezentă; lipsa cheii = nu s-a găsit. |
| Memory Management\\ClearPageFileAtShutdown | valoare de registru | Dacă fișierul de paginare (memoria virtuală de pe disc) se șterge la oprire. | Poate conține fragmente de documente deschise. | 1 = se șterge (corect pentru stații clasificate), 0 = rămâne pe disc. |
| Power\\HibernateEnabled | valoare de registru | Dacă hibernarea este permisă. | Hibernarea scrie pe disc tot conținutul memoriei, inclusiv documentele deschise. | 0 = oprită, 1 = permisă. |
| CrashControl\\CrashDumpEnabled | valoare de registru | Ce copie a memoriei salvează Windows la o cădere a sistemului. | Copia poate conține date din documente deschise. | 0 = nimic, 1 = completă, 2 = nucleu, 3 = mică, 7 = automată. |
| System\\LegalNoticeCaption | valoare de registru | Titlul mesajului de avertizare afișat înainte de autentificare. | Anunță utilizatorul că stația tratează informații clasificate. | Trebuie să corespundă marcajului de clasificare aprobat. |
| System\\LegalNoticeText | valoare de registru | Textul mesajului de avertizare afișat înainte de autentificare. | Vezi LegalNoticeCaption. | Trebuie să indice nivelul maxim de clasificare. |
| Parameters\\NtpServer | valoare de registru | Serverul de timp configurat pentru ceasul stației. | Ora din jurnale este de încredere doar dacă ceasul este sincronizat cu o sursă aprobată. | Lipsa înseamnă că niciun server nu este configurat. |
| Parameters\\Type | valoare de registru | Tipul de sincronizare a ceasului stației. | Un ceas lăsat liber se abate în timp. | NTP = server de timp, NT5DS = domeniu, NoSync = fără sincronizare. |
| Application 11707 | eveniment Windows | Windows Installer: un program a fost instalat cu succes. | Software-ul instalat trebuie să fie pe lista aprobată. |  |
| Application 11724 | eveniment Windows | Windows Installer: un program a fost dezinstalat cu succes. | Dezinstalarea poate ascunde un program folosit anterior. |  |
| Application 1033 | eveniment Windows | Windows Installer: instalare încheiată (cu codul de rezultat). | Software-ul instalat trebuie să fie pe lista aprobată. | Codul de stare 0 = reușit. |
| Application 1034 | eveniment Windows | Windows Installer: dezinstalare încheiată. | Dezinstalarea poate ascunde un program folosit anterior. |  |
| System 7045 | eveniment Windows | Un serviciu nou (program care rulează permanent în fundal) a fost instalat. | Serviciile noi sunt o metodă frecventă de a păstra un program rău-intenționat activ după repornire. | Calea executabilului (ImagePath) arată ce program rulează. |
| Security 4698 | eveniment Windows | A fost creat un task programat (o comandă care pornește singură la o oră sau la pornire). | Task-urile programate sunt o metodă frecventă prin care un program rău-intenționat repornește singur. | Programele legitime (actualizări) creează și ele task-uri; comparați numele cu software-ul aprobat. |
| CurrentVersion\\DisplayVersion | valoare de registru | Versiunea Windows pe înțelesul utilizatorilor (de exemplu 24H2). | Arată cât de nouă este versiunea și dacă mai primește actualizări. | Se compară cu tabelul versiunilor suportate al organizației. |
| CurrentVersion\\CurrentBuild | valoare de registru | Numărul de build al Windows. | Identifică exact versiunea; 22000 sau mai mult înseamnă Windows 11. | Numele produsului din registru poate spune „Windows 10” și pe Windows 11. |
| CurrentVersion\\UBR | valoare de registru | Numărul de revizie al actualizărilor instalate peste build. | Crește cu fiecare actualizare lunară instalată. | Se citește împreună cu build-ul: 26100.1234 înseamnă revizia 1234. |
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
