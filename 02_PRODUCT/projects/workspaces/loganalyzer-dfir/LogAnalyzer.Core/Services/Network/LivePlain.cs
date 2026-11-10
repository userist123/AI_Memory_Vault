using System;
using System.Linq;
using System.Text.RegularExpressions;
using LogAnalyzer.Core.Models;
using LogAnalyzer.Dfir.Analysis;
using LogAnalyzer.Dfir.Language;

namespace LogAnalyzer.Core.Services.Network
{
    /// <summary>
    /// A live alert in plain words (owner 2026-11-06: „ca la prosti”, who did what, no event codes and no attack-tool names): „Pe 6 noiembrie 2026, la
    /// ora 15:20, pe calculatorul LAB-WS01, utilizatorul cu contul „labadmin” a șters jurnalul „Windows PowerShell” (lista cu ce s-a făcut pe calculator).”
    /// The alert kind is read from its technique id, which the engine sets and which never reaches the sentence. The sentence never claims more than the event
    /// shows: a typed command is told as a typed command (its result is another event), an account that is only the run-as account of a service is not
    /// credited with installing it, and a field the event does not carry is left out (never „?”).
    /// </summary>
    public static class LivePlain
    {
        public static string Tell(DetectedIssue issue, ParsedEvent ev, TimeZoneInfo zone)
        {
            string? F(string name) => LiveSecurityMonitoringEngine.Field(ev.XmlData, name);
            var when = ev.TimeCreated == default ? "" : PlainStory.When(new DateTimeOffset(ev.TimeCreated.ToUniversalTime(), TimeSpan.Zero), null, zone);
            var where = string.IsNullOrWhiteSpace(ev.MachineName) ? "" : $"pe calculatorul {ev.MachineName}";
            var who = PlainStory.Who(F("SubjectUserName") ?? F("User") ?? F("AccountName") ?? "", null, ev.MachineName ?? "");
            var tech = issue.MitreTechniqueId ?? "";
            var msg = ev.Message ?? "";
            string what = tech switch
            {
                "T1105" or "T1204.002" or "T1218.005" => $"{who} a adus un fișier din Internet sau a pornit un script descărcat (așa ajunge de obicei un program rău pe calculator)",
                "T1490" => $"{who} a încercat să șteargă copiile de rezervă ale calculatorului (pasul de dinaintea criptării fișierelor de către un virus care cere răscumpărare)",
                "T1059.001" when issue.IsTestAlert => "A sosit un mesaj de TEST al alertelor; nu s-a întâmplat nimic rău",
                "T1059.001" => $"{who} a pornit PowerShell ascuns, cu o comandă codificată sau care descarcă ceva din Internet",
                "T1003.001" => $"{who} a încercat să citească parolele din memoria calculatorului",
                "T1033" => $"{who} a verificat ce drepturi are și cine sunt administratorii",
                "T1136.001" => NewAccount(ev, F, msg, who),
                "T1070.001" => ClearedLog(issue, ev, msg, F),
                "T1543.003" => ServiceOrPsExec(ev, F, msg, who),
                // lab 2026-11-10: PowerShell Direct from the Hyper-V host leaves failed logons for this placeholder account inside the virtual machine
                "T1110.001" when string.Equals(F("TargetUserName"), "?<PSDirectVMLegacy>", System.StringComparison.OrdinalIgnoreCase) =>
                    "Gazda Hyper-V s-a conectat la această mașină virtuală prin PowerShell Direct; conexiunea lasă autentificări eșuate pentru contul tehnic „?<PSDirectVMLegacy>” (nu este o parolă ghicită; verificați dacă administrarea de pe gazdă era așteptată)",
                "T1110.001" => $"Cineva a greșit de mai multe ori parola {(F("TargetUserName") is { } t ? $"contului „{t}”" : "unui cont")}, de la {Source(F)} (poate titularul și-a uitat parola, poate cineva o ghicește)",
                "T1110.003" => $"Cineva a încercat parole pe multe conturi diferite, de la {Source(F)}",
                _ => $"calculatorul a semnalat: {Plainer(issue.Title)}",
            };
            var lead = string.Join(", ", new[] { when, where }.Where(s => s.Length > 0));
            if (lead.Length == 0) return Upper(what) + ".";
            return $"{lead}, {PlainStory.LowerFirst(what)}.";
        }

