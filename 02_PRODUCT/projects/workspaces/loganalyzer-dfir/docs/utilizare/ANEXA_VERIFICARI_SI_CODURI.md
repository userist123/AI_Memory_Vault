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

- **Ce verificăm:** Dacă UAC este pornit și chiar întreabă (fereastra care cere confirmare înainte ca un program să primească drepturi de administrator), pe ecranul securizat.
- **De ce contează:** Fără UAC, sau cu UAC care dă drepturile fără întrebare, orice program pornit de un administrator — inclusiv unul rău-intenționat — primește control total pe neobservate.
- **Cum verifică aplicația:** A citit din registru dacă UAC este pornit (EnableLUA), cum întreabă un administrator (ConsentPromptBehaviorAdmin) și dacă întrebarea apare pe ecranul securizat (PromptOnSecureDesktop).
- **Sursa tehnică:** `HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System → EnableLUA, ConsentPromptBehaviorAdmin, PromptOnSecureDesktop`
- **Cum se citește rezultatul:** Conform = EnableLUA = 1, ConsentPromptBehaviorAdmin între 1 și 5 (se cere parola sau confirmarea) și PromptOnSecureDesktop = 1. Neconform = EnableLUA = 0 (UAC oprit) sau ConsentPromptBehaviorAdmin = 0 (drepturile se dau fără nicio întrebare). De verificat = întrebarea apare pe ecranul obișnuit (PromptOnSecureDesktop = 0) sau una dintre valori nu a putut fi citită. Nedeterminat = EnableLUA nu a putut fi citită.

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
- **Cum se citește rezultatul:** Conform = toate colecțiile blochează. Neconform = lista cere blocare, dar serviciul care o aplică este dezactivat. De verificat = lipsește, este doar în mod audit sau fără mod. Lista albă App Control (WDAC) este verificată separat, la P37.

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

### P28

- **Ce verificăm:** Dacă stația poate scrie CD/DVD-uri și dacă scrierea este oprită la nivel de politică, nu doar în Explorer.
- **De ce contează:** Un disc inscripționat este un mod simplu de a scoate date și nu apare ca stick USB; politica NoCDBurning oprește doar inscripționarea din Explorer, nu un program de inscripționare.
- **Cum verifică aplicația:** A căutat unitățile optice cu capacitate de scriere și a citit politica de refuzare a scrierii pe CD/DVD și politica NoCDBurning.
- **Sursa tehnică:** `WMI Win32_CDROMDrive → Capabilities; HKLM\SOFTWARE\Policies\Microsoft\Windows\RemovableStorageDevices\{53f56308-b6bf-11d0-94f2-00a0c91efb8b} → Deny_Write; ...\Policies\Explorer → NoCDBurning`
- **Cum se citește rezultatul:** Conform = nicio unitate nu scrie, sau scrierea este refuzată prin politică. Neconform = unitatea poate scrie și nu există blocare (sau doar NoCDBurning, care nu este suficient). De verificat = la o stație neizolată.

### P29

- **Ce verificăm:** Dacă stația înregistrează ce se tipărește și dacă are imprimante de rețea.
- **De ce contează:** Tipărirea este una dintre căile prin care iese informație de pe o stație; fără jurnal nu se poate afla ce documente s-au tipărit.
- **Cum verifică aplicația:** A citit dacă jurnalul PrintService/Operational este pornit (în Windows este oprit implicit) și lista imprimantelor, cu portul fiecăreia.
- **Sursa tehnică:** `Canalul Microsoft-Windows-PrintService/Operational (activat); WMI Win32_Printer → PortName, Network`
- **Cum se citește rezultatul:** Conform = fără imprimante, sau jurnalul este pornit și nu există imprimante de rețea. Neconform = pe stația izolată jurnalul este oprit sau există imprimantă de rețea. Nedeterminat = starea jurnalului nu s-a putut citi.

### P30

- **Ce verificăm:** Dacă istoricul clipboard și istoricul de activitate Windows (Timeline) sunt oprite.
- **De ce contează:** Ele păstrează pe disc fragmente din ce a fost copiat sau deschis, deci pot reține text dintr-un document clasificat în afara registrelor.
- **Cum verifică aplicația:** A citit setarea istoricului clipboard (utilizatorul curent și politica), politica istoricului de activitate și dacă baza ActivitiesCache.db există.
- **Sursa tehnică:** `HKCU\Software\Microsoft\Clipboard → EnableClipboardHistory; HKLM\SOFTWARE\Policies\Microsoft\Windows\System → AllowClipboardHistory, EnableActivityFeed`
- **Cum se citește rezultatul:** Conform = ambele oprite. Neconform = istoricul clipboard este pornit sau activitatea este permisă explicit, pe o stație izolată. De verificat = activitatea rămâne în regimul implicit al Windows, nesetată prin politică.

### P31

- **Ce verificăm:** Ce camere, microfoane și ieșiri audio sunt active pe stație și dacă politica le blochează aplicațiilor.
- **De ce contează:** O cameră poate fotografia ecranul, un microfon poate înregistra încăperea, iar difuzoarele pot transmite sunete în afara stației.
- **Cum verifică aplicația:** A listat dispozitivele din clasele Camera, Image, MEDIA și AudioEndpoint cu starea lor și a citit permisiunea de acces la cameră și microfon (aceeași ca la P19).
- **Sursa tehnică:** `WMI Win32_PnPEntity → PNPClass, ConfigManagerErrorCode; HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\webcam și microphone → Value`
- **Cum se citește rezultatul:** Conform = niciun dispozitiv activ. Neconform = pe stația izolată există cameră sau microfon activ, iar accesul aplicațiilor nu este blocat. De verificat = politica blochează, dar dispozitivul rămâne pornit în driver; acoperirea fizică se verifică la M09.

### P32

- **Ce verificăm:** Dacă fiecare disc fix al stației (nu doar C:) este criptat cu BitLocker și dacă discul de sistem cere un PIN la pornire.
- **De ce contează:** Un disc necriptat poate fi scos și citit pe alt calculator; fără PIN, un calculator furat întreg pornește singur și discul de sistem se deblochează.
- **Cum verifică aplicația:** A cerut Windows starea BitLocker a fiecărui volum cu literă și tipurile de protecție ale fiecăruia (de exemplu „TPM și PIN”, „parolă de recuperare”). Volumele fără literă (partiția de recuperare) nu sunt evaluate.
- **Sursa tehnică:** `WMI root\cimv2\Security\MicrosoftVolumeEncryption Win32_EncryptableVolume → ProtectionStatus, ConversionStatus; metodele GetKeyProtectors / GetKeyProtectorType (doar citire)`
- **Cum se citește rezultatul:** Conform = toate discurile fixe sunt protejate și discul de sistem are protecția „TPM și PIN”. Neconform = un disc fix nu este criptat sau discul de sistem pornește fără PIN. De verificat = tipul protecției nu a putut fi citit. Nedeterminat = necesită administrator.

