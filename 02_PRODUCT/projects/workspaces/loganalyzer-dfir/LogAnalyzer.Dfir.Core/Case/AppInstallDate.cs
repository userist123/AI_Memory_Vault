using System.Text.Json;

namespace LogAnalyzer.Dfir.Case;

/// <summary>
/// When the application was installed on this station, what that rests on, and what contradicts it. <see cref="Utc"/> null = not determinable (<see cref="Basis"/>
/// says why, as „sursă lipsă: …”); then every network connection on a Standalone station is NECONFORM. <see cref="Violations"/> are NECONFORM facts on a
/// Standalone station by themselves: a re-initialised authentication store, a chain whose first record does not match the other traces of the application.
/// </summary>
public sealed record AppInstall(DateTimeOffset? Utc, string Basis, IReadOnlyList<string> Violations)
{
    public static AppInstall Unknown(string why, IReadOnlyList<string>? violations = null) => new(null, why, violations ?? []);
}

/// <summary>One trace of the application on the station with the time it was created (program files, licence, database, case folder).</summary>
public sealed record AppTrace(string What, DateTimeOffset Utc);

/// <summary>
/// The installation date of the application on this station (owner directive 2026-10-16), hardened against a rewritten audit (review of PR #288):
/// <list type="bullet">
/// <item>the whole audit chain must verify (<see cref="HashChain.Verify"/>: sequence, hashes, sealed head); a broken chain gives "unknown";</item>
/// <item>the first chained record must be seq 1 and one of the records written when the store is created (clock.seen, edition.policy_version,
/// auth.primary_admin_created), and the chain must contain auth.primary_admin_created; otherwise "unknown";</item>
/// <item>the date must not be in the future;</item>
/// <item>the date is the EARLIEST of the chain's first record and the other traces of the application (its program files, licence, database, case folders):
/// a store deleted and recreated later cannot move the date forward. A difference larger than <see cref="MismatchTolerance"/> is itself a violation
/// („data instalării nu se potrivește — jurnalul aplicației a fost reinițializat”);</item>
/// <item>every auth.store_reinitialised record, and every primary administrator created again, is a violation.</item>
/// </list>
/// Read-only.
/// </summary>
public static class AppInstallDate
{
    public const string SourceName = "jurnalul de audit al aplicației (auth_audit.jsonl, primul rând înlănțuit)";
    public const string StoreCreatedEvent = "auth.primary_admin_created";
    public const string StoreReinitialisedEvent = "auth.store_reinitialised";
    /// <summary>Records that are written at the very start of a new store, before or with the primary administrator.</summary>
    public static readonly IReadOnlyList<string> FirstRecordKinds = [ClockMemory.Event, LogAnalyzer.Dfir.Auth.AuthService.EditionPolicyVersionEvent, StoreCreatedEvent];
    /// <summary>The program files may be copied some time before the first start: up to this much earlier is normal.</summary>
    public static readonly TimeSpan MismatchTolerance = TimeSpan.FromDays(2);
    private static readonly TimeSpan FutureTolerance = TimeSpan.FromMinutes(5);

    /// <summary>The audit chain of the installed application on this machine.</summary>
    public static string DefaultAuditPath => Path.Combine(LogAnalyzer.Dfir.Auth.AuthService.DefaultDir, LogAnalyzer.Dfir.Auth.AuthService.AuditFileName);

