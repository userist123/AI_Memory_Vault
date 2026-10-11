namespace LogAnalyzer.Dfir.Windows.Audit;

/// <summary>
/// What a control check means, for a controller without IT training: ce verificăm, de ce contează, cum a verificat aplicația (sursa, în cuvinte
/// și tehnic), ce înseamnă fiecare rezultat. One entry per check id of <see cref="ControlEvaluator"/>; a test keeps the two in step.
/// </summary>
public sealed record CheckGuide(string Id, string What, string Why, string How, string Source, string Meaning);

/// <summary>The guide of one check, with the codes in its evidence decoded: what the „Detalii” panel of a control result shows.</summary>
public sealed record CheckExplanation(ControlCheck Check, CheckGuide Guide, IReadOnlyList<CodeExplanation> Codes)
{
    public string StatusMeaning => Check.Status switch
    {
        ControlStatus.Conform => "CONFORM: ce s-a verificat corespunde regulii.",
        ControlStatus.Neconform => "NECONFORM: ce s-a găsit încalcă regula. Se trece în procesul-verbal și se cere remedierea.",
        ControlStatus.DeVerificat => "DE VERIFICAT: aplicația arată faptele, dar doar organizația poate spune dacă erau aprobate. Comparați cu documentele (registre, aprobări).",
        _ => "NEDETERMINAT: sursa necesară nu a putut fi citită, deci verificarea nu s-a putut face. Nu înseamnă nici bine, nici rău.",
    };

    public string NextStep => Check.Status switch
    {
        ControlStatus.Conform => "Nu este nevoie de nicio acțiune.",
        ControlStatus.Nedeterminat => "Închideți aplicația, porniți-o cu clic dreapta → „Executare ca administrator” și rulați din nou controlul. Dacă rămâne nedeterminat, notați în procesul-verbal că sursa lipsește.",
        _ => Check.Recommendation is { Length: > 0 } r ? r : "Cereți explicații persoanei responsabile de stație și notați răspunsul în procesul-verbal.",
    };

    /// <summary>All of it as plain text (copy, report annex).</summary>
    public string ToPlainText()
    {
        var lines = new List<string>
        {
            $"{Check.Id} · {Check.Title}", "", "Ce verificăm: " + Guide.What, "De ce contează: " + Guide.Why, "Cum a verificat aplicația: " + Guide.How,
            "Sursa tehnică: " + Guide.Source, "", "Rezultat: " + StatusMeaning, "Ce s-a găsit: " + Check.Detail, "Cum se citește rezultatul: " + Guide.Meaning,
            "Ce faceți: " + NextStep,
        };
        if (Codes.Count > 0) { lines.Add(""); lines.Add("Codurile care apar, pe înțeles:"); lines.AddRange(Codes.Select(c => "• " + c.Text)); }
        return string.Join(Environment.NewLine, lines);
    }
}

public static class CheckGuides
{
    private static CheckGuide G(string id, string what, string why, string how, string source, string meaning) => new(id, what, why, how, source, meaning);