### P33

- **Ce verificăm:** Dacă stația are cipul de securitate TPM, dacă este pornit și pregătit, și ce versiune are.
- **De ce contează:** Cipul TPM păstrează cheile de criptare legate de placa de bază; fără el criptarea discului este mai slabă.
- **Cum verifică aplicația:** A întrebat Windows starea cipului TPM: dacă există, dacă este pornit și activat, dacă este pregătit de folosire și versiunea.
- **Sursa tehnică:** `WMI root\CIMV2\Security\MicrosoftTpm Win32_Tpm → IsEnabled_InitialValue, IsActivated_InitialValue, SpecVersion; metoda IsReady`
- **Cum se citește rezultatul:** Conform = TPM 2.0 prezent, pornit și pregătit. Neconform = lipsește, este oprit sau nu este pregătit. De verificat = versiunea veche 1.2 sau starea „pregătit” necunoscută. Nedeterminat = necesită administrator.

### P34

- **Ce verificăm:** Dacă stația are pornită protecția memoriei la porturile Thunderbolt și USB4 (Kernel DMA Protection).
- **De ce contează:** Un dispozitiv conectat la un astfel de port poate citi direct memoria stației, inclusiv parole și documente deschise, chiar cu ecranul blocat.
- **Cum verifică aplicația:** A citit starea protecției DMA așa cum o afișează Windows în „Informații sistem” și politica pentru dispozitivele externe care nu suportă protecția.
- **Sursa tehnică:** `Windows (informația afișată de msinfo32 ca „Kernel DMA Protection”); HKLM\SOFTWARE\Policies\Microsoft\Windows\Kernel DMA Protection → DeviceEnumerationPolicy`
- **Cum se citește rezultatul:** Conform = pornită. Neconform = oprită pe o stație izolată. De verificat = oprită pe o stație neizolată, sau pornită dar politica permite toate dispozitivele. Dacă stația nu are porturi Thunderbolt/USB4, notați acest lucru. Metoda de citire trebuie confirmată în laborator.

### P35

- **Ce verificăm:** Dacă securitatea bazată pe virtualizare (VBS) rulează pe stație.
- **De ce contează:** Ea izolează într-o zonă protejată a memoriei parolele (Credential Guard) și verificarea driverelor; fără ea aceste protecții nu pot funcționa.
- **Cum verifică aplicația:** A cerut Windows starea securității bazate pe virtualizare.
- **Sursa tehnică:** `WMI root\Microsoft\Windows\DeviceGuard Win32_DeviceGuard → VirtualizationBasedSecurityStatus`
- **Cum se citește rezultatul:** Conform = rulează (2). Neconform = este cerută, dar nu rulează (1), de obicei pentru că virtualizarea este oprită din BIOS. De verificat = nu este pornită (0).

### P36

- **Ce verificăm:** Dacă stația cere parola când revine din somn.
- **De ce contează:** Dacă nu cere, oricine trezește o stație lăsată în somn ajunge direct în sesiunea deschisă a utilizatorului.
- **Cum verifică aplicația:** A citit politica „parolă la trezire” (în priză și pe baterie), valoarea din planul de alimentare activ și setarea „Solicitare autentificare” a utilizatorului care a pornit aplicația.
- **Sursa tehnică:** `HKLM\SOFTWARE\Policies\Microsoft\Power\PowerSettings\0e796bdb-100d-47d6-a2d5-f7d2daa51f51 → ACSettingIndex, DCSettingIndex; planul de alimentare activ (CONSOLELOCK); HKCU\Control Panel\Desktop → DelayLockInterval`
- **Cum se citește rezultatul:** Conform = politica cere parola în ambele cazuri. Neconform = politica sau setarea stației nu cere parola. De verificat = parola se cere acum, dar nu prin politică (utilizatorul o poate schimba).

### P37

- **Ce verificăm:** Dacă stația are lista albă App Control for Business (WDAC) și dacă ea blochează sau doar notează.
- **De ce contează:** Cu lista albă în mod de blocare pe stație pornesc doar programele aprobate; un program adus pe un stick nu mai poate rula.
- **Cum verifică aplicația:** A cerut Windows modul în care se aplică App Control pentru programe și a numărat fișierele de politică active (fără să le deschidă).
- **Sursa tehnică:** `WMI Win32_DeviceGuard → UsermodeCodeIntegrityPolicyEnforcementStatus, CodeIntegrityPolicyEnforcementStatus; C:\Windows\System32\CodeIntegrity\CiPolicies\Active`
- **Cum se citește rezultatul:** Conform = blochează (2). De verificat = doar mod audit (1), sau nu se folosește dar AppLocker blochează. Neconform = nici App Control, nici AppLocker nu blochează programele neaprobate.

### P38

- **Ce verificăm:** Când a făcut antivirusul ultima scanare completă și ultima scanare rapidă a stației.
- **De ce contează:** Semnăturile la zi nu ajută dacă discul nu este scanat: un program adus înainte de actualizare rămâne nedescoperit.
- **Cum verifică aplicația:** A citit din Microsoft Defender data de sfârșit a ultimei scanări complete și a ultimei scanări rapide.
- **Sursa tehnică:** `MSFT_MpComputerStatus → FullScanEndTime, QuickScanEndTime`
- **Cum se citește rezultatul:** Conform = scanare completă în ultimele 30 de zile și rapidă în ultimele 7. Neconform = nicio scanare completă, niciodată. De verificat = scanări mai vechi; comparați cu frecvența din procedură. Dacă stația folosește alt antivirus, datele lui se verifică în consola lui.

### P39

- **Ce verificăm:** Ce antivirus este înregistrat în Centrul de securitate Windows (inclusiv al altui producător) și dacă este pornit, cu semnături la zi.
- **De ce contează:** Dacă stația folosește alt antivirus decât Defender, starea lui reală se vede doar aici.
- **Cum verifică aplicația:** A citit lista produselor antivirus înregistrate în Windows și starea raportată de fiecare.
- **Sursa tehnică:** `WMI root\SecurityCenter2 AntiVirusProduct → displayName, productState`
- **Cum se citește rezultatul:** Conform = cel puțin un antivirus pornit, cu semnături la zi. Neconform = niciun antivirus înregistrat sau toate oprite. De verificat = pornit, dar cu semnături vechi. Decodarea stării nu este documentată oficial de Microsoft; confirmați vizual în consola antivirusului.

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
- **Cum se citește rezultatul:** Conform = nicio schimbare în perioadă. De verificat: fiecare schimbare trebuie să fie aprobată; în probe codul GUID arată categoria schimbată. Nedeterminat = jurnalul Security nu a putut fi citit.

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

