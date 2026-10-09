using System;
using System.Collections.Generic;
using System.Linq;
using LogAnalyzer.Core.Services.Connectivity;

namespace LogAnalyzer.Core.Services.Edition
{
    /// <summary>What a Home intent does when chosen, besides navigating to its page.</summary>
    public enum IntentSource
    {
        /// <summary>Only navigates.</summary>
        None,
        /// <summary>Opens the investigation page set to collect from this PC.</summary>
        CollectFromThisStation,
        /// <summary>Opens the investigation page set to import evidence brought from another PC.</summary>
        ImportEvidence,
        /// <summary>Asks for an existing case folder and opens it.</summary>
        OpenExistingCase,
    }

    /// <summary>
    /// One button of the Home page, written with the user's words (title = what you get, description = one sentence under it).
    /// The technical page it leads to is <see cref="TargetTab"/>; what it needs from the edition and the mode is declared, never assumed.
    /// </summary>
    public sealed record HomeIntent(
        string Key,
        string Title,
        string Description,
        int TargetTab,
        IntentSource Source = IntentSource.None,
        EditionFeature? RequiresFeature = null,
        bool RequiresNetwork = false);

    /// <summary>An intent with the verdict of the edition and the mode: enabled, or disabled with the reason the user sees.</summary>
    public sealed record IntentAvailability(HomeIntent Intent, bool Enabled, string Reason)
    {
        public string Key => Intent.Key;
        public string Title => Intent.Title;
        public string Description => Intent.Description;
        public int TargetTab => Intent.TargetTab;
    }

    /// <summary>One entry of the role's sidebar section.</summary>
    public sealed record NavigationEntry(string Title, int Tab, bool Enabled, string Reason)
    {
        public string TabText => Tab.ToString(System.Globalization.CultureInfo.InvariantCulture);
        public string Tooltip => Enabled ? Title : $"{Title}: {Reason}";
    }

    /// <summary>
    /// The role profile is data (prompt §7): at most five primary intents, the rest under "Mai multe", and the sidebar entries of the role.
    /// The UI renders it; nothing here decides anything about evidence.
    /// </summary>
    public sealed record RoleProfile(
        StationRole Role,
        string Title,
        string Intro,
        IReadOnlyList<IntentAvailability> PrimaryIntents,
        IReadOnlyList<IntentAvailability> MoreIntents,
        IReadOnlyList<NavigationEntry> Navigation);

    public static class RoleProfiles
    {
        public const int MaxPrimary = 5;

        // Tab indexes of the main window (MainWindow.xaml TabControl order; Home was appended last, WP5).
        public const int TabTimeline = 4, TabChainOfCustody = 9, TabContainment = 13, TabStationControl = 14, TabDomainMail = 15,
            TabInvestigation = 16, TabPolicies = 17, TabProcedureProfile = 18, TabMediaRegister = 19, TabUsersRegister = 20, TabAccounts = 21, TabHome = 22;

        public const string NeedsNetworkReason = "necesită modul conectat; această stație este izolată (air-gap)";

