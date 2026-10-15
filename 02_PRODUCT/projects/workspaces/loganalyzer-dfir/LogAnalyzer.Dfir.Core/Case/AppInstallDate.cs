using System.Text.Json;

namespace LogAnalyzer.Dfir.Case;

/// <summary>When the application was installed on this station, and what that rests on. <see cref="Utc"/> null = not known (<see cref="Basis"/> says why, as „sursă lipsă: …”).</summary>
public sealed record AppInstall(DateTimeOffset? Utc, string Basis)
{
    public static AppInstall Unknown(string why) => new(null, why);
}

/// <summary>
/// The installation date of the application on this station (owner directive 2026-10-16): the time of the FIRST chained record of the application's own audit
/// chain (<c>%ProgramData%\LogAnalyzer\auth\auth_audit.jsonl</c>; the record with the smallest <c>seq</c>, written when the primary administrator was created).
/// It is the date after which a Standalone station must never have been connected to a network. Read-only; a missing or unreadable chain gives
/// <see cref="AppInstall.Unknown"/>, never a guessed date.
/// </summary>
public static class AppInstallDate
{
    public const string SourceName = "jurnalul de audit al aplicației (auth_audit.jsonl, primul rând înlănțuit)";

    /// <summary>The audit chain of the installed application on this machine.</summary>
    public static string DefaultAuditPath => Path.Combine(LogAnalyzer.Dfir.Auth.AuthService.DefaultDir, LogAnalyzer.Dfir.Auth.AuthService.AuditFileName);

    public static AppInstall Read(string? auditPath = null)
    {
        auditPath ??= DefaultAuditPath;
        const string how = "cum se pornește: aplicația se instalează și se configurează administratorul principal (primul rând al jurnalului de audit se scrie atunci); analiza se rulează pe stația pe care este instalată aplicația";
        if (!File.Exists(auditPath))
            return AppInstall.Unknown($"sursă lipsă: {SourceName}, de ce lipsește: fișierul nu există pe această stație ({auditPath}), {how}.");
        long bestSeq = long.MaxValue; DateTimeOffset? best = null;
        try
        {
            using var fs = new FileStream(auditPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var sr = new StreamReader(fs);
            while (sr.ReadLine() is { } line)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                try
                {
                    using var doc = JsonDocument.Parse(line);
                    var root = doc.RootElement;
                    if (root.ValueKind != JsonValueKind.Object) continue;
                    if (!root.TryGetProperty("seq", out var s) || s.ValueKind != JsonValueKind.Number || !s.TryGetInt64(out var seq) || seq < 1) continue;
                    if (!root.TryGetProperty("hash", out var h) || h.ValueKind != JsonValueKind.String) continue;
                    if (!root.TryGetProperty("whenUtc", out var w) || w.ValueKind != JsonValueKind.String || !w.TryGetDateTimeOffset(out var when)) continue;
                    if (seq < bestSeq) { bestSeq = seq; best = when.ToUniversalTime(); }
                }
                catch (JsonException) { }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return AppInstall.Unknown($"sursă lipsă: {SourceName}, de ce lipsește: fișierul nu a putut fi citit ({ex.Message}), cum se pornește: rulați aplicația ca administrator pe stația pe care este instalată.");
        }
        if (best is null)
            return AppInstall.Unknown($"sursă lipsă: {SourceName}, de ce lipsește: fișierul nu conține niciun rând înlănțuit cu oră (jurnal gol sau dinainte de lanțul de audit), {how}.");
        return new AppInstall(best, $"data instalării aplicației = ora primului rând înlănțuit (seq {bestSeq}) din {SourceName}: {best.Value:yyyy-MM-dd HH:mm} UTC");
    }
}