### A09

- **Ce verificăm:** Câte programe neaprobate au fost oprite (sau ar fi fost oprite) de lista albă în perioada controlată, și care anume.
- **De ce contează:** Fiecare încercare arată că cineva a vrut să pornească un program care nu este aprobat pe stație.
- **Cum verifică aplicația:** A numărat în jurnalele AppLocker și CodeIntegrity evenimentele de blocare și de audit din perioada controlată și a listat fișierele.
- **Sursa tehnică:** `AppLocker 8003, 8004, 8006, 8007; CodeIntegrity 3076, 3077`
- **Cum se citește rezultatul:** Conform = lista albă există și nu s-a înregistrat nicio încercare. De verificat = există încercări (aflați cine și de ce) sau stația nu are listă albă, deci nu pot exista urme. Nedeterminat = jurnalele nu au putut fi citite.

### A10

- **Ce verificăm:** Dacă pe fiecare folder clasificat din profilul de proceduri este setat ce acces la fișiere se înregistrează (reguli de audit, SACL).
- **De ce contează:** Fără reguli de audit pe folder, nimeni nu va ști cine a deschis, a copiat sau a șters documentele clasificate.
- **Cum verifică aplicația:** A citit regulile de audit ale fiecărui folder din tabelul „Foldere clasificate” al profilului și politica de audit pentru subcategoria „File System”. Citirea regulilor cere drepturi de administrator; folderele nu se modifică.
- **Sursa tehnică:** `Regulile de audit ale folderului (echivalentul Get-Acl -Audit); auditpol: subcategoria File System`
- **Cum se citește rezultatul:** Conform = fiecare folder are reguli de audit și „File System” este auditată. Neconform = un folder nu are reguli sau „File System” nu este auditată. De verificat = un folder din profil nu există. Nedeterminat = profilul nu definește foldere clasificate (se completează tabelul „Foldere clasificate”) sau regulile nu au putut fi citite (administrator).

### A11

- **Ce verificăm:** Dacă jurnalul propriu al aplicației (cine a pornit controale, ce s-a exportat, ce s-a schimbat în profil) este același pe care l-a văzut controlul anterior, doar cu înregistrări adăugate după el.
- **De ce contează:** Un administrator al stației poate rescrie jurnalul aplicației și îl poate sigila din nou, pentru că are acces la cheia de sigilare de pe stație. Singurul lucru pe care nu-l poate schimba este codul de ancorare tipărit pe procesul-verbal semnat al controlului anterior, care se află la ofițerul de securitate.
- **Cum verifică aplicația:** La începutul controlului ați introdus codul de ancorare de pe procesul-verbal anterior. Aplicația a recalculat, pentru fiecare înregistrare din jurnalul de acum, codul ei și a căutat-o pe cea cu același cod. Fiecare înregistrare conține amprenta tuturor celor dinaintea ei, deci orice modificare a trecutului schimbă codul.
- **Sursa tehnică:** `Jurnalul de audit al aplicației (auth_audit.jsonl), lanț de amprente SHA-256; codul = 20 de caractere din SHA-256(identificator lanț | număr înregistrare | amprenta înregistrării), ultimul caracter fiind de control`
- **Cum se citește rezultatul:** Rezultatul spune și de unde vine codul: tastat de pe procesul-verbal (sigur) sau propus de stație (nesigur, trebuie confirmat cu hârtia). Conform = înregistrarea de la controlul anterior este în jurnal, neschimbată. Neconform = nicio înregistrare nu are codul de pe procesul-verbal: jurnalul a fost rescris sau înlocuit după controlul anterior. Nedeterminat = nu s-a introdus niciun cod (primul control sau procesul-verbal anterior lipsește) ori codul a fost tastat greșit. Verificarea arată doar că trecutul nu s-a schimbat; nu poate arăta ce s-a petrecut cât timp aplicația nu a rulat.

### A12

- **Ce verificăm:** Dacă serviciul Windows care scrie jurnalele (Event Log) a fost oprit fără ca acel calculator să fie închis sau repornit.
- **De ce contează:** Cât timp serviciul de jurnalizare este oprit, Windows nu înregistrează nimic: este un mod de a lucra fără urme.
- **Cum verifică aplicația:** A căutat evenimentele de oprire a serviciului de jurnalizare și, pentru fiecare, dacă a fost o închidere efectivă a calculatorului: oprirea jurnalelor la închidere în cel mult 5 minute, sau o cerere de închidere ori repornire în ultimele 30 de minute urmată de o pornire a calculatorului. O cerere de închidere anulată nu explică oprirea (Windows scrie aceeași oprire la fiecare închidere normală).
- **Sursa tehnică:** `Security 1100 (serviciul de jurnalizare s-a oprit); System 6006 (jurnalele oprite la închidere), 1074 (închidere sau repornire cerută), 6005 (pornire)`
- **Cum se citește rezultatul:** Conform = nicio oprire, sau toate la o închidere efectivă a calculatorului. De verificat = opriri fără închidere cerută înainte; cereți explicația. Nedeterminat = jurnalul Security nu a putut fi citit. Atenție: dacă serviciul este omorât forțat, Windows nu scrie nimic, deci lipsa evenimentului nu dovedește că serviciul a mers tot timpul.

### A13

- **Ce verificăm:** Dacă lista ofițerilor de securitate (cei care pot semna raportul oficial) s-a schimbat de la controlul anterior.
- **De ce contează:** Cine reușește să se treacă pe listă poate semna rapoarte „oficiale”; de aceea orice schimbare trebuie confirmată de ofițer, cu procesul-verbal în mână.
- **Cum verifică aplicația:** A citit din jurnalul aplicației, sigilat și înlănțuit, înregistrările și eliminările de ofițeri, importurile de profil care conțineau ofițeri (ignorate) și importurile de certificate de autoritate pentru semnătura calificată, scrise după înregistrarea găsită la verificarea A11 (sau, fără cod anterior, în perioada controlată).
- **Sursa tehnică:** `Jurnalul de audit al aplicației: profile.officer_registered, profile.officer_removed, profile.officer_import_ignored, officer_trust.imported`
- **Cum se citește rezultatul:** Conform = nicio schimbare. De verificat = schimbări; fiecare apare în probe cu data, amprenta și cine a făcut-o. Nedeterminat = jurnalul aplicației nu a fost disponibil.

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

### D02

