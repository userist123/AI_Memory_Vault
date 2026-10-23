namespace LogAnalyzer.UI.ViewModels
{
    /// <summary>
    /// The texts of the "empty page" panels (rows 16-17 of the 2026-10-22 beginner walkthrough): what the page is, why it matters for the control (and which checks stay
    /// NEDETERMINAT while it is empty) and the first step, whose button sits under the text. Plain Romanian, no English field names.
    /// </summary>
    public static class EmptyStates
    {
        public const string UsersWhat = "Registrul de utilizatori este lista persoanelor care lucrează pe stație: conturile lor, nivelul de autorizare și zonele în care au voie.";
        public const string UsersWhy = "Controlul compară cine a folosit un mediu de stocare cu autorizarea persoanei. Cât timp registrul este gol, verificările de autorizare a utilizatorilor rămân NEDETERMINAT; un registru gol nu înseamnă „conform”.";
        public const string UsersStep = "Primul pas: apăsați „Preia de pe stație”. Aplicația citește conturile existente (doar citire, nu modifică nimic), iar voi completați apoi autorizarea fiecărei persoane.";

        public const string ProfileWhat = "Profilul de proceduri descrie cum se lucrează în organizație: programul de lucru, ferestrele de mentenanță, software-ul aprobat, politicile așteptate, zonele și transferurile.";
        public const string ProfileWhy = "Controlul compară ce s-a observat pe stație cu ce este așteptat. Cât timp secțiunile sunt goale, verificările care depind de ele (activitate în afara programului, golirea jurnalelor, software neaprobat, politici așteptate, zone și transferuri) rămân NEDETERMINAT.";
        public const string ProfileStep = "Primul pas: apăsați „Importă JSON…” dacă aveți deja un profil; altfel completați tabelele de mai jos, începând cu programul de lucru.";

        public const string PolicyWhat = "O politică este un set de setări de securitate Windows (de exemplu dintr-un export de politică de grup) pe care le importați, le aprobați și apoi le aplicați, cu verificare după aplicare.";
        public const string PolicyWhy = "Controlul stației compară setările reale cu politica așteptată. Cât timp nu este deschisă nicio politică, verificările de conformitate cu politica rămân NEDETERMINAT.";
        public const string PolicyStep = "Primul pas: apăsați „Deschide fișier…” și alegeți politica (un fișier de politică, Registry.pol, .inf sau o copie de rezervă a politicii de grup).";
    }
}