        // 4720 / 4732 are the result; anything else that got here is a typed „net user … /add”, whose result is another event
        private static string NewAccount(ParsedEvent ev, Func<string, string?> f, string msg, string who)
        {
            if (ev.EventId == 4732)
            {
                var member = f("MemberName") is { } mn && !AccountResolver.IsSid(mn) ? Short(mn) : null;
                var group = f("TargetUserName");
                // without a name the SID tells only whether the account is of a domain or of this computer; neither is guessed (round-2 review: a domain member was "un cont local")
                var known = member is not null ? $"contul „{member}”"
                    : f("MemberSid") is { } ms && LogAnalyzer.Dfir.Analysis.Wp11Rules.DomainGroupOfSid(ms) is { Length: > 0 } dg ? $"grupul de domeniu „{dg}”"
                    : "un cont al cărui nume nu apare în eveniment";
                return $"{who} a pus {known} {(group is not null ? $"în grupul „{group}”" : "într-un grup")}";
            }
            if (ev.EventId == 4720)
                return $"{who} a creat {(f("TargetUserName") is { } t ? $"contul nou „{t}”" : "un cont nou")}";
            var m = Regex.Match(msg, @"net1?(?:\.exe)?\s+user\s+(?:""([^""]+)""|([^\s/""]+))", RegexOptions.IgnoreCase);
            var account = m.Success ? (m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value) : "";
            return $"{who} a dat comanda de creare a unui cont nou{(account.Length > 0 ? $" „{account}”" : "")} („net user … /add”); rezultatul nu se vede în acest eveniment";
        }

        // 1102 / 104 are the result (the account is in <UserData><LogFileCleared>); a typed „wevtutil cl X” is only the command
        private static string ClearedLog(DetectedIssue issue, ParsedEvent ev, string msg, Func<string, string?> f)
        {
            if (ev.EventId is 1102 or 104)
            {
                var (user, _) = LogClearAssessment.SubjectFromXml(ev.XmlData);
                var who = PlainStory.Who(user.Length > 0 ? user : f("SubjectUserName") ?? f("User") ?? "", null, ev.MachineName ?? "");
                var cleared = Cleared(issue);
                return cleared.Length > 0 ? $"{who} a șters jurnalul „{cleared}” (lista cu ce s-a făcut pe calculator)" : $"{who} a șters jurnalul de evenimente (evenimentul nu arată care anume)";
            }
            var actor = PlainStory.Who(f("SubjectUserName") ?? f("User") ?? "", null, ev.MachineName ?? "");
            var m = Regex.Match(msg, @"wevtutil(?:\.exe)?\s+(?:cl|clear-log)\s+(?:""([^""]+)""|([^\s""]+))", RegexOptions.IgnoreCase);
            var log = m.Success ? (m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value) : "";
            return $"{actor} a dat comanda de golire a {(log.Length > 0 ? $"jurnalului „{log}”" : "unui jurnal")}";
        }

        // 7045: the account is the run-as account of the service, not who installed it: no one is credited; PsExec: the direction is unknown
        private static string ServiceOrPsExec(ParsedEvent ev, Func<string, string?> f, string msg, string who)
        {
            var name = f("ServiceName"); var image = f("ImagePath");
            bool psexecInstall = ev.EventId == 7045 && ((name ?? "").Contains("PSEXESVC", StringComparison.OrdinalIgnoreCase) || (image ?? "").Contains("PSEXESVC", StringComparison.OrdinalIgnoreCase));
            bool psexec = psexecInstall || msg.Contains("psexec", StringComparison.OrdinalIgnoreCase) || msg.Contains("psexesvc", StringComparison.OrdinalIgnoreCase);
            // the program started here (a process start names its account): who started it, and that it runs commands on ANOTHER computer (round-2 review)
            if (psexec && !psexecInstall && ev.EventId is 4688 or 1)
                return $"{who} a pornit aici un program care rulează comenzi pe alt calculator, prin rețea; verificați pe care calculator și ce comenzi";
            if (psexec && !psexecInstall)
                return "S-a folosit pe acest calculator un program care rulează comenzi de la un calculator la altul, prin rețea; verificați de unde și spre unde";
            var s = "A fost instalat " + (name is not null ? $"serviciul nou „{name}”" : "un serviciu nou");
            if (image is not null) s += $", care pornește singur programul {image}";
            return s;
        }

        private static string Source(Func<string, string?> f) =>
            f("WorkstationName") is { } ws ? $"calculatorul {ws}" + (f("IpAddress") is { } ip ? $" (adresa {ip})" : "") : f("IpAddress") is { } a ? $"adresa {a}" : "o sursă necunoscută";

        private static string Short(string member)
        {
            var m = Regex.Match(member, "^CN=([^,]+)", RegexOptions.IgnoreCase);
            return m.Success ? m.Groups[1].Value : member;
        }

        // the engine already wrote the cleared log's name in its explanation: „Golire de jurnal (Windows PowerShell) …”; "" when the event does not name it
        private static string Cleared(DetectedIssue issue)
        {
            var m = Regex.Match(issue.Explanation ?? "", @"Golire de jurnal \(([^)]+)\)");
            return m.Success && !m.Groups[1].Value.StartsWith("jurnal nenumit", StringComparison.OrdinalIgnoreCase) ? m.Groups[1].Value : "";
        }

        // the title without its icon, the „ALERTĂ …:” prefix and the bracketed tool names
        private static string Plainer(string title)
        {
            var t = Regex.Replace(title ?? "", @"^[^\p{L}\[]+", "");
            t = Regex.Replace(t, @"^ALERTĂ \p{L}+:\s*", "");
            t = Regex.Replace(t, @"\s*\([^)]*\)", "");
            return PlainStory.WithoutTechnicalTerms(t);
        }

        private static string Upper(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];
    }
}