- **Ce verificăm:** Dacă fiecare suport amovibil conectat acum (stick, disc extern) este criptat cu BitLocker To Go și dacă scrierea pe suporturi necriptate este blocată.
- **De ce contează:** Un suport necriptat pierdut poate fi citit de oricine; blocarea scrierii împiedică ieșirea datelor pe suporturi necriptate.
- **Cum verifică aplicația:** A listat suporturile amovibile conectate, starea BitLocker a fiecăruia și politica de interzicere a scrierii pe suporturi necriptate.
- **Sursa tehnică:** `WMI Win32_EncryptableVolume (suporturi amovibile); HKLM\SYSTEM\CurrentControlSet\Policies\Microsoft\FVE → RDVDenyWriteAccess`
- **Cum se citește rezultatul:** Conform = toate suporturile conectate sunt criptate, sau politica blochează scrierea pe cele necriptate. Neconform = un suport necriptat conectat și scrierea permisă. De verificat = niciun suport conectat și politica nu blochează scrierea. Nedeterminat = necesită administrator.

### D03

- **Ce verificăm:** Dacă fiecare suport conectat în perioada controlată a fost scanat cu antivirusul imediat după conectare și dacă antivirusul a găsit ceva.
- **De ce contează:** Un suport care intră pe stația izolată fără scanare poate aduce un program rău-intenționat.
- **Cum verifică aplicația:** A comparat fiecare conectare de disc din jurnalul Partition/Diagnostic cu scanările Defender începute în următoarele 10 minute și cu detecțiile din următoarea oră.
- **Sursa tehnică:** `Partition 1006; Defender 1000, 1001, 1116, 1117`
- **Cum se citește rezultatul:** Conform = fiecare conectare este urmată de o scanare. Neconform = antivirusul a găsit un program rău-intenționat după conectare. De verificat = conectări fără scanare pe această stație (scanarea se poate face înainte, pe stația de decontaminare: comparați cu registrul ei, verificarea M04). Aplicația nu vede stația de decontaminare.

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

### N05

- **Ce verificăm:** Dacă stația are un adaptor care este de fapt un telefon folosit ca modem, un modem celular sau o placă Ethernet pe USB.
- **De ce contează:** Aceste adaptoare dau stației o cale către internet sau către altă rețea, în afara rețelei ei, și nu se văd ca o placă de rețea obișnuită.
- **Cum verifică aplicația:** A citit adaptoarele de rețea active și a stabilit tipul fiecăruia după clasa dispozitivului, identificatorii compatibili (de exemplu clasa RNDIS pentru tethering), serviciul și descrierea; adaptoarele virtuale nu se numără.
- **Sursa tehnică:** `WMI Win32_PnPEntity (clasa Net) → Service, CompatibleID, HardwareID`
- **Cum se citește rezultatul:** Conform = niciunul dintre aceste tipuri. Neconform = pe stația izolată există telefon-modem sau modem celular. De verificat = placă Ethernet pe USB, sau stație neizolată; cereți aprobarea scrisă sau scoaterea dispozitivului.

### N06

- **Ce verificăm:** Dacă stația partajează foldere sau imprimante în rețea, în afara partajărilor administrative implicite ale Windows.
- **De ce contează:** O partajare permite altui calculator să citească sau să scrie fișiere pe stație.
- **Cum verifică aplicația:** A citit lista partajărilor stației și le-a separat pe cele create de cineva de cele implicite (C$, ADMIN$, IPC$).
- **Sursa tehnică:** `WMI Win32_Share (Name, Path, Type)`
- **Cum se citește rezultatul:** Conform = nicio partajare creată. Neconform = partajări pe o stație izolată. De verificat = partajări pe o stație neizolată; fiecare trebuie aprobată.

### N07

- **Ce verificăm:** Dacă este permisă Asistența la distanță (Remote Assistance).
- **De ce contează:** Prin ea altă persoană poate vedea și controla ecranul stației din rețea.
- **Cum verifică aplicația:** A citit politica care interzice Asistența la distanță și setarea stației.
- **Sursa tehnică:** `HKLM\SOFTWARE\Policies\Microsoft\Windows NT\Terminal Services → fAllowToGetHelp; HKLM\SYSTEM\CurrentControlSet\Control\Remote Assistance → fAllowToGetHelp`
- **Cum se citește rezultatul:** Conform = interzisă prin politică sau oprită. Neconform = permisă pe o stație izolată. De verificat = permisă pe o stație neizolată sau setarea lipsește.

### N08

- **Ce verificăm:** Dacă administrarea la distanță Windows (WinRM) rulează sau ascultă conexiuni.
- **De ce contează:** Prin WinRM se pot rula comenzi pe stație din rețea.
- **Cum verifică aplicația:** A citit modul de pornire al serviciului WinRM, dacă rulează acum și dacă are puncte de ascultare configurate.
- **Sursa tehnică:** `HKLM\SYSTEM\CurrentControlSet\Services\WinRM → Start; HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\WSMAN\Listener; WMI Win32_Service`
- **Cum se citește rezultatul:** Conform = serviciul este oprit și nu are puncte de ascultare. Neconform = rulează și are puncte de ascultare, pe o stație izolată. De verificat = este oprit acum, dar are puncte de ascultare (dacă pornește, stația acceptă administrare de la distanță), rulează fără puncte de ascultare, sau rulează pe o stație neizolată. Locul punctelor de ascultare trebuie confirmat în laborator.

### N09

- **Ce verificăm:** Dacă stația redirecționează conexiuni de rețea către alte adrese (comanda netsh interface portproxy).
- **De ce contează:** O redirecționare face din stație o punte între rețele și este folosită pentru a ocoli separarea lor.
- **Cum verifică aplicația:** A citit din registru lista redirecționărilor configurate (aceeași pe care o arată „netsh interface portproxy show all”).
- **Sursa tehnică:** `HKLM\SYSTEM\CurrentControlSet\Services\PortProxy`
- **Cum se citește rezultatul:** Conform = nicio redirecționare. Neconform = redirecționări pe o stație izolată. De verificat = redirecționări pe o stație neizolată.

### N10

- **Ce verificăm:** Dacă partajarea conexiunii la Internet (ICS) este pornită pe vreo conexiune.
- **De ce contează:** Cu ICS pornită, stația transmite traficul dintr-o rețea în alta.
- **Cum verifică aplicația:** A întrebat Windows care conexiuni au partajarea pornită și a citit politica care o interzice.
- **Sursa tehnică:** `Interfața Windows de partajare (HNetCfg.HNetShare); HKLM\SOFTWARE\Policies\Microsoft\Windows\Network Connections → NC_ShowSharedAccessUI`
- **Cum se citește rezultatul:** Conform = nepornită. Neconform = pornită pe o stație izolată. De verificat = pornită pe o stație neizolată, sau starea nu a putut fi citită dar politica o interzice.

### N11

