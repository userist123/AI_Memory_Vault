namespace LogAnalyzer.Dfir.Flow;

/// <summary>The controller's answer to a check the computer cannot do. „Nu pot verifica” is an answer, and it is never counted as „în regulă”.</summary>
public enum ManualAnswer { NotAnswered, Yes, No, CannotCheck }

/// <summary>
/// A check the application can only support: physical seals, BIOS password, documents, registers kept on paper
/// (docs/dfir/RESEARCH_2026-10-11_CONTROL_STATII_CLASIFICATE.md §3; UX research R5/R12: „De verificat manual” with the steps, carried into the report).
/// <see cref="Question"/> is phrased so that „Da” is the expected (compliant) answer.
/// </summary>
public sealed record ManualCheck(string Id, string Question, string Why, string How, string ComparedWith);

public sealed record ManualCheckResult(string Id, ManualAnswer Answer, string Note);

public static class ManualChecks
{
    public static readonly IReadOnlyList<ManualCheck> All =
    [
        new("M01", "Stația figurează în documentele de acreditare ale sistemului, la nivelul de clasificare corect?",
            "O stație care nu este în perimetrul acreditat nu are voie să prelucreze informații clasificate.",
            "Comparați numele stației (afișat sus în aplicație) cu lista din documentele de acreditare și nivelul maxim aprobat.",
            "Numele stației și nivelul descifrat din nume; eticheta de pe ecranul de autentificare."),
        new("M02", "Procedurile de securitate în vigoare (cerințele de securitate și procedurile de operare) sunt cele aprobate, ultima versiune?",
            "Controlul se face față de procedurile aprobate; o versiune veche duce la concluzii greșite.",
            "Cereți documentele și verificați versiunea și data aprobării.",
            "Profilul de proceduri din aplicație (ore de lucru, software aprobat, zone)."),
        new("M03", "Fiecare suport din registrul de medii este prezent fizic, cu eticheta corectă și cu un responsabil?",
            "Un suport lipsă sau fără responsabil poate însemna date scoase din zonă.",
            "Luați fiecare suport din registru, comparați eticheta (xxxx-yyyyL) și seria; notați lipsurile.",
            "Pagina „Medii de stocare”: dispozitivele văzute pe stație față de registru."),
        new("M04", "Datele aduse pe stație trec printr-o stație de verificare (scanare antivirus) înainte de prima conectare?",
            "Majoritatea programelor rău-intenționate care ajung pe stații izolate vin pe stick-uri.",
            "Cereți registrul stației de verificare și căutați înregistrarea pentru suporturile văzute pe stație.",
            "Istoricul conectărilor fiecărui suport (pagina „Medii de stocare”)."),
        new("M05", "Actualizările și semnăturile antivirus aduse offline sunt înregistrate (data, sursa, amprenta pachetului, cine le-a adus)?",
            "Fără registru nu se poate dovedi că actualizările provin dintr-o sursă sigură.",
            "Comparați data ultimei actualizări din aplicație cu registrul de actualizări.",
            "Verificările P09 (semnături antivirus) și versiunea Windows."),
        new("M06", "Exporturile de jurnale sunt înregistrate (cine, când, pe ce suport, unde se păstrează)?",
            "Jurnalele sunt dovada a ce s-a întâmplat pe stație; trebuie păstrate și urmărite.",
            "Cereți registrul exporturilor; comparați cu golurile și ștergerile de jurnal arătate de aplicație.",
            "Verificările A03–A05 (acoperirea și ștergerea jurnalelor)."),
        new("M07", "Parola BIOS/UEFI este setată, păstrată sigilat, iar pornirea este permisă doar de pe discul intern?",
            "Fără parolă BIOS, oricine poate porni stația de pe un stick și poate citi sau copia discul.",
            "La pornire, încercați meniul BIOS (doar în prezența administratorului): trebuie să ceară parolă; verificați ordinea de pornire.",
            "Secure Boot și modul UEFI (dacă aplicația le-a putut citi)."),
        new("M08", "Sigiliile de pe carcasă și de pe porturi sunt intacte, iar porturile nefolosite sunt dezactivate fizic?",
            "Un sigiliu rupt poate însemna că s-a umblat în stație sau s-a conectat ceva.",
            "Verificați fiecare sigiliu și numărul lui față de procesul-verbal anterior.",
            "Dispozitivele noi față de controlul anterior („Compară cu controlul anterior”)."),
        new("M09", "Camera și microfonul sunt acoperite, deconectate sau dezactivate?",
            "Camera și microfonul pot înregistra informații fără ca utilizatorul să știe.",
            "Verificați fizic; la laptopuri căutați camera de deasupra ecranului.",
            "Setările de confidențialitate pentru cameră și microfon, dacă au fost citite."),
        new("M10", "Ora stației este corectă față de un ceas de referință (abatere de câteva minute cel mult)?",
            "O oră greșită face ca toate orele din jurnale să fie greșite.",
            "Comparați ora afișată de Windows cu ceasul de referință și notați abaterea.",
            "Verificarea A07 (ora schimbată manual)."),
        new("M11", "Toți utilizatorii care au lucrat pe stație au autorizație de acces valabilă la nivelul stației?",
            "Accesul la informații clasificate cere autorizație și nevoia de a cunoaște.",
            "Comparați lista „Cine a lucrat și când” cu registrul utilizatorilor și cu autorizațiile.",
            "Registrul utilizatorilor și fila „Utilizatori” a controlului."),
        new("M12", "Documentele tipărite sunt înregistrate și marcate cu nivelul de clasificare?",
            "Tipărirea este una dintre căile prin care informațiile ies din zonă.",
            "Comparați registrul de tipărire cu tipăririle înregistrate de stație (dacă jurnalul de tipărire este activ).",
            "Jurnalul de tipărire al stației, dacă este activ."),
        new("M13", "Incidentele de securitate de pe stație au fost înregistrate și raportate?",
            "Orice incident trebuie raportat; un incident neraportat este el însuși o abatere.",
            "Comparați constatările NECONFORME ale controlului cu registrul incidentelor.",
            "Verificările NECONFORME din acest control."),
        new("M14", "Suporturile scoase din uz au proces-verbal de distrugere sau de declasificare?",
            "Un suport aruncat fără distrugere poate fi citit de oricine.",
            "Pentru suporturile marcate „retras” sau „distrus” în registru, cereți procesul-verbal.",
            "Registrul de medii (stare retras / distrus)."),
        new("M15", "Excepțiile (de exemplu Bluetooth, inscriptor CD, program neaprobat) sunt aprobate în scris?",
            "O abatere aprobată documentat nu este neconformitate; una neaprobată este.",
            "Pentru fiecare constatare de tip excepție, cereți documentul de aprobare și notați-i numărul în observație.",
            "Verificările DE VERIFICAT ale acestui control."),
    ];

    public static string AnswerText(ManualAnswer a) => a switch
    {
        ManualAnswer.Yes => "Da",
        ManualAnswer.No => "NU",
        ManualAnswer.CannotCheck => "Nu am putut verifica",
        _ => "Neverificat",
    };

    /// <summary>One line per question for the report and the result screen; unanswered questions are listed as „Neverificat”, never dropped.</summary>
    public static string Summary(IReadOnlyCollection<ManualCheckResult> results)
    {
        int yes = results.Count(r => r.Answer == ManualAnswer.Yes), no = results.Count(r => r.Answer == ManualAnswer.No),
            cannot = results.Count(r => r.Answer == ManualAnswer.CannotCheck), open = All.Count - yes - no - cannot;
        return $"Verificări făcute de controlor: {no} cu răspuns NU · {cannot} nu au putut fi verificate · {open} neverificate · {yes} în regulă";
    }
}
