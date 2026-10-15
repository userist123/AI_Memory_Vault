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

    /// <summary>
    /// DE VERIFICAT notes (review of PR #290): a date that does not match but is not proof of a lost history (program files copied long before the first
    /// configuration), and a check made weaker because the Windows traces of the application are missing (Prefetch off, Amcache unreadable).
    /// </summary>
    public IReadOnlyList<string> ToVerify { get; init; } = [];

    /// <summary>The time of the first chained record (seq 1) of the application's audit chain; null when the chain gives none. Printed next to <see cref="Utc"/>.</summary>
    public DateTimeOffset? ChainFirstUtc { get; init; }
}

/// <summary>What a trace of the application shows: a file the application owns (its creation time can be rewritten by an administrator), a trace Windows keeps
/// that shows the file was SEEN (Amcache entry), or one that shows it RAN (Prefetch run times, Security 4688).</summary>
public enum AppTraceKind { AppFile, WindowsSeen, WindowsRun }

/// <summary>One trace of the application on the station with the time it was created or recorded.</summary>
public sealed record AppTrace(string What, DateTimeOffset Utc, AppTraceKind Kind = AppTraceKind.AppFile);

/// <summary>
/// The installation date of the application on this station (owner directive 2026-10-16), hardened against a rewritten audit (review of PR #288):
/// <list type="bullet">
/// <item>the whole audit chain must verify (<see cref="HashChain.Verify"/>: sequence, hashes, sealed head); a broken chain gives "unknown";</item>
/// <item>the first chained record must be seq 1 and one of the records written when the store is created (clock.seen, edition.policy_version,
/// auth.primary_admin_created), and the chain must contain auth.primary_admin_created; otherwise "unknown";</item>
/// <item>the date must not be in the future;</item>
/// <item>the date is the EARLIEST of the chain's first record and the other traces of the application (its program files, licence, database, case folders):
/// a store deleted and recreated later cannot move the date forward. A Windows RUN trace (Prefetch, Security 4688) earlier
/// than the chain by more than <see cref="MismatchTolerance"/> is a violation (history lost); an earlier file time or Amcache entry only is DE VERIFICAT
/// (<see cref="AppInstall.ToVerify"/>), and so is the absence of every Windows trace;</item>
/// <item>every auth.store_reinitialised record, and every primary administrator created again, is a violation.</item>
/// </list>
/// Read-only.
/// </summary>
public static class AppInstallDate
{
    public const string SourceName = "jurnalul de audit al aplicației (auth_audit.jsonl, primul rând înlănțuit)";
    public const string StoreCreatedEvent = "auth.primary_admin_created";
    public const string StoreReinitialisedEvent = "auth.store_reinitialised";
    /// <summary>Written at the very beginning of the first start, before licence activation (review of PR #290, round 2).</summary>
    public const string FirstStartEvent = "app.first_start";