    private static readonly Dictionary<string, CheckGuide> All = new[]
    {
        G("C01", "Dacă contul „Guest” (vizitator, fără parolă personală) este dezactivat.", "Contul Guest permite intrarea pe stație fără un cont nominal; nu se poate ști cine a lucrat.",
          "A citit lista conturilor locale din Windows și a căutat contul care se termină în -501 (Guest are mereu acest număr, chiar dacă e redenumit).",
          "WMI Win32_UserAccount (LocalAccount = TRUE), SID …-501", "Conform = dezactivat sau inexistent. Neconform = contul Guest este activ."),
        G("C02", "Ce conturi active au drepturi de administrator pe stație.", "Un administrator poate schimba orice: poate șterge jurnale, instala programe, crea conturi. Lista trebuie să fie scurtă și aprobată.",
          "A citit membrii grupului local Administrators și a păstrat doar conturile active.", "WMI Win32_Group (SID S-1-5-32-544) și Win32_GroupUser",
          "Întotdeauna „de verificat”: comparați numele cu lista de administratori aprobați în scris."),
        G("C03", "Dacă contul Administrator încorporat al Windows (cel creat la instalare) este dezactivat.", "Este un cont cunoscut de toată lumea, ținta preferată a celor care ghicesc parole.",
          "A căutat contul care se termină în -500 (Administratorul încorporat are mereu acest număr).", "WMI Win32_UserAccount, SID …-500",
          "Conform = dezactivat. De verificat = activ; poate fi justificat dacă este redenumit și are parolă puternică."),
        G("C04", "Dacă există conturi active care au voie să aibă parolă goală.", "Un cont fără parolă poate fi folosit de oricine se așază la stație.",
          "A citit indicatorul „parola nu este obligatorie” (PASSWD_NOTREQD) al fiecărui cont activ.", "WMI Win32_UserAccount.PasswordRequired",
          "Indicatorul arată doar că parola goală este permisă, nu că parola chiar este goală. Verificați cu titularul contului."),
        G("C05", "Ce conturi active au parola setată să nu expire niciodată.", "O parolă care nu se schimbă niciodată poate fi aflată în timp și folosită ani de zile.",
          "A citit indicatorul de expirare a parolei pentru fiecare cont activ.", "WMI Win32_UserAccount.PasswordExpires",
          "De verificat: fiecare excepție trebuie justificată (de exemplu conturi de serviciu)."),
        G("C06", "Cine are voie să lucreze pe stație de la distanță (Remote Desktop).", "Lucrul de la distanță înseamnă acces fără a fi fizic la stație.",
          "A citit membrii grupului local Remote Desktop Users.", "WMI Win32_Group (SID S-1-5-32-555)", "De verificat: fiecare nume trebuie să aibă aprobare. Pe o stație izolată grupul ar trebui să fie gol."),
        G("P01", "Câte caractere trebuie să aibă cel puțin o parolă.", "Parolele scurte se ghicesc ușor.", "A citit politica de parole a stației (aceeași pe care o arată „net accounts”).",
          "NetUserModalsGet (politica locală de parole)", "Conform = cel puțin 12. De verificat = 8–11. Neconform = sub 8."),
        G("P02", "După câte parole greșite se blochează contul.", "Fără blocare, cineva poate încerca la nesfârșit parole până o nimerește.", "A citit pragul de blocare din politica de parole.",
          "NetUserModalsGet (prag și durată de blocare)", "Conform = blocare după cel mult 10 încercări. Neconform = contul nu se blochează niciodată (0) sau pragul e prea mare."),
        G("P03", "Dacă parolele expiră și trebuie schimbate periodic.", "O parolă schimbată periodic limitează timpul în care o parolă aflată poate fi folosită.",
          "A citit durata maximă a parolei și istoricul (câte parole vechi nu pot fi refolosite).", "NetUserModalsGet (vârsta maximă, istoric)", "De verificat = parolele nu expiră niciodată."),
        G("P04", "Dacă UAC este pornit (fereastra care cere confirmare înainte ca un program să primească drepturi de administrator).",
          "Fără UAC, orice program pornit de un administrator — inclusiv unul rău-intenționat — primește control total fără întrebare.",
          "A citit valoarea EnableLUA din registru.", @"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System → EnableLUA, ConsentPromptBehaviorAdmin",
          "Conform = 1 (pornit). Neconform = 0 (oprit). Nedeterminat = valoarea nu a putut fi citită."),
        G("P05", "Dacă stația intră automat într-un cont la pornire, fără parolă.", "Oricine pornește stația ajunge în cont; parola salvată poate fi citită din registru.",
          "A citit valorile de pornire automată din registru (fără să citească parola în sine).", @"HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon → AutoAdminLogon, DefaultPassword",
          "Conform = dezactivată. Neconform = pornire automată activă sau parolă salvată."),
        G("P06", "Dacă stația acceptă conexiuni de la distanță (Remote Desktop).", "Remote Desktop permite lucrul pe stație din alt loc.", "A citit valoarea fDenyTSConnections din registru.",
          @"HKLM\SYSTEM\CurrentControlSet\Control\Terminal Server → fDenyTSConnections", "Conform = 1 (interzis). Pe o stație izolată, permis = neconform; altfel de verificat."),
        G("P07", "Dacă firewall-ul Windows (bariera care oprește conexiunile nedorite) este pornit.", "Fără firewall, programele și serviciile stației sunt expuse în rețea.",
          "A întrebat Windows starea firewall-ului pentru fiecare din cele trei profiluri: domeniu, privat, public.", "COM HNetCfg.FwPolicy2.FirewallEnabled",
          "Conform = activ pe toate trei. Neconform = oprit pe cel puțin unul."),
        G("P08", "Dacă antivirusul Microsoft Defender este pornit, inclusiv protecția în timp real.", "Protecția în timp real oprește programele rău-intenționate în momentul în care apar.",
          "A întrebat Defender starea sa.", @"WMI root\Microsoft\Windows\Defender MSFT_MpComputerStatus",
          "Conform = activ și în timp real. Nedeterminat = starea nu a putut fi citită (poate exista alt antivirus: verificați vizual)."),
        G("P09", "Cât de vechi sunt semnăturile antivirusului (lista virușilor cunoscuți).", "Un antivirus cu semnături vechi nu recunoaște virușii noi.",
          "A citit data ultimei actualizări a semnăturilor.", "MSFT_MpComputerStatus.AntivirusSignatureLastUpdated",
          "Conform = cel mult 7 zile. Pe stația izolată se actualizează offline, după procedură: comparați data cu registrul actualizărilor."),
        G("P10", "Ce foldere, programe sau tipuri de fișiere sunt excluse de la verificarea antivirusului.", "Atacatorii își ascund des programele în locuri excluse.",
          "A citit listele de excluderi din setările Defender.", "MSFT_MpPreference ExclusionPath / ExclusionProcess / ExclusionExtension",
          "De verificat: fiecare excludere trebuie justificată (de exemplu de furnizorul unei aplicații)."),
        G("P11", "Dacă discul de sistem este criptat cu BitLocker.", "Fără criptare, cine scoate discul din stație poate citi toate datele.", "A întrebat Windows starea de protecție a discului C:.",
          @"WMI root\cimv2\Security\MicrosoftVolumeEncryption Win32_EncryptableVolume.ProtectionStatus", "Conform = protecție activă (1). Neconform = discul nu este protejat."),
        G("P12", "Dacă versiunea veche (1) a protocolului de partajare a fișierelor, SMBv1, este oprită.", "SMBv1 are vulnerabilități cunoscute folosite de viruși care se răspândesc singuri (WannaCry).",
          "A citit setările SMBv1 ale serverului și ale clientului din registru.", @"HKLM\SYSTEM\CurrentControlSet\Services\LanmanServer\Parameters → SMB1; Services\mrxsmb10 → Start",
          "Conform = dezactivat. Neconform = activ."),
        G("P13", "Dacă Windows păstrează parolele în memorie într-o formă care poate fi citită (WDigest).", "Un program rău-intenționat ar putea extrage parolele în clar.",
          "A citit valoarea UseLogonCredential din registru.", @"HKLM\SYSTEM\CurrentControlSet\Control\SecurityProviders\WDigest → UseLogonCredential",
          "Conform = 0 sau lipsă (implicit sigur). Neconform = 1."),
        G("P14", "Dacă folosirea stick-urilor și discurilor USB este blocată prin politică.", "Stick-urile sunt principala cale prin care datele ies de pe o stație izolată sau prin care intră viruși.",
          "A citit pornirea componentei USBSTOR și politica „interzice orice acces la stocare amovibilă”.", @"HKLM\SYSTEM\CurrentControlSet\Services\USBSTOR → Start; HKLM\SOFTWARE\Policies\Microsoft\Windows\RemovableStorageDevices → Deny_All",
          "Conform = blocat. De verificat = permis: atunci trebuie folosite doar suporturi aprobate și înregistrate."),
        G("A01", "Dacă stația înregistrează în jurnal tot ce are nevoie un control (autentificări, conturi, programe, USB etc.).", "Ce nu se înregistrează nu lasă urme, deci nu poate fi verificat.",
          "A citit politica de audit avansat a stației și a comparat-o cu cele 10 categorii necesare controlului.", "auditpol /get /category:* (AuditQuerySystemPolicy)",
          "Conform = toate categoriile necesare sunt auditate. Neconform = lipsesc categorii; sunt numite în constatare."),
        G("A02", "Cât de mare poate fi jurnalul Security înainte să se suprascrie.", "Un jurnal mic se umple în câteva zile și evenimentele vechi dispar.", "A citit dimensiunea maximă și modul jurnalului.",
          "Configurația jurnalului Security (EventLogConfiguration)", "Conform = cel puțin 128 MB. Mod Circular = suprascrie cele mai vechi evenimente."),
        G("A03", "Dacă jurnalul Security mai conține evenimente din toată perioada controlată.", "Dacă cel mai vechi eveniment e mai nou decât începutul perioadei, activitatea de dinainte nu mai poate fi verificată.",
          "A citit data celui mai vechi eveniment din jurnal și a comparat-o cu începutul perioadei.", "Jurnalul Security, primul eveniment", "Conform = acoperă perioada. Neconform = jurnalul începe după începutul perioadei."),
        G("A04", "Dacă jurnalul System mai conține evenimente din toată perioada controlată.", "La fel ca pentru Security.", "La fel ca pentru Security, pe jurnalul System.", "Jurnalul System, primul eveniment",
          "Conform = acoperă perioada. Neconform = jurnalul începe după începutul perioadei."),
        G("A05", "Dacă cineva a golit jurnalele în perioada controlată.", "Golirea jurnalelor șterge urmele; aproape întotdeauna trebuie explicată.",
          "A căutat evenimentele de golire a jurnalelor.", "Security 1102, System 104", "Conform = nicio golire. Neconform = cel puțin o golire; contul care a făcut-o apare în probe."),
        G("A06", "Dacă politica de audit (ce se înregistrează) a fost schimbată în perioada controlată.", "Cine reduce auditul poate acționa fără urme.",
          "A căutat evenimentele de modificare a politicii de audit.", "Security 4719", "De verificat: fiecare schimbare trebuie să fie aprobată; în probe codul GUID arată categoria schimbată."),
        G("A07", "Dacă ora stației a fost schimbată manual de o persoană.", "Cu ora schimbată, toate orele din jurnale sunt greșite.", "A căutat schimbările de oră, fără corecțiile automate ale Windows.",
          "Security 4616 (fără svchost.exe și conturile sistemului)", "Conform = nicio schimbare manuală. De verificat = schimbări făcute de utilizatori."),
        G("U01", "Cine a fost adăugat sau scos din grupul administratorilor în perioada controlată.", "Primirea drepturilor de administrator trebuie aprobată.",
          "A căutat adăugările și eliminările din grupul local Administrators.", "Security 4732, 4733 (grup S-1-5-32-544)", "Conform = nicio modificare. De verificat = modificări; comparați cu aprobările."),
        G("U02", "Ce conturi au fost create, șterse, activate, dezactivate sau au avut parola resetată.", "Fiecare operație pe conturi trebuie să aibă o cerere aprobată.",
          "A căutat evenimentele de gestionare a conturilor.", "Security 4720, 4722, 4724, 4725, 4726", "Conform = nicio operație. De verificat = operații; comparați cu cererile aprobate."),
        G("U03", "Cine s-a conectat la stație de la distanță (Remote Desktop) și de unde.", "Pe o stație izolată nu ar trebui să existe acces de la distanță.",
          "A numărat autentificările de tip 10 (la distanță) și sesiunile cu adresă de rețea.", "Security 4624 (tip de logon 10); TerminalServices-LocalSessionManager 21, 25",
          "Conform = niciuna. Pe stația izolată = neconform; altfel de verificat."),
        G("U04", "Dacă au existat serii de parole greșite (5 sau mai multe într-o oră pentru același cont).", "Poate însemna că cineva încearcă să ghicească parola.",
          "A grupat autentificările eșuate pe cont și pe oră.", "Security 4625", "Conform = nicio serie. De verificat = serii; întrebați titularul contului dacă și-a uitat parola."),
        G("U05", "Cine a lucrat pe stație cu drepturi de administrator.", "Lucrul zilnic nu ar trebui făcut cu drepturi de administrator.",
          "A numărat sesiunile care au primit drepturi speciale, fără conturile sistemului.", "Security 4672", "De verificat: comparați cu lista administratorilor aprobați."),
        G("U06", "Dacă au rulat scripturi PowerShell care folosesc comenzi des abuzate (descărcare, cod ascuns, oprirea antivirusului).", "Aceste comenzi sunt folosite frecvent de atacatori.",
          "A căutat în textul scripturilor înregistrate cuvinte-cheie: DownloadString, FromBase64String, Invoke-Expression/IEX, Set-MpPreference, -EncodedCommand.",
          "Microsoft-Windows-PowerShell/Operational 4104", "De verificat: un specialist IT trebuie să se uite la textul scriptului din probe."),
        G("D01", "Ce dispozitive de stocare USB au fost conectate la stație.", "Pe o stație izolată, doar suporturile aprobate și înregistrate au voie să fie folosite.",
          "A citit lista dispozitivelor USB din registru și conectările de discuri din jurnal, cu producător, model și număr de serie.",
          @"HKLM\SYSTEM\CurrentControlSet\Enum\USBSTOR; Microsoft-Windows-Partition/Diagnostic 1006",
          "Întotdeauna de verificat: comparați seriile cu registrul suporturilor. Lipsa urmelor nu dovedește că nu s-au folosit suporturi."),
        G("N01", "Dacă stația izolată s-a conectat la vreo rețea în perioada controlată.", "O stație izolată conectată la rețea poate transmite sau primi date fără control.",
          "A căutat profiluri de rețea folosite în perioadă, evenimente de conectare și interfețe de rețea active acum.",
          @"HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\NetworkList\Profiles; NetworkProfile 10000; WLAN-AutoConfig 8001",
          "Neconform = s-a conectat (cine, când, ce s-a transferat trebuie investigat). Lipsa urmelor = de verificat, nu dovadă."),
        G("N02", "La ce rețele s-a conectat stația vreodată (istoric).", "Arată dacă stația a fost vreodată în altă rețea decât cea permisă.", "A citit lista profilurilor de rețea salvate.",
          @"HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\NetworkList\Profiles", "De verificat: fiecare rețea din listă trebuie să fie una permisă."),
        G("S01", "Ce programe au fost instalate sau dezinstalate în perioada controlată.", "Pe stație trebuie să existe doar software aprobat.", "A căutat evenimentele Windows Installer.",
          "Application 11707, 11724, 1033, 1034", "Conform = nicio operație. De verificat = operații; comparați cu lista software-ului aprobat."),
        G("S02", "Ce servicii noi (programe permanente în fundal) și task-uri programate au fost create.", "Sunt metodele obișnuite prin care un program rău-intenționat rămâne activ după repornire.",
          "A căutat instalările de servicii (fără drivere, listate separat în cronologie) și crearea de task-uri programate.", "System 7045; Security 4698",
          "Conform = niciunul. De verificat = fiecare nume necunoscut trebuie explicat."),
    }.ToDictionary(g => g.Id);

    public static IReadOnlyCollection<string> Ids => All.Keys;

    public static CheckGuide For(string id) => All.TryGetValue(id, out var g) ? g
        : new(id, "Această verificare nu are încă o descriere în aplicație.", "—", "—", "—", "Citiți constatarea și recomandarea.");

    /// <summary>The guide plus every recognised code in the check's detail and evidence, decoded.</summary>
    public static CheckExplanation Explain(ControlCheck c)
    {
        var codes = new List<CodeExplanation>();
        void AddAll(string? text) { foreach (var e in CodeExplainer.FindIn(text)) if (!codes.Any(x => x.Code == e.Code)) codes.Add(e); }
        AddAll(For(c.Id).Source);
        AddAll(c.Detail);
        foreach (var line in c.Evidence.Take(200)) AddAll(line);
        return new CheckExplanation(c, For(c.Id), codes);
    }
}