- **Ce verificăm:** Dacă stația are configurată sau prezentă vreo cale spre internet: gateway, servere DNS, rute permanente, proxy, profil VPN, adaptor Wi-Fi sau modem celular, profiluri Wi-Fi salvate, partajarea conexiunii.
- **De ce contează:** O singură cale spre exterior anulează izolarea unei stații Standalone sau a unei rețele air-gap, chiar dacă acum nu este folosită.
- **Cum verifică aplicația:** A citit doar configurația locală: proprietățile adaptoarelor, registrul (gateway și DNS ale fiecărei interfețe, inclusiv ale adaptoarelor oprite acum, rute permanente, proxy WinINet și WinHTTP), fișierele rasphone.pbk, lista dispozitivelor Plug and Play (modemuri celulare, telefoane folosite ca modem), numărul profilurilor Wi-Fi salvate și starea partajării conexiunii (ICS); nu folosește WMI. Nu a deschis nicio conexiune, nu a trimis niciun pachet și nu a rezolvat niciun nume.
- **Sursa tehnică:** `Adaptoarele de rețea; HKLM\SYSTEM\CurrentControlSet\Services\Tcpip\Parameters (Interfaces, PersistentRoutes); HKLM/HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\Internet Settings; rasphone.pbk; lista dispozitivelor Plug and Play (SetupAPI); %ProgramData%\Microsoft\Wlansvc\Profiles (număr de fișiere); HNetCfg.HNetShare`
- **Cum se citește rezultatul:** Pe Standalone: Conform = nicio configurație găsită; Neconform = orice cale găsită, chiar un gateway intern sau un radio oprit. Pe Rețea air-gap: Conform = doar gateway și DNS interne (10/8, 172.16/12, 192.168/16, link-local, unique-local); Neconform = gateway sau DNS public, proxy spre exterior, VPN, radio pornit, modem celular, partajare a conexiunii; De verificat = proxy dat prin nume (nu se rezolvă fără trafic) sau radio prezent dar oprit. Nu se aplică pe Internet. Absența unei căi găsite nu dovedește că nu există una.

### N12

- **Ce verificăm:** Dacă stația este membră a domeniului rețelei izolate.
- **De ce contează:** Într-o rețea air-gap cu domeniu, stațiile se administrează și se autentifică prin controlerele de domeniu; o stație în grup de lucru iese din politicile domeniului.
- **Cum verifică aplicația:** A citit din WMI dacă calculatorul face parte dintr-un domeniu și numele domeniului. Nu a contactat controlerul de domeniu.
- **Sursa tehnică:** `WMI Win32_ComputerSystem → PartOfDomain, Domain`
- **Cum se citește rezultatul:** Conform = membră a unui domeniu. Neconform = grup de lucru. Nedeterminat = nu a putut fi citit. Nu se aplică pe Standalone și Internet. Accesibilitatea controlerului de domeniu se verifică separat, activ, în N13 (doar ediția Rețea).

### N13

- **Ce verificăm:** Dacă controlerul domeniului la care este alăturată stația poate fi găsit la o adresă internă și răspunde (verificare activă, doar citire).
- **De ce contează:** Fără controler de domeniu accesibil, stația nu primește politicile domeniului și autentificarea se face din memorie; un controler care se rezolvă la o adresă publică indică o configurare greșită sau o deturnare a rezolvării de nume.
- **Cum verifică aplicația:** Doar în ediția Rețea, doar pe o stație membră a domeniului și doar dacă modul de lucru permite traficul. Localizarea controlerului folosește traficul obișnuit al Windows, cel de la fiecare autentificare în domeniu: localizatorul (DsGetDcName, Netlogon) întreabă serverul DNS configurat de înregistrarea _ldap._tcp.dc._msdcs a domeniului, trimite controlerelor găsite pingul UDP Netlogon (389), iar numele controlerului se rezolvă prin același server DNS; acest trafic pleacă înainte să se cunoască vreo adresă. Conexiunea proprie a aplicației este una singură, TCP la LDAP (389), numai spre o adresă privată, fără autentificare și fără date trimise. Dacă vreo adresă rezolvată este publică, aplicația nu deschide nicio conexiune TCP. Totul are o limită de timp de 10 secunde.
- **Sursa tehnică:** `DsGetDcName (netapi32) → numele și adresa controlerului; rezolvarea numelui; conexiune TCP la portul 389`
- **Cum se citește rezultatul:** Conform = controler găsit la o adresă privată, LDAP răspunde. Neconform = controlerul se rezolvă la o adresă publică (aplicația nu deschide conexiunea TCP). De verificat = controlerul nu a răspuns, nu a fost găsit sau limita de timp a trecut. Nedeterminat = stația nu este membră a unui domeniu (N12), verificarea nu a rulat sau a fost anulată. Nu se aplică pe Standalone și Internet.

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

- **Ce verificăm:** Ce servicii noi (programe permanente în fundal) și activități planificate au fost create.
- **De ce contează:** Sunt metodele obișnuite prin care un program rău-intenționat rămâne activ după repornire.
- **Cum verifică aplicația:** A căutat instalările de servicii (fără drivere, listate separat în cronologie) și crearea de activități planificate.
- **Sursa tehnică:** `System 7045; Security 4698`
- **Cum se citește rezultatul:** Conform = niciunul. De verificat = fiecare nume necunoscut trebuie explicat.

### S03

- **Ce verificăm:** Ce versiune de Windows rulează stația și când s-a instalat ultima actualizare.
- **De ce contează:** O versiune care nu mai primește actualizări de securitate rămâne cu defecte cunoscute; pe o stație izolată actualizările se aduc manual, deci data lor trebuie să corespundă procedurii.
- **Cum verifică aplicația:** A citit numele, versiunea și numărul de build al Windows din registru și cea mai recentă dată de instalare a unei actualizări din lista Windows.
- **Sursa tehnică:** `HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion → DisplayVersion, CurrentBuild, UBR; WMI Win32_QuickFixEngineering → InstalledOn`
- **Cum se citește rezultatul:** Întotdeauna „de verificat”: aplicația arată faptele, dar nu are tabelul oficial al versiunilor încă suportate. Compară cu registrul actualizărilor offline al organizației. Numele din registru poate spune „Windows 10” și pe Windows 11; un build de la 22000 în sus este Windows 11.

### S04

- **Ce verificăm:** Ce actualizări Windows s-au instalat în perioada controlată și dacă fiecare apare în registrul actualizărilor offline.
- **De ce contează:** Pe o stație izolată fiecare actualizare vine pe un suport adus de cineva; o actualizare care nu apare în registru a intrat necontrolat.
- **Cum verifică aplicația:** A citit lista actualizărilor Windows instalate (cu data) și a căutat numărul KB al fiecăreia în registrul actualizărilor offline completat în aplicație.
- **Sursa tehnică:** `WMI Win32_QuickFixEngineering → HotFixID, InstalledOn; registrul actualizărilor offline (pagina Control stație)`
- **Cum se citește rezultatul:** Conform = toate actualizările din perioadă apar în registru. Neconform = actualizări instalate care nu apar în registru. De verificat = registrul nu este completat în aplicație (comparați manual, verificarea M05) sau nu s-a instalat nimic în perioadă. Lista Windows nu cuprinde actualizările de antivirus; data semnăturilor se compară separat.