    /// <summary>
    /// Records the first start of the application in its audit chain, at the very beginning of the start and BEFORE licence activation, when the chain does
    /// not exist yet (review of PR #290, round 2): a normal deployment (start, read the Hardware ID, wait for the licence, configure) keeps its first run
    /// inside the chain, so Prefetch / 4688 runs of that first start are not "before the chain". The chain is created unsealed; the authentication service
    /// seals an intact existing chain when it creates its key (trusted on first use, audited as auth.chain_sealed). Returns true when it wrote the record.
    /// </summary>
    public static bool RecordFirstStart(string? auditPath = null, DateTimeOffset? nowUtc = null)
    {
        auditPath ??= DefaultAuditPath;
        if (File.Exists(auditPath) && new FileInfo(auditPath).Length > 0) return false;
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(auditPath))!);
        new HashChain(auditPath).Append(new LogAnalyzer.Dfir.Auth.AuthAuditEntry(FirstStartEvent, "aplicație", "", "", "", "ok",
            "prima pornire a aplicației pe stație, înainte de activarea licenței și de crearea administratorului", nowUtc ?? DateTimeOffset.UtcNow, "", ""));
        return true;
    }
    /// <summary>Records that are written at the very start of a new store, before or with the primary administrator.</summary>
    public static readonly IReadOnlyList<string> FirstRecordKinds = [FirstStartEvent, ClockMemory.Event, LogAnalyzer.Dfir.Auth.AuthService.EditionPolicyVersionEvent, StoreCreatedEvent];
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
        // AuthService logs auth.store_reinitialised whenever the audit file already has records when the primary administrator is created. On a genuine first
        // installation the start-up records (app.first_start, clock.seen) come first, so only a reinitialisation AFTER a primary administrator existed is one.
        var firstCreationSeq = recs.Where(r => r.Event == StoreCreatedEvent).Select(r => r.Seq).DefaultIfEmpty(long.MaxValue).Min();
        foreach (var r in recs.Where(r => r.Event == StoreReinitialisedEvent && r.Seq > firstCreationSeq))
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

        // Review of PR #290: the date is the EARLIEST of the chain and every trace, so re-dating the application's own files can never move it later; what an
        // earlier trace MEANS depends on its kind. A Windows record that the application RAN before the chain began (Prefetch run, Security 4688) proves the
        // history before it was lost: NECONFORM. A file time or an Amcache entry only shows the program was there (copied, unpacked) before the first
        // configuration, which an installation procedure can explain: DE VERIFICAT, to be confirmed from the installation record.
        var valid = traces.Where(t => t.Utc <= now + FutureTolerance).ToList();
        var toVerify = new List<string>();
        if (!valid.Any(t => t.Kind != AppTraceKind.AppFile))
            toVerify.Add("urmele Windows ale aplicației lipsesc (Prefetch oprit sau necitit, Amcache necitit, nicio pornire în Security 4688): data instalării se sprijină doar pe jurnalul " +
                         "aplicației și pe orele fișierelor ei, pe care un administrator le poate rescrie; verificarea este mai slabă");
        var earliestRun = valid.Where(t => t.Kind == AppTraceKind.WindowsRun).OrderBy(t => t.Utc).FirstOrDefault();
        if (earliestRun is not null && earliestRun.Utc < chainDate - MismatchTolerance)
            violations.Add($"aplicația a rulat pe stație la {T(earliestRun.Utc)} ({earliestRun.What}), înainte de începutul jurnalului de audit ({T(chainDate)}): " +
                           "istoricul dinaintea primului rând a fost pierdut — jurnalul aplicației a fost reinițializat");
        var earliestOther = valid.Where(t => t.Kind != AppTraceKind.WindowsRun).OrderBy(t => t.Utc).FirstOrDefault();
        if (earliestOther is not null && earliestOther.Utc < chainDate - MismatchTolerance && !(earliestRun is not null && earliestRun.Utc < chainDate - MismatchTolerance))
            toVerify.Add($"data instalării nu se potrivește: primul rând al jurnalului de audit este din {T(chainDate)}, dar {earliestOther.What} există din {T(earliestOther.Utc)}; " +
                         "fie jurnalul a fost reinițializat, fie aplicația a fost copiată pe stație cu mult înainte de prima configurare — confirmați din procesul-verbal de instalare");
        var earliest = valid.OrderBy(t => t.Utc).FirstOrDefault();
        var date = earliest is not null && earliest.Utc < chainDate ? earliest.Utc : chainDate;
        var basis = date < chainDate
            ? $"data instalării aplicației = cea mai veche urmă a aplicației ({earliest!.What}, {T(date)}), mai veche decât primul rând al jurnalului de audit ({T(chainDate)})"
            : $"data instalării aplicației = ora primului rând înlănțuit (seq 1, „{first.Event}”) din {SourceName}, lanț verificat: {T(chainDate)}";
        return new AppInstall(date, basis, violations) { ToVerify = toVerify, ChainFirstUtc = chainDate };
    }

    private static string T(DateTimeOffset? t) => t is { } x ? x.ToString("yyyy-MM-dd HH:mm", System.Globalization.CultureInfo.InvariantCulture) + " UTC" : "oră necunoscută";
}
