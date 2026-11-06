using System;
using System.Linq;
using System.Text.RegularExpressions;
using LogAnalyzer.Core.Models;
using LogAnalyzer.Dfir.Language;

namespace LogAnalyzer.Core.Services.Network
{
    /// <summary>
    /// A live alert in plain words (owner 2026-11-06: „ca la prosti”, who did what, no event codes and no attack-tool names): „Pe 6 noiembrie 2026, la
    /// ora 15:20, pe calculatorul LAB-WS01, utilizatorul cu contul „labadmin” a șters jurnalul „Windows PowerShell” (lista cu ce s-a făcut pe calculator).”
    /// The alert kind is read from its technique id, which the engine sets and which never reaches the sentence.
    /// </summary>
    public static class LivePlain
    {
        public static string Tell(DetectedIssue issue, ParsedEvent ev, TimeZoneInfo zone)
        {
            string? F(string name) => LiveSecurityMonitoringEngine.Field(ev.XmlData, name);
            var when = ev.TimeCreated == default ? "" : PlainStory.When(new DateTimeOffset(ev.TimeCreated.ToUniversalTime(), TimeSpan.Zero), null, zone);
            var where = string.IsNullOrWhiteSpace(ev.MachineName) ? "" : $"pe calculatorul {ev.MachineName}";
            var who = PlainStory.Who(F("SubjectUserName") ?? F("User") ?? F("AccountName") ?? "", null);
            var tech = issue.MitreTechniqueId ?? "";
            string what = tech switch
            {
                "T1105" or "T1204.002" or "T1218.005" => $"{who} a adus un fișier din Internet sau a pornit un script descărcat (așa ajunge de obicei un program rău pe calculator)",
                "T1490" => $"{who} a încercat să șteargă copiile de rezervă ale calculatorului (pasul de dinaintea criptării fișierelor de către un virus care cere răscumpărare)",
                "T1059.001" when issue.IsTestAlert => "A sosit un mesaj de TEST al alertelor; nu s-a întâmplat nimic rău",
                "T1059.001" => $"{who} a pornit PowerShell ascuns, cu o comandă codificată sau care descarcă ceva din Internet",
                "T1003.001" => $"{who} a încercat să citească parolele din memoria calculatorului",
                "T1033" => $"{who} a verificat ce drepturi are și cine sunt administratorii",
                "T1136.001" => ev.EventId == 4732
                    ? $"{who} a pus contul „{Short(F("MemberName") ?? F("MemberSid") ?? "?")}” în grupul „{F("TargetUserName") ?? "?"}”"
                    : $"{who} a creat contul nou „{F("TargetUserName") ?? "?"}”",
                "T1070.001" => $"{who} a șters jurnalul „{Cleared(issue)}” (lista cu ce s-a făcut pe calculator)",
                "T1543.003" when issue.Severity == "High" => $"{who} a pornit programe pe acest calculator de pe alt calculator, prin rețea",
                "T1543.003" => $"{who} a instalat serviciul nou „{F("ServiceName") ?? "?"}”, care pornește singur programul {F("ImagePath") ?? "(necunoscut)"}",
                "T1110.001" => $"Cineva a greșit de mai multe ori parola contului „{F("TargetUserName") ?? "?"}”, de la {Source(F)} (poate titularul și-a uitat parola, poate cineva o ghicește)",
                "T1110.003" => $"Cineva a încercat parole pe multe conturi diferite, de la {Source(F)}",
                _ => $"calculatorul a semnalat: {Plainer(issue.Title)}",
            };
            var lead = string.Join(", ", new[] { when, where }.Where(s => s.Length > 0));
            if (lead.Length == 0) return Upper(what) + ".";
            return $"{lead}, {(what.StartsWith("Cineva", StringComparison.Ordinal) || what.StartsWith("A sosit", StringComparison.Ordinal) ? char.ToLowerInvariant(what[0]) + what[1..] : what)}.";
        }

        private static string Source(Func<string, string?> f) =>
            f("WorkstationName") is { } ws ? $"calculatorul {ws}" + (f("IpAddress") is { } ip ? $" (adresa {ip})" : "") : f("IpAddress") is { } a ? $"adresa {a}" : "o sursă necunoscută";

        private static string Short(string member)
        {
            var m = Regex.Match(member, "^CN=([^,]+)", RegexOptions.IgnoreCase);
            return m.Success ? m.Groups[1].Value : member;
        }

        // the engine already wrote the cleared log's name in its explanation: „Golire de jurnal (Windows PowerShell) …”
        private static string Cleared(DetectedIssue issue)
        {
            var m = Regex.Match(issue.Explanation ?? "", @"Golire de jurnal \(([^)]+)\)");
            return m.Success ? m.Groups[1].Value : "de evenimente";
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