        public static RoleProfile For(StationRole role, IEditionProfile edition, AppMode mode)
        {
            IntentAvailability A(HomeIntent i)
            {
                if (i.RequiresFeature is { } f && !edition.Has(f))
                    return new(i, false, edition.Kind == EditionKind.Classified ? EditionText.NotAvailableClassified : "nu este inclus în această ediție");
                if (i.RequiresNetwork && mode == AppMode.AirGapped) return new(i, false, NeedsNetworkReason);
                return new(i, true, "");
            }
            NavigationEntry N(string title, int tab, EditionFeature? feature = null, bool network = false)
            {
                if (feature is { } f && !edition.Has(f))
                    return new(title, tab, false, edition.Kind == EditionKind.Classified ? EditionText.NotAvailableClassified : "nu este inclus în această ediție");
                if (network && mode == AppMode.AirGapped) return new(title, tab, false, NeedsNetworkReason);
                return new(title, tab, true, "");
            }

            if (role == StationRole.Csirt)
            {
                var primary = new[]
                {
                    A(new("receive_evidence", "Primește probe de la o stație",
                        "Importă jurnalele, Prefetch, SRUM sau captura de rețea aduse de la stația afectată. Integritatea se verifică la intrare și ce lipsește se spune clar.",
                        TabInvestigation, IntentSource.ImportEvidence)),
                    A(new("what_happened", "Ce s-a întâmplat?",
                        "Cronologia simplă, lanțul incidentului și constatările cazului deschis, fiecare cu dovezile ei.",
                        TabInvestigation)),
                    A(new("what_now", "Ce fac acum?",
                        "Recomandări pe pași; izolarea unui program suspect se face cu confirmare și verificare după aplicare.",
                        TabContainment, IntentSource.None, EditionFeature.HostResponse)),
                    A(new("incident_report", "Raport de incident",
                        "Pentru conducere, IT, investigație sau audit. Se alege întâi pentru cine este raportul.",
                        TabInvestigation)),
                    A(new("open_cases", "Cazuri deschise",
                        "Un caz lucrat anterior, cu starea lui și cu reverificarea probelor la deschidere.",
                        TabHome, IntentSource.OpenExistingCase)),
                };
                var more = new[]
                {
                    A(new("check_this_computer", "Verifică acest calculator",
                        "Colectare de pe această stație și analiză completă.", TabInvestigation, IntentSource.CollectFromThisStation)),
                    A(new("timeline", "Cronologia evenimentelor", "Toate evenimentele în ordine, cu filtre.", TabTimeline)),
                    A(new("domain_mail", "Investigație domeniu și e-mail",
                        "Inventar Active Directory, autentificările unui utilizator, reguli de inbox și redirecționări.",
                        TabDomainMail, IntentSource.None, EditionFeature.DomainInvestigation, RequiresNetwork: true)),
                    A(new("evidence_history", "Istoricul probelor", "Cine a avut fiecare probă, când și ce s-a făcut cu ea.", TabChainOfCustody)),
                    A(new("send_to_vault", "Trimite în Memory Vault",
                        "Doar o propunere, după verificare; nimic nu devine fapt fără revizuirea unui om.", TabInvestigation)),
                };
                var nav = new[]
                {
                    N("Investigație completă (caz)", TabInvestigation),
                    N("Izolare procese suspecte", TabContainment, EditionFeature.HostResponse),
                    N("Investigație domeniu și e-mail", TabDomainMail, EditionFeature.DomainInvestigation, network: true),
                    N("Cronologia evenimentelor", TabTimeline),
                    N("Istoricul probelor", TabChainOfCustody),
                    N("Control stație", TabStationControl),
                    N("Autentificare și conturi", TabAccounts),
                };
                return new RoleProfile(role, "Stație de sprijin răspuns la incidente",
                    "Primiți probe de la stațiile afectate, aflați ce s-a întâmplat și ce urmează, și produceți raportul potrivit pentru cine îl citește.",
                    primary, more, nav);
            }
            else
            {
                var primary = new[]
                {
                    A(new("check_station", "Verifică această stație",
                        "Rulează controlul de conformitate pe perioada aleasă. Aplicația doar citește; nu modifică nimic pe stație.",
                        TabStationControl)),
                    A(new("check_media", "Verifică un suport (USB, CD/DVD)",
                        "Ce suporturi au fost conectate la stație și dacă sunt în registrul de medii.",
                        TabMediaRegister)),
                    A(new("who_worked", "Cine a lucrat și când",
                        "Conturi, autentificări locale și RDP, sesiuni privilegiate, pe utilizator, cu dovada fiecăreia.",
                        TabStationControl)),
                    A(new("open_previous", "Deschide un control anterior",
                        "Un control sau un caz salvat mai devreme; probele se reverifică la deschidere.",
                        TabHome, IntentSource.OpenExistingCase)),
                    A(new("control_report", "Raport pentru proces-verbal",
                        "PDF cu verificările, pe ce se bazează fiecare și golurile de probă, salvat în caz cu amprentă SHA-256.",
                        TabStationControl)),
                };
                var more = new[]
                {
                    A(new("analyze_evidence", "Analizează probe aduse de pe altă stație",
                        "Import de jurnale și artefacte dintr-un folder sau de pe un suport.", TabInvestigation, IntentSource.ImportEvidence)),
                    A(new("procedure_profile", "Profilul de proceduri",
                        "Orele de lucru, rotația jurnalelor, software aprobat, zone și transferuri, după care se judecă controlul.", TabProcedureProfile)),
                    A(new("users_register", "Registrul utilizatorilor", "Persoanele cu acces la stație și autorizațiile lor, introduse de organizație.", TabUsersRegister)),
                    A(new("accounts", "Persoane și carduri", "Conturile aplicației și cardurile înregistrate (numai administratorul).", TabAccounts)),
                };
                var nav = new[]
                {
                    N("Control stație", TabStationControl),
                    N("Registru medii", TabMediaRegister),
                    N("Registru utilizatori", TabUsersRegister),
                    N("Profil de proceduri", TabProcedureProfile),
                    N("Investigație completă (caz)", TabInvestigation),
                    N("Politici", TabPolicies),
                    N("Autentificare și conturi", TabAccounts),
                };
                return new RoleProfile(role, "Stație de control",
                    "Verificați o stație izolată după procedurile organizației și obțineți raportul pentru procesul-verbal. Nimic nu se modifică pe stația controlată.",
                    primary, more, nav);
            }
        }

        /// <summary>Guard used by tests: a profile never offers more than <see cref="MaxPrimary"/> primary intents and never a network intent in P1.</summary>
        public static IEnumerable<string> Violations(RoleProfile p, IEditionProfile edition)
        {
            if (p.PrimaryIntents.Count > MaxPrimary) yield return $"{p.PrimaryIntents.Count} intenții principale (maxim {MaxPrimary})";
            if (edition.Kind == EditionKind.Classified)
                foreach (var i in p.PrimaryIntents.Concat(p.MoreIntents).Where(i => i.Enabled && (i.Intent.RequiresNetwork || i.Intent.RequiresFeature is not null)))
                    yield return $"intenția „{i.Title}” este activă în ediția clasificată";
        }
    }
}
