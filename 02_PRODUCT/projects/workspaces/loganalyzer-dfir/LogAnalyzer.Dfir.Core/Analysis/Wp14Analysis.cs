using LogAnalyzer.Dfir.Case;
using LogAnalyzer.Dfir.Model;
using LogAnalyzer.Dfir.Profile;
using LogAnalyzer.Dfir.Registers;

namespace LogAnalyzer.Dfir.Analysis;

/// <summary>What the WP14a step of a run produced: the findings, the SHA-256 of the register copies in the case, and the report line.</summary>
public sealed record Wp14Result(List<Finding> Findings, string MediaSha256, string UsersSha256, string Line);

/// <summary>
/// The WP14a step of an investigation run: takes the registers (the ones given, or the ones installed on this machine), copies those that are defined into
/// the case with their SHA-256 in custody, runs the media / air-gap rules and says, for each register, whether it was defined. An unreadable register file is
/// reported (audit <c>register.issue</c>) and treated as not defined - never as a clean result.
/// </summary>
public static class Wp14Analysis
{
    public static Wp14Result Run(CaseWorkspace ws, IReadOnlyList<TimelineEvent> timeline, Func<string> nextId, MediaRegister? media, UsersRegister? users, ProcedureProfile? profile,
        string systemZone, Wp14Data? data = null, string? mediaPath = null, string? usersPath = null)
    {
        media = Resolve(ws, media, mediaPath);
        users = Resolve(ws, users, usersPath);
        var mediaSha = media is { IsDefined: true } ? RegisterSnapshot.WriteTo(ws, media) : RegisterSnapshot.WriteTo(ws, new MediaRegister());
        var usersSha = users is { IsDefined: true } ? RegisterSnapshot.WriteTo(ws, users) : RegisterSnapshot.WriteTo(ws, new UsersRegister());
        var findings = Wp14Rules.Run(timeline, nextId, new Wp14Input { Scope = ws.Info.Scope, Media = media, Users = users, Profile = profile, SystemZone = systemZone, Data = data });
        string Part(string title, IRegisterData? r, string sha) => $"{title}: " + (r is { IsDefined: true } ? $"definit ({r.Cells().Count} rânduri; copie în caz Analysis/{r.Kind}_register.json, SHA-256 {sha})" : "nedefinit (nu există rânduri; observațiile nu se compară cu un registru)");
        var zone = systemZone.Length > 0 ? $" Zona sistemului declarată: {systemZone}." : " Zona sistemului nu este declarată.";
        return new Wp14Result(findings, mediaSha, usersSha, Part("Registru medii", media, mediaSha) + "; " + Part("Registru utilizatori", users, usersSha) + "." + zone);
    }

    private static T? Resolve<T>(CaseWorkspace ws, T? given, string? path) where T : class, IRegisterData, new()
    {
        if (given is not null) return given;
        var loaded = RegisterStore.Load<T>(path);
        foreach (var i in loaded.Issues.Where(i => i.IsError)) ws.Audit("register.issue", i.ToString());
        return loaded.Register;
    }
}