### S05

- **Ce verificăm:** Ce verificăm: dacă ceasul stației este în ordine față de ultima oră pe care aplicația însăși a notat-o (la pornire și la fiecare raport), în jurnalul ei protejat împotriva modificărilor, și față de data instalării Windows.
- **De ce contează:** De ce contează: toate orele din jurnale și din constatări se bazează pe ceas; un ceas dat înapoi sau resetat (de exemplu baterie CMOS descărcată) face ca evenimentele să pară în altă ordine și poate ascunde ce s-a întâmplat.
- **Cum verifică aplicația:** Ce faceți: dacă ceasul este în urmă, corectați ora dintr-o sursă aprobată, verificați bateria CMOS și notați incidentul. Un salt mare înainte (peste 30 de zile) nu este o eroare: stația poate fi oprită mult timp; doar confirmați că data este cea reală.
- **Sursa tehnică:** `Jurnalul de audit al aplicației (intrările clock.seen, lanț de amprente); jurnalul de custodie al cazului; HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion → InstallDate`
- **Cum se citește rezultatul:** Conform = ora este în ordine. Neconform = ceasul este mai vechi cu peste 2 minute decât ultima activitate a aplicației sau dinaintea instalării Windows. De verificat = salt înainte de peste 30 de zile. Nedeterminat = aplicația nu are încă nicio înregistrare (prima rulare). Aplicația nu poate vedea o schimbare a ceasului făcută cât timp era oprită.

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
| AppLocker 8003 | eveniment Windows | AppLocker (lista albă) ar fi oprit pornirea unui program, dar este doar în mod audit, așa că programul a rulat. | Arată programe neaprobate care rulează pe stație; în mod de blocare ar fi fost oprite. | Jurnalul „AppLocker/EXE and DLL”; câmpul FilePath arată programul. |
| AppLocker 8004 | eveniment Windows | AppLocker (lista albă) a OPRIT pornirea unui program neaprobat. | Cineva a încercat să pornească un program care nu este aprobat pe stație. | Jurnalul „AppLocker/EXE and DLL”; câmpul FilePath arată programul. |
| AppLocker 8006 | eveniment Windows | AppLocker ar fi oprit un script sau un pachet de instalare (MSI), dar este doar în mod audit, așa că a rulat. | Arată scripturi sau instalări neaprobate care au rulat. | Jurnalul „AppLocker/MSI and Script”. |
| AppLocker 8007 | eveniment Windows | AppLocker a OPRIT un script sau un pachet de instalare (MSI) neaprobat. | Cineva a încercat să ruleze un script sau să instaleze un program neaprobat. | Jurnalul „AppLocker/MSI and Script”. |
| CodeIntegrity 3076 | eveniment Windows | App Control (WDAC) ar fi oprit un fișier neaprobat, dar politica este doar în mod audit, așa că fișierul a rulat. | Arată programe sau biblioteci neaprobate care au rulat pe stație. | Câmpul „File Name” arată fișierul. |
| CodeIntegrity 3077 | eveniment Windows | App Control (WDAC) a OPRIT încărcarea unui fișier neaprobat. | Cineva sau ceva a încercat să ruleze un fișier care nu este aprobat pe stație. | Câmpul „File Name” arată fișierul. |
| Reguli de audit (SACL) | setare Windows | Lista care spune ce acces la fișierele unui folder se înregistrează în jurnalul Security, și pentru cine. | Fără reguli, accesul la documente nu lasă nicio urmă. | Citirea ei cere drepturi de administrator; aplicația nu o schimbă. |
| Win32_UserAccount | interogare Windows (WMI) | Lista conturilor locale ale stației, cu starea lor (activ, dezactivat, parolă obligatorie, expirare). | Este lista reală a conturilor, chiar dacă unele sunt ascunse din ecranul de pornire. |  |
| S-1-5-32-544 | identificator de securitate (SID) | Administrators: grupul administratorilor locali: control total. | SID-ul identifică sigur contul sau grupul, chiar dacă a fost redenumit. |  |
| Win32_Group | interogare Windows (WMI) | Grupurile locale ale stației și membrii lor. | Arată cine are drepturi de administrator sau de acces la distanță. |  |
| S-1-5-32-555 | identificator de securitate (SID) | Remote Desktop Users: cine are voie să lucreze de la distanță. | SID-ul identifică sigur contul sau grupul, chiar dacă a fost redenumit. |  |
| Partition 1006 | eveniment Windows | Un disc a fost conectat la stație; jurnalul păstrează producătorul, modelul, seria și capacitatea. | Este cea mai bună urmă a unui stick USB sau disc extern conectat, cu numărul de serie de comparat cu registrul suporturilor. | BusType 7 = USB, 12 = card SD, 13 = card MMC. |
| Enum\\USBSTOR | cheie de registru | Lista tuturor dispozitivelor de stocare USB conectate vreodată la stație, cu seria lor. | Arată ce stick-uri au fost folosite, chiar dacă jurnalele s-au pierdut. | Ștergerea acestei liste este o urmă de ascundere. |
| FVE\\RDVDenyWriteAccess | valoare de registru | Politica BitLocker care interzice scrierea pe suporturi amovibile necriptate. | Datele pot ieși de pe stație doar pe suporturi criptate. | 1 = scrierea pe suporturi necriptate este blocată (de pe ele se poate doar citi). |
| Win32_EncryptableVolume | interogare BitLocker | Starea criptării BitLocker a discului. | Arată dacă datele sunt protejate dacă discul este scos din stație. | ProtectionStatus 1 = protejat, 0 = neprotejat. |
| Defender 1000 | eveniment Windows | Microsoft Defender a început o scanare (rapidă, completă sau a unui suport anume). | Arată dacă un suport a fost verificat cu antivirusul imediat după conectare. | Câmpul „Scan Resources” arată ce s-a scanat (de exemplu E:\\). |
| Defender 1001 | eveniment Windows | Microsoft Defender a terminat o scanare. | Arată că scanarea s-a încheiat. |  |
| Defender 1116 | eveniment Windows | Microsoft Defender a găsit un program rău-intenționat sau nedorit. | Un program rău-intenționat a ajuns pe stație; trebuie aflat de unde (de exemplu de pe un suport conectat). | Câmpurile „Threat Name” și „Path” arată ce și unde. |
| Defender 1117 | eveniment Windows | Microsoft Defender a luat o măsură împotriva unui program rău-intenționat (carantină, ștergere). | Arată că amenințarea a fost tratată; originea ei trebuie totuși investigată. |  |
| PASSWD_NOTREQD | indicator de cont | Contul are voie să aibă parolă goală. | Un cont fără parolă poate fi folosit de oricine. | Indicatorul nu dovedește că parola chiar este goală; se verifică. |
| NetworkProfile 10000 | eveniment Windows | Stația s-a conectat la o rețea (numele rețelei este în eveniment). | Pe o stație izolată, orice conectare la rețea este o abatere. |  |
| WLAN 8001 | eveniment Windows | Stația s-a conectat la o rețea fără fir (Wi-Fi); SSID = numele rețelei. | Pe o stație izolată, o conectare Wi-Fi este o abatere. |  |
| NetworkList\\Profiles | cheie de registru | Lista rețelelor la care stația s-a conectat vreodată, cu data primei și ultimei conectări. | Pe o stație izolată arată dacă a fost vreodată conectată la o rețea. | Profilurile pot fi șterse manual; lipsa lor nu dovedește nimic. |
| Services\\WlanSvc\\Start | valoare de registru | Modul de pornire al serviciului Wi-Fi (WLAN AutoConfig). | Fără serviciu pornit, stația nu se poate conecta la rețele fără fir. | 4 = dezactivat, 3 = la cerere, 2 = automat; lipsa înseamnă că serviciul nu există. |
| Services\\bthserv\\Start | valoare de registru | Modul de pornire al serviciului Bluetooth. | Bluetooth permite trimiterea de fișiere fără cablu. | 4 = dezactivat, 3 = la cerere, 2 = automat. |
| Win32_Share | interogare Windows (WMI) | Lista folderelor și imprimantelor partajate de stație în rețea. | O partajare permite altui calculator să citească sau să scrie pe stație. | C$, ADMIN$, IPC$ sunt partajările administrative implicite ale Windows. |
| Terminal Services\\fAllowToGetHelp | valoare de registru | Politica pentru Asistența la distanță (Remote Assistance). | Prin ea altă persoană poate prelua stația din rețea. | 0 = interzisă (corect pe stația izolată), 1 = permisă. |
| Remote Assistance\\fAllowToGetHelp | valoare de registru | Setarea stației pentru Asistența la distanță (Proprietăți sistem → Acces de la distanță). | Prin ea altă persoană poate prelua stația din rețea. | 0 = oprită, 1 = permisă; politica Terminal Services\\fAllowToGetHelp are întâietate. |
| Services\\WinRM\\Start | valoare de registru | Modul de pornire al serviciului de administrare la distanță (WinRM). | Cu serviciul pornit se pot rula comenzi pe stație din rețea. | 4 = dezactivat, 3 = la cerere, 2 = automat. |
| WSMAN\\Listener | cheie de registru | Punctele de ascultare ale administrării la distanță (WinRM), create de „winrm quickconfig”. | Un punct de ascultare înseamnă că stația acceptă comenzi din rețea. | Lipsa cheii sau a subcheilor = nu ascultă. |
| Services\\PortProxy | cheie de registru | Redirecționările de porturi create cu „netsh interface portproxy”. | O redirecționare face din stație o punte între rețele. | Fiecare valoare: adresa/portul ascultat → adresa/portul către care se trimite. |
| Network Connections\\NC_ShowSharedAccessUI | valoare de registru | Politica care interzice pornirea partajării conexiunii la Internet (ICS). | Partajarea face din stație o punte între rețele. | 0 = interzisă (corect pe stația izolată). |
| HNetCfg.HNetShare | interfață Windows | Interfața Windows care arată ce conexiuni au partajarea conexiunii la Internet pornită. | Arată dacă stația face legătura între două rețele. | Aplicația doar citește, nu schimbă nimic. |
| NetUserModalsGet | funcție Windows | Citește politica de parole a stației (aceeași pe care o arată comanda „net accounts”). | Arată regulile efective pentru parole și blocarea conturilor. |  |
| Policies\\System\\EnableLUA | valoare de registru | Pornește sau oprește UAC (fereastra care cere confirmare înainte ca un program să primească drepturi de administrator). | Cu UAC oprit, orice program pornit de un administrator are control total, fără întrebare. | 1 = activ (corect), 0 = oprit. |
| Policies\\System\\ConsentPromptBehaviorAdmin | valoare de registru | Cum întreabă UAC un administrator înainte de a da drepturi complete. | 0 înseamnă că nu se mai întreabă nimic: orice program pornit de un administrator capătă drepturi depline fără întrebare. | 0 = fără întrebare (neconform), 1/3 = cere parola, 2/4 = cere confirmare, 5 = cere confirmare doar pentru programe din afara Windows (implicit). |
| Policies\\System\\PromptOnSecureDesktop | valoare de registru | Dacă întrebarea UAC apare pe ecranul securizat (ecranul se întunecă și numai utilizatorul poate răspunde). | Pe ecranul obișnuit, un program rău-intenționat poate apăsa singur „Da” în locul utilizatorului. | 1 = pe ecranul securizat (corect), 0 = pe ecranul obișnuit. |
| Winlogon\\AutoAdminLogon | valoare de registru | Pornire automată în cont, fără să se tasteze parola. | Oricine pornește stația intră în cont. | 1 = activă (abatere), 0 sau lipsă = dezactivată. |
| Winlogon\\DefaultPassword | valoare de registru | Parolă salvată în registru pentru pornirea automată. | Parola stă în clar și poate fi citită de oricine are acces la registru. | Aplicația verifică doar dacă există, nu o citește. |
| Terminal Server\\fDenyTSConnections | valoare de registru | Permite sau interzice conectarea de la distanță (Remote Desktop). | Remote Desktop deschis permite lucrul pe stație din altă parte. | 1 = interzis (corect pe stația izolată), 0 = permis. |
| HNetCfg.FwPolicy2 | interfață Windows | Starea firewall-ului Windows pe fiecare profil de rețea. | Arată dacă firewall-ul este pornit. |  |
| MSFT_MpComputerStatus | interogare Microsoft Defender | Starea antivirusului Defender: pornit, protecție în timp real, data semnăturilor. | Arată dacă stația este protejată acum. | Dacă există alt antivirus, Defender poate apărea oprit; se verifică vizual. |
| ExclusionPath | setare Defender | Un folder pe care antivirusul NU îl verifică. | Atacatorii își pun programele în foldere excluse ca să nu fie detectate. |  |
| ExclusionProcess | setare Defender | Un program pe care antivirusul NU îl verifică. | Programul exclus poate face orice fără să fie oprit de antivirus. |  |
| ExclusionExtension | setare Defender | Un tip de fișier pe care antivirusul NU îl verifică. | Toate fișierele de acel tip scapă de verificare. |  |
| MSFT_MpPreference | interogare Microsoft Defender | Setările Defender, inclusiv excluderile. | Excluderile sunt locuri pe care antivirusul nu le verifică. |  |
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
| Tipuri de protecție BitLocker | interogare BitLocker | Cum se deblochează discul criptat la pornire. | Doar TPM (fără PIN) deblochează discul singur, chiar dacă stația este furată întreagă. | 1 = TPM, 2 = cheie pe stick, 3 = parolă de recuperare, 4 = TPM și PIN, 5 = TPM și cheie pe stick, 6 = TPM, PIN și cheie pe stick, 8 = parolă. |
| ConversionStatus | interogare BitLocker | Cât de criptat este volumul. | Un volum în curs de criptare nu este încă protejat complet. | 0 = necriptat, 1 = criptat complet, 2 = criptare în curs, 3 = decriptare în curs, 4/5 = oprită temporar. |
| Win32_Tpm | interogare Windows (WMI) | Starea cipului de securitate TPM: dacă există, dacă este pornit, pregătit și ce versiune are. | TPM păstrează cheile de criptare legate de placa de bază. | Necesită drepturi de administrator. |
| Kernel DMA Protection\\DeviceEnumerationPolicy | valoare de registru | Ce se întâmplă cu dispozitivele Thunderbolt/USB4 care nu suportă protecția memoriei. | Un astfel de dispozitiv poate citi direct memoria stației. | 0 = blocate, 1 = permise după autentificare (implicit), 2 = permise toate. |
| Kernel DMA Protection | informație Windows | Protecția memoriei la porturile Thunderbolt/USB4, așa cum o afișează „Informații sistem” (msinfo32). | Fără ea, un dispozitiv conectat la port poate citi memoria stației. | Pornită / oprită; depinde de placa de bază și de setările UEFI. |
| VirtualizationBasedSecurityStatus | interogare Windows (WMI) | Starea securității bazate pe virtualizare (VBS). | Fără VBS, Credential Guard și protecția integrității codului nu funcționează. | 0 = oprită, 1 = cerută dar nu rulează, 2 = rulează. |
| 0e796bdb-100d-47d6-a2d5-f7d2daa51f51\\ACSettingIndex | valoare de registru | Politica „parolă la trezire” pentru stația alimentată din priză. | Fără parolă, oricine trezește stația intră în sesiunea deschisă. | 1 = se cere parola, 0 = nu se cere. |
| 0e796bdb-100d-47d6-a2d5-f7d2daa51f51\\DCSettingIndex | valoare de registru | Politica „parolă la trezire” pentru stația alimentată din baterie. | Fără parolă, oricine trezește stația intră în sesiunea deschisă. | 1 = se cere parola, 0 = nu se cere. |
| Desktop\\DelayLockInterval | valoare de registru | Setarea „Solicitare autentificare” a utilizatorului (după cât timp de somn se cere parola). | Valoarea „niciodată” lasă sesiunea deschisă la trezire. | 0 = la fiecare trezire; 4294967295 (sau -1) = niciodată. |
| CONSOLELOCK | setare de alimentare | Setarea planului de alimentare care cere parola la revenirea din somn (powercfg). | Fără ea, oricine trezește stația intră în sesiunea deschisă. | 1 = se cere parola, 0 = nu se cere. |
| UsermodeCodeIntegrityPolicyEnforcementStatus | interogare Windows (WMI) | Cum aplică App Control (WDAC) lista albă pentru programe. | Doar modul de aplicare oprește programele neaprobate. | 0 = oprită, 1 = doar audit (notează), 2 = aplicată (blochează). |
| FullScanEndTime | interogare Microsoft Defender | Data la care s-a terminat ultima scanare completă a stației. | O scanare veche poate lăsa nedescoperit un program adus înainte de ultima actualizare. | Gol = nicio scanare completă, niciodată. |
| AntiVirusProduct | interogare Windows (WMI) | Antivirusurile înregistrate în Centrul de securitate Windows, inclusiv ale altor producători. | Arată ce antivirus protejează efectiv stația. | productState: un cod numeric; aplicația îl citește ca pornit/oprit și semnături la zi/vechi (decodare nedocumentată oficial). |
| Application 11707 | eveniment Windows | Windows Installer: un program a fost instalat cu succes. | Software-ul instalat trebuie să fie pe lista aprobată. |  |
| Application 11724 | eveniment Windows | Windows Installer: un program a fost dezinstalat cu succes. | Dezinstalarea poate ascunde un program folosit anterior. |  |
| Application 1033 | eveniment Windows | Windows Installer: instalare încheiată (cu codul de rezultat). | Software-ul instalat trebuie să fie pe lista aprobată. | Codul de stare 0 = reușit. |
| Application 1034 | eveniment Windows | Windows Installer: dezinstalare încheiată. | Dezinstalarea poate ascunde un program folosit anterior. |  |
| System 7045 | eveniment Windows | Un serviciu nou (program care rulează permanent în fundal) a fost instalat. | Serviciile noi sunt o metodă frecventă de a păstra un program rău-intenționat activ după repornire. | Calea executabilului (ImagePath) arată ce program rulează. |
| Security 4698 | eveniment Windows | A fost creată o activitate planificată (o comandă care pornește singură la o oră sau la pornire). | Activitățile planificate sunt o metodă frecventă prin care un program rău-intenționat repornește singur. | Programele legitime (actualizări) creează și ele activități planificate; comparați numele cu software-ul aprobat. |
| CurrentVersion\\DisplayVersion | valoare de registru | Versiunea Windows pe înțelesul utilizatorilor (de exemplu 24H2). | Arată cât de nouă este versiunea și dacă mai primește actualizări. | Se compară cu tabelul versiunilor suportate al organizației. |
| CurrentVersion\\CurrentBuild | valoare de registru | Numărul de build al Windows. | Identifică exact versiunea; 22000 sau mai mult înseamnă Windows 11. | Numele produsului din registru poate spune „Windows 10” și pe Windows 11. |
| CurrentVersion\\UBR | valoare de registru | Numărul de revizie al actualizărilor instalate peste build. | Crește cu fiecare actualizare lunară instalată. | Se citește împreună cu build-ul: 26100.1234 înseamnă revizia 1234. |
| Win32_QuickFixEngineering | interogare Windows (WMI) | Lista actualizărilor Windows instalate, cu numărul KB și data. | Se compară cu registrul actualizărilor offline. | Nu cuprinde actualizările antivirusului și nici toate componentele Windows. |
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