    /// <summary>
    /// The other traces of the application on this station, with their creation time: the program files next to the executable, license.lic,
    /// %LocalAppData%\LogAnalyzer\LogAnalyzer.db and the case folders. A trace that cannot be read is left out (it never moves the date forward).
    /// </summary>
    public static IReadOnlyList<AppTrace> DefaultTraces()
    {
        var list = new List<AppTrace>();
        void Add(string what, string path, bool dir)
        {
            try
            {
                if (dir ? Directory.Exists(path) : File.Exists(path))
                    list.Add(new(what, new DateTimeOffset(dir ? Directory.GetCreationTimeUtc(path) : File.GetCreationTimeUtc(path), TimeSpan.Zero)));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
        var baseDir = AppContext.BaseDirectory;
        try
        {
            foreach (var f in Directory.EnumerateFiles(baseDir, "LogAnalyzer*.exe").Concat(Directory.EnumerateFiles(baseDir, "LogAnalyzer*.dll")).Take(50))
                Add($"fișierul aplicației {Path.GetFileName(f)}", f, false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        Add("licența (license.lic)", Path.Combine(baseDir, "license.lic"), false);
        var local = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LogAnalyzer");
        Add("baza de date LogAnalyzer.db", Path.Combine(local, "LogAnalyzer.db"), false);
        Add("folderul cazurilor", Path.Combine(local, "Cases"), true);
        try
        {
            if (Directory.Exists(Path.Combine(local, "Cases")))
                foreach (var d in Directory.EnumerateDirectories(Path.Combine(local, "Cases")).Take(200)) Add($"folderul de caz {Path.GetFileName(d)}", d, true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        return list;
    }

    private sealed record Rec(long Seq, string Event, DateTimeOffset? When);

    public static AppInstall Read(string? auditPath = null, IReadOnlyList<AppTrace>? traces = null, DateTimeOffset? nowUtc = null)
    {
        auditPath ??= DefaultAuditPath;
        traces ??= DefaultTraces();
        var now = nowUtc ?? DateTimeOffset.UtcNow;
        const string how = "cum se pornește: aplicația se instalează și se configurează administratorul principal (primul rând al jurnalului de audit se scrie atunci); analiza se rulează pe stația pe care este instalată aplicația";
        if (!File.Exists(auditPath))
            return AppInstall.Unknown($"sursă lipsă: {SourceName}, de ce lipsește: fișierul nu există pe această stație ({auditPath}), {how}.");

        ChainVerification verdict;
        var recs = new List<Rec>();
        try
        {
            verdict = new HashChain(auditPath).Verify();
            using var fs = new FileStream(auditPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var sr = new StreamReader(fs);
            while (sr.ReadLine() is { } line)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                try
                {
                    using var doc = JsonDocument.Parse(line);
                    var root = doc.RootElement;
                    if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("seq", out var s) || !s.TryGetInt64(out var seq)) continue;
                    var ev = root.TryGetProperty("event", out var e) && e.ValueKind == JsonValueKind.String ? e.GetString() ?? "" : "";
                    DateTimeOffset? when = root.TryGetProperty("whenUtc", out var w) && w.ValueKind == JsonValueKind.String && w.TryGetDateTimeOffset(out var t) ? t.ToUniversalTime() : null;
                    recs.Add(new(seq, ev, when));
                }
                catch (JsonException) { }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return AppInstall.Unknown($"sursă lipsă: {SourceName}, de ce lipsește: fișierul nu a putut fi citit ({ex.Message}), cum se pornește: rulați aplicația ca administrator pe stația pe care este instalată.");
        }

        // Reinitialisations are reported whatever else is found.
        var violations = new List<string>();
        foreach (var r in recs.Where(r => r.Event == StoreReinitialisedEvent))
            violations.Add($"magazinul de autentificare al aplicației a fost reinițializat offline la {T(r.When)} (rândul seq {r.Seq}, {StoreReinitialisedEvent}): conturile și parola au fost recreate după ștergerea lor");
        var creations = recs.Where(r => r.Event == StoreCreatedEvent).ToList();
        foreach (var r in creations.Skip(1))
            violations.Add($"administratorul principal a fost creat din nou la {T(r.When)} (rândul seq {r.Seq}): magazinul de autentificare a fost reinițializat");

        if (verdict.Status == ChainStatus.Broken)
            return AppInstall.Unknown($"data instalării nu poate fi stabilită: {SourceName} nu se verifică ({verdict.Reason}); un jurnal modificat nu poate da data instalării, deci nicio conectare nu poate fi pusă înaintea ei",
                [.. violations, $"jurnalul de audit al aplicației nu se verifică: {verdict.Reason}"]);
        var first = recs.OrderBy(r => r.Seq).FirstOrDefault();
        if (first is null || verdict.Status == ChainStatus.Legacy && recs.Count == 0)
            return AppInstall.Unknown($"sursă lipsă: {SourceName}, de ce lipsește: fișierul nu conține niciun rând înlănțuit (jurnal gol sau dinainte de lanțul de audit), {how}.", violations);
        if (first.Seq != 1 || !FirstRecordKinds.Contains(first.Event) || creations.Count == 0)
            return AppInstall.Unknown($"data instalării nu poate fi stabilită: primul rând al jurnalului de audit (seq {first.Seq}, „{first.Event}”) nu este rândul scris la crearea magazinului de autentificare" +
                                      (creations.Count == 0 ? $" și jurnalul nu conține {StoreCreatedEvent}" : ""),
                [.. violations, "jurnalul de audit al aplicației nu începe cu crearea magazinului de autentificare: jurnalul a fost reinițializat sau trunchiat"]);
        if (first.When is not { } chainDate)
            return AppInstall.Unknown($"data instalării nu poate fi stabilită: primul rând al jurnalului de audit nu are oră", violations);
        if (chainDate > now + FutureTolerance)
            return AppInstall.Unknown($"data instalării nu poate fi stabilită: primul rând al jurnalului de audit are o oră din viitor ({T(chainDate)})",
                [.. violations, $"jurnalul de audit al aplicației începe la o oră din viitor ({T(chainDate)}): ceas greșit sau jurnal modificat"]);

        var earliest = traces.Where(t => t.Utc <= now + FutureTolerance).OrderBy(t => t.Utc).FirstOrDefault();
        if (earliest is not null && earliest.Utc < chainDate - MismatchTolerance)
        {
            violations.Add($"data instalării nu se potrivește — jurnalul aplicației a fost reinițializat: primul rând al jurnalului de audit este din {T(chainDate)}, " +
                           $"dar {earliest.What} există din {T(earliest.Utc)}");
            return new AppInstall(earliest.Utc, $"data instalării aplicației = cea mai veche urmă a aplicației ({earliest.What}, {T(earliest.Utc)}), mai veche decât primul rând al jurnalului de audit ({T(chainDate)})", violations);
        }
        var date = earliest is not null && earliest.Utc < chainDate ? earliest.Utc : chainDate;
        return new AppInstall(date, $"data instalării aplicației = ora primului rând înlănțuit (seq 1, „{first.Event}”) din {SourceName}, lanț verificat: {T(chainDate)}" +
                                    (date < chainDate ? $"; urma cea mai veche a aplicației ({earliest!.What}) este puțin mai veche, deci se folosește {T(date)}" : ""), violations);
    }

    private static string T(DateTimeOffset? t) => t is { } x ? x.ToString("yyyy-MM-dd HH:mm", System.Globalization.CultureInfo.InvariantCulture) + " UTC" : "oră necunoscută";
}
