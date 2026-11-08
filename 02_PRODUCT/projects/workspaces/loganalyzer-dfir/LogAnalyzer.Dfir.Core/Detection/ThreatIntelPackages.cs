using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using LogAnalyzer.Dfir.Case;
using LogAnalyzer.Dfir.IO;
using LogAnalyzer.Dfir.Reporting;

namespace LogAnalyzer.Dfir.Detection;

/// <summary>One rule file of a threat-intelligence package: path inside the package (ioc/, yara/ or sigma/) and its SHA-256.</summary>
public sealed record ThreatIntelFile(string Path, string Sha256);

/// <summary>
/// The signed manifest of an offline threat-intelligence package (master plan 6.12): <c>ti_package.json</c> plus the detached CMS
/// <c>ti_package.json.p7s</c> over its exact bytes. Versions form a chain (<see cref="PreviousVersionSha256"/>) like the signed media register.
/// </summary>
public sealed class ThreatIntelManifest
{
    public const string FormatId = "loganalyzer.ti_package/1";
    public const string FileName = "ti_package.json";
    public const string SignatureName = FileName + ".p7s";

    [JsonPropertyName("format")] public string Format { get; set; } = "";
    [JsonPropertyName("package_id")] public string PackageId { get; set; } = "";
    [JsonPropertyName("version")] public int Version { get; set; }
    [JsonPropertyName("previous_version_sha256")] public string PreviousVersionSha256 { get; set; } = "";
    [JsonPropertyName("issued_utc")] public DateTimeOffset IssuedUtc { get; set; }
    [JsonPropertyName("valid_until_utc")] public DateTimeOffset? ValidUntilUtc { get; set; }
    [JsonPropertyName("publisher")] public string Publisher { get; set; } = "";
    [JsonPropertyName("files")] public List<ThreatIntelFile> Files { get; set; } = [];

    public static ThreatIntelManifest Parse(byte[] json)
    {
        try { return JsonSerializer.Deserialize<ThreatIntelManifest>(json) ?? throw new FormatException("manifest gol"); }
        catch (JsonException ex) { throw new FormatException("manifest JSON invalid: " + ex.Message); }
    }

    private static readonly Dictionary<string, string> Allowed = new(StringComparer.OrdinalIgnoreCase) { ["ioc"] = ".csv", ["yara"] = ".yar", ["sigma"] = ".yml" };

    /// <summary>What makes the manifest unusable, before any signature is looked at. Paths are only ioc/*.csv, yara/*.yar, sigma/*.yml, never outside.</summary>
    public List<string> Problems()
    {
        var p = new List<string>();
        if (Format != FormatId) p.Add($"format necunoscut „{Format}”");
        // a whitelist (review 2026-11-08: "*", "|" or NUL in a signed id threw inside Approve's Directory.Move)
        if (!System.Text.RegularExpressions.Regex.IsMatch(PackageId ?? "", "^[A-Za-z0-9][A-Za-z0-9._-]{0,79}$")) p.Add("identificatorul pachetului lipsește sau nu este valid (litere, cifre, . _ -)");
        if (Version < 1) p.Add("versiunea trebuie să fie cel puțin 1");
        if (Files is null || Files.Count == 0) { p.Add("pachetul nu are fișiere de reguli"); return p; }
        if (Files.Any(f => f is null)) { p.Add("lista fișierelor conține o intrare goală"); return p; }
        foreach (var f in Files)
        {
            var parts = (f.Path ?? "").Split('/');
            if (parts.Length != 2 || !Allowed.TryGetValue(parts[0], out var ext) || !parts[1].EndsWith(ext, StringComparison.OrdinalIgnoreCase) ||
                parts[1].Length < ext.Length + 1 || parts[1].IndexOfAny(System.IO.Path.GetInvalidFileNameChars()) >= 0 || parts[1].StartsWith('.'))
                p.Add($"cale nepermisă „{f.Path}” (doar ioc/*.csv, yara/*.yar, sigma/*.yml)");
            if (!System.Text.RegularExpressions.Regex.IsMatch(f.Sha256 ?? "", "^[0-9a-fA-F]{64}$")) p.Add($"amprentă invalidă pentru „{f.Path}”");
        }
        if (Files.GroupBy(f => f.Path, StringComparer.OrdinalIgnoreCase).Any(g => g.Count() > 1)) p.Add("același fișier apare de două ori");
        return p;
    }
}

public enum ThreatIntelVerdict { Accepted, Refused }

public sealed record ThreatIntelCheck(ThreatIntelVerdict Verdict, string Headline, IReadOnlyList<VerificationLine> Lines, ThreatIntelManifest? Manifest, string ManifestSha256, string SignerFingerprint, string SignerName)
{
    public bool Ok => Verdict == ThreatIntelVerdict.Accepted;
}

public sealed record ThreatIntelHistoryEvent(DateTimeOffset TimestampUtc, string Actor, string Kind, string PackageId, int Version, string ManifestSha256, string Detail)
{
    public long Seq { get; init; }
}

public sealed record ActiveThreatIntel(string? RulesRoot, string PackageId, int Version, string Problem)
{
    public static ActiveThreatIntel None(string why) => new(null, "", 0, why);
}

public sealed record StagedPackage(string Id, string PackageId, int Version, string ManifestSha256, string StagedBy, DateTimeOffset StagedUtc);

/// <summary>
/// Offline threat-intelligence packages (master plan 6.12): stage → approve (activates) → roll back, kept under one store folder:
/// <c>Staging/&lt;id&gt;/</c>, <c>Versions/&lt;package&gt;-v&lt;n&gt;/</c>, <c>active.json</c> and the hash-chained <c>history.jsonl</c>. Nothing from a package
/// is used before an approver activates it, and the active version is verified again (signature, signer, every file hash) each time it is loaded:
/// a package that no longer verifies is not used, and the reason is shown. A new version must continue the highest version ever approved; going back
/// is a separate, audited rollback to a version that was approved before.
/// </summary>
public sealed class ThreatIntelStore
{
    /// <param name="sealKey">The machine chain key (as for the authentication audit). When <paramref name="sealed"/> is true the history head is sealed
    /// with it, so cutting lines off the end of the history (to re-enable an older version) breaks the chain; a null key then refuses every write.</param>
    /// <param name="refusal">Set by the caller when the store must not be used (its folder rights could not be confirmed); every operation then refuses with it.</param>
    public ThreatIntelStore(string root, byte[]? sealKey = null, bool @sealed = false, string refusal = "")
    {
        Root = Path.GetFullPath(root);
        if (@sealed) HashChain.RegisterHeadSeal(HistoryPath, sealKey);
        // a missing key is said as such, not reported as tampering (review round 2: a non-admin run saw "compromis")
        Refusal = refusal.Length > 0 ? refusal
                : @sealed && sealKey is null ? "cheia de sigilare a istoricului pachetelor nu poate fi citită (porniți aplicația dintr-un cont de administrator); pachetele nu se folosesc" : "";
    }

    /// <summary>Why this store cannot be used at all ("" = it can).</summary>
    public string Refusal { get; }

    public string Root { get; }
    private string StagingDir => Path.Combine(Root, "Staging");
    private string VersionsDir => Path.Combine(Root, "Versions");
    private string ActivePath => Path.Combine(Root, "active.json");
    public string HistoryPath => Path.Combine(Root, "history.jsonl");
    private HashChain History => new(HistoryPath);

    public static string Sha(byte[] b) => Convert.ToHexStringLower(SHA256.HashData(b));

    /// <summary>The signature, certificate, publisher and file checks shared by staging and loading. Pure: nothing is written.</summary>
    public static ThreatIntelCheck Verify(string folder, ICmsDetachedVerifier verifier, QualifiedSignatureTrust trust, IReadOnlyList<string> trustedPublishers)
    {
        var lines = new List<VerificationLine>();
        ThreatIntelManifest? m = null; string sha = "", fp = "", name = "";
        ThreatIntelCheck Done(bool ok, string headline) => new(ok ? ThreatIntelVerdict.Accepted : ThreatIntelVerdict.Refused, headline, lines, m, sha, fp, name);

        var mp = Path.Combine(folder, ThreatIntelManifest.FileName);
        var sp = Path.Combine(folder, ThreatIntelManifest.SignatureName);
        if (!File.Exists(mp) || !File.Exists(sp)) { lines.Add(new(false, $"Lipsește {ThreatIntelManifest.FileName} sau semnătura {ThreatIntelManifest.SignatureName}.")); return Done(false, "pachet incomplet"); }
        var json = File.ReadAllBytes(mp);
        sha = Sha(json);
        try { m = ThreatIntelManifest.Parse(json); }
        catch (FormatException ex) { lines.Add(new(false, ex.Message)); return Done(false, "manifestul nu este valid"); }
        var problems = m.Problems();
        if (problems.Count > 0) { lines.Add(new(false, string.Join("; ", problems) + ".")); return Done(false, "manifestul nu este valid"); }
        lines.Add(new(true, $"Pachetul „{m.PackageId}” versiunea {m.Version}, emis la {m.IssuedUtc:yyyy-MM-dd} de „{m.Publisher}”, {m.Files.Count} fișiere de reguli, SHA-256 {sha[..16]}…"));

        CmsResult c;
        try { c = verifier.Verify(File.ReadAllBytes(sp), json); }
        catch (Exception ex) when (ex is CryptographicException or ArgumentException or FormatException) { c = new CmsResult(false, null, ex.Message); }
        if (!c.Ok || c.Signer is null) { lines.Add(new(false, "Semnătura: " + (c.Problem.Length > 0 ? c.Problem : "nu se verifică") + ".")); return Done(false, "semnătura nu corespunde manifestului"); }
        fp = OfficerFingerprint.Of(c.Signer); name = OfficerFingerprint.CommonName(c.Signer);
        lines.Add(new(true, "Semnătura corespunde exact manifestului."));

        var v = trust.Validate(c.Signer, m.IssuedUtc);
        if (v.Status != QualifiedCheck.Ok) { lines.Add(new(false, "Certificatul semnatarului: " + v.Message + ".")); return Done(false, "certificatul semnatarului nu este confirmat: " + v.Message); }
        lines.Add(new(true, $"Certificatul „{name}” este valabil, cu lanț și revocare verificate."));
        if (!trustedPublishers.Any(t => OfficerFingerprint.Same(t, fp))) { lines.Add(new(false, $"Semnatarul (amprenta {fp}) NU este pe lista publicatorilor de încredere.")); return Done(false, "semnatarul nu este publicator de încredere"); }
        lines.Add(new(true, $"Semnatarul este pe lista publicatorilor de încredere (amprenta {fp})."));

        foreach (var f in m.Files)
        {
            var full = Path.Combine(folder, f.Path.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(full)) { lines.Add(new(false, $"Lipsește fișierul „{f.Path}”.")); return Done(false, "pachet incomplet"); }
            if (!string.Equals(Hashing.Sha256File(full), f.Sha256, StringComparison.OrdinalIgnoreCase)) { lines.Add(new(false, $"Fișierul „{f.Path}” nu are amprenta din manifest.")); return Done(false, "un fișier de reguli a fost modificat"); }
        }
        // exactly the listed files and nothing else (review 2026-11-08: a rule file planted next to them was loaded by the detection engine unverified)
        var listed = m.Files.Select(f => Path.GetFullPath(Path.Combine(folder, f.Path.Replace('/', Path.DirectorySeparatorChar)))).ToHashSet(StringComparer.OrdinalIgnoreCase);
        listed.Add(Path.GetFullPath(mp)); listed.Add(Path.GetFullPath(sp));
        var extra = Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories).Where(x => !listed.Contains(Path.GetFullPath(x))).ToList();
        if (extra.Count > 0) { lines.Add(new(false, $"În pachet există fișiere care nu sunt în manifest: {string.Join(", ", extra.Take(5).Select(x => Path.GetRelativePath(folder, x)))}.")); return Done(false, "pachetul conține fișiere nesemnate"); }
        lines.Add(new(true, "Toate fișierele de reguli au amprentele din manifest și nu există alte fișiere."));
        return Done(true, $"pachet valid: „{m.PackageId}” v{m.Version}, semnat de „{name}”");
    }

    private List<ThreatIntelHistoryEvent> ReadHistory()
    {
        var list = new List<ThreatIntelHistoryEvent>();
        if (!File.Exists(HistoryPath)) return list;
        foreach (var line in File.ReadLines(HistoryPath))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            try { if (JsonSerializer.Deserialize<ThreatIntelHistoryEvent>(line, Json.Options) is { } e) list.Add(e); } catch (JsonException) { }
        }
        return list;
    }

    public IReadOnlyList<ThreatIntelHistoryEvent> HistoryEvents() => ReadHistory();
    public ChainVerification VerifyHistory() => History.Verify();

    private void Record(string actor, string kind, string packageId, int version, string sha, string detail) =>
        History.Append(new ThreatIntelHistoryEvent(DateTimeOffset.UtcNow, actor, kind, packageId, version, sha, detail.ReplaceLineEndings(" ")));

    /// <summary>Highest version of a package ever approved (rollbacks do not lower it), with its manifest hash.</summary>
    private (int Version, string Sha) HighestApproved(string packageId) =>
        ReadHistory().Where(e => e.Kind == "approved" && e.PackageId == packageId).OrderByDescending(e => e.Version).Select(e => (e.Version, e.ManifestSha256)).FirstOrDefault();

    /// <summary>
    /// Copies only the manifest, its signature and the files it lists from <paramref name="sourceFolder"/> into a new staging folder, then verifies
    /// the copy (so what is checked is what will be used) and the version chain. A refused package is staged nowhere.
    /// </summary>
    public (ThreatIntelCheck Check, StagedPackage? Staged) Stage(string sourceFolder, string actor, ICmsDetachedVerifier verifier, QualifiedSignatureTrust trust, IReadOnlyList<string> trustedPublishers)
    {
        // any failure of an unsigned, malformed input is a refusal with nothing left behind (review 2026-11-08: "files": null threw out of Stage)
        if (Refusal.Length > 0) return (new ThreatIntelCheck(ThreatIntelVerdict.Refused, Refusal, [new(false, Refusal)], null, "", "", ""), null);
        if (!LogAnalyzer.Dfir.Auth.OperatorIdentity.MayEditAdministration)
            return (new ThreatIntelCheck(ThreatIntelVerdict.Refused, LogAnalyzer.Dfir.Auth.OperatorIdentity.AdministratorOnlyMessage, [new(false, LogAnalyzer.Dfir.Auth.OperatorIdentity.AdministratorOnlyMessage)], null, "", "", ""), null);
        var before = Directory.Exists(StagingDir) ? Directory.GetDirectories(StagingDir).ToHashSet(StringComparer.OrdinalIgnoreCase) : [];
        try { return StageCore(sourceFolder, actor, verifier, trust, trustedPublishers); }
        catch (Exception ex) when (ex is not OutOfMemoryException and not SealingKeyUnavailableException)
        {
            if (Directory.Exists(StagingDir)) foreach (var d in Directory.GetDirectories(StagingDir).Where(d => !before.Contains(d))) DeleteTree(d);
            return (new ThreatIntelCheck(ThreatIntelVerdict.Refused, "pachetul nu a putut fi citit: " + ex.Message, [new(false, ex.GetType().Name + ": " + ex.Message)], null, "", "", ""), null);
        }
    }

    private (ThreatIntelCheck Check, StagedPackage? Staged) StageCore(string sourceFolder, string actor, ICmsDetachedVerifier verifier, QualifiedSignatureTrust trust, IReadOnlyList<string> trustedPublishers)
    {
        var history = History.Verify();
        if (history.Status == ChainStatus.Broken)
            return (new ThreatIntelCheck(ThreatIntelVerdict.Refused, "istoricul pachetelor este compromis: " + history.Reason, [new(false, history.Message)], null, "", "", ""), null);
        var id = $"ST-{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}";
        var dir = Path.Combine(StagingDir, id);
        Directory.CreateDirectory(dir);
        try
        {
            File.Copy(Path.Combine(sourceFolder, ThreatIntelManifest.FileName), Path.Combine(dir, ThreatIntelManifest.FileName));
            File.Copy(Path.Combine(sourceFolder, ThreatIntelManifest.SignatureName), Path.Combine(dir, ThreatIntelManifest.SignatureName));
            var m = ThreatIntelManifest.Parse(File.ReadAllBytes(Path.Combine(dir, ThreatIntelManifest.FileName)));
            if (m.Problems().Count == 0)
                foreach (var f in m.Files)
                {
                    var src = Path.Combine(sourceFolder, f.Path.Replace('/', Path.DirectorySeparatorChar));
                    var dst = Path.Combine(dir, f.Path.Replace('/', Path.DirectorySeparatorChar));
                    Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
                    if (File.Exists(src)) File.Copy(src, dst);
                }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException) { /* the check below says what is missing or invalid */ }

        var check = Verify(dir, verifier, trust, trustedPublishers);
        if (check.Ok && check.Manifest is { } man)
        {
            var (hv, hsha) = HighestApproved(man.PackageId);
            var lines = check.Lines.ToList();
            string? refuse = null;
            if (hv > 0 && man.Version <= hv) refuse = $"versiunea {man.Version} nu este mai nouă decât cea mai nouă aprobată ({hv}); revenirea la o versiune veche se face doar prin „Revino la versiunea”";
            else if (hv > 0 && !string.Equals(man.PreviousVersionSha256, hsha, StringComparison.OrdinalIgnoreCase)) refuse = $"versiunea {man.Version} nu continuă versiunea aprobată {hv} (amprenta versiunii anterioare nu corespunde)";
            if (refuse is not null)
            {
                lines.Add(new(false, refuse + "."));
                check = check with { Verdict = ThreatIntelVerdict.Refused, Headline = "refuzat: " + refuse, Lines = lines };
            }
            else
            {
                lines.Add(new(true, hv == 0 ? "Este prima versiune aprobată a acestui pachet pe stație." : $"Continuă versiunea aprobată {hv}."));
                if (man.ValidUntilUtc is { } until && until < DateTimeOffset.UtcNow) lines.Add(new(null, $"Pachetul a expirat la {until:yyyy-MM-dd}: se poate aproba, dar regulile pot fi depășite."));
                check = check with { Lines = lines };
            }
        }
        if (!check.Ok)
        {
            DeleteTree(dir);
            Record(actor, "refused", check.Manifest?.PackageId ?? "", check.Manifest?.Version ?? 0, check.ManifestSha256, check.Headline);
            return (check, null);
        }
        var staged = new StagedPackage(id, check.Manifest!.PackageId, check.Manifest.Version, check.ManifestSha256, actor, DateTimeOffset.UtcNow);
        Record(actor, "staged", staged.PackageId, staged.Version, staged.ManifestSha256, id);
        return (check, staged);
    }

    /// <summary>Staged packages waiting for approval (the folder and a "staged" history record for it must both exist).</summary>
    public IReadOnlyList<StagedPackage> Staged()
    {
        if (!Directory.Exists(StagingDir)) return [];
        var hist = ReadHistory();
        var done = hist.Where(e => e.Kind is "approved" or "discarded").Select(e => e.Detail.Split(' ')[0]).ToHashSet(StringComparer.Ordinal);
        return hist.Where(e => e.Kind == "staged" && !done.Contains(e.Detail) && Directory.Exists(Path.Combine(StagingDir, e.Detail)))
                   .Select(e => new StagedPackage(e.Detail, e.PackageId, e.Version, e.ManifestSha256, e.Actor, e.TimestampUtc)).ToList();
    }

    /// <summary>
    /// Approves and activates a staged package. The staged copy is verified again first (it may have changed while waiting); the approver must
    /// give a reason. Approving a package one staged oneself is allowed (a single-analyst station) but recorded as such.
    /// </summary>
    public (StateResult Result, ThreatIntelCheck? Check) Approve(string stagedId, string approver, string reason, ICmsDetachedVerifier verifier, QualifiedSignatureTrust trust, IReadOnlyList<string> trustedPublishers)
    {
        if (Refusal.Length > 0) return (StateResult.Refuse(Refusal), null);
        if (!LogAnalyzer.Dfir.Auth.OperatorIdentity.MayEditAdministration) return (StateResult.Refuse(LogAnalyzer.Dfir.Auth.OperatorIdentity.AdministratorOnlyMessage), null);
        if (string.IsNullOrWhiteSpace(approver)) return (StateResult.Refuse("aprobare fără cont de operator"), null);
        if (string.IsNullOrWhiteSpace(reason)) return (StateResult.Refuse("aprobarea cere un motiv"), null);
        if (History.Verify() is { Status: ChainStatus.Broken } h) return (StateResult.Refuse("istoricul pachetelor este compromis: " + h.Reason), null);
        var staged = Staged().FirstOrDefault(s => s.Id == stagedId);
        if (staged is null) return (StateResult.Refuse("pachetul nu este în așteptare"), null);
        var dir = Path.Combine(StagingDir, stagedId);
        var check = Verify(dir, verifier, trust, trustedPublishers);
        if (!check.Ok || check.ManifestSha256 != staged.ManifestSha256)
        {
            Record(approver, "approval_refused", staged.PackageId, staged.Version, check.ManifestSha256, $"{stagedId} {check.Headline}");
            return (StateResult.Refuse("pachetul din așteptare nu se mai verifică: " + check.Headline), check);
        }
        var (hv, _) = HighestApproved(staged.PackageId);
        if (staged.Version <= hv) return (StateResult.Refuse($"între timp s-a aprobat versiunea {hv}"), check);
        var target = Path.Combine(VersionsDir, $"{staged.PackageId}-v{staged.Version}");
        if (Directory.Exists(target)) return (StateResult.Refuse("versiunea există deja în depozit"), check);
        Directory.CreateDirectory(VersionsDir);
        Directory.Move(dir, target);
        bool self = string.Equals(staged.StagedBy, approver, StringComparison.OrdinalIgnoreCase);
        Record(approver, "approved", staged.PackageId, staged.Version, staged.ManifestSha256, $"{stagedId} motiv: {reason}" + (self ? " (aprobat de aceeași persoană care l-a pus în așteptare)" : ""));
        WriteActive(staged.PackageId, staged.Version, staged.ManifestSha256);
        return (StateResult.Success, check);
    }

    public StateResult Discard(string stagedId, string actor, string reason)
    {
        if (Refusal.Length > 0) return StateResult.Refuse(Refusal);
        if (!LogAnalyzer.Dfir.Auth.OperatorIdentity.MayEditAdministration) return StateResult.Refuse(LogAnalyzer.Dfir.Auth.OperatorIdentity.AdministratorOnlyMessage);
        var staged = Staged().FirstOrDefault(s => s.Id == stagedId);
        if (staged is null) return StateResult.Refuse("pachetul nu este în așteptare");
        if (string.IsNullOrWhiteSpace(reason)) return StateResult.Refuse("renunțarea cere un motiv");
        DeleteTree(Path.Combine(StagingDir, stagedId));
        Record(actor, "discarded", staged.PackageId, staged.Version, staged.ManifestSha256, $"{stagedId} motiv: {reason}");
        return StateResult.Success;
    }

    /// <summary>Makes an earlier approved version active again (explicit, with a reason). It does not lower the version a new package must exceed.</summary>
    public StateResult Rollback(string packageId, int version, string actor, string reason)
    {
        if (Refusal.Length > 0) return StateResult.Refuse(Refusal);
        if (!LogAnalyzer.Dfir.Auth.OperatorIdentity.MayEditAdministration) return StateResult.Refuse(LogAnalyzer.Dfir.Auth.OperatorIdentity.AdministratorOnlyMessage);
        if (string.IsNullOrWhiteSpace(actor)) return StateResult.Refuse("revenire fără cont de operator");
        if (string.IsNullOrWhiteSpace(reason)) return StateResult.Refuse("revenirea cere un motiv");
        if (History.Verify() is { Status: ChainStatus.Broken } h) return StateResult.Refuse("istoricul pachetelor este compromis: " + h.Reason);
        var approved = ReadHistory().LastOrDefault(e => e.Kind == "approved" && e.PackageId == packageId && e.Version == version);
        if (approved is null || !Directory.Exists(Path.Combine(VersionsDir, $"{packageId}-v{version}"))) return StateResult.Refuse($"versiunea {version} a pachetului „{packageId}” nu a fost aprobată pe această stație");
        Record(actor, "rolled_back", packageId, version, approved.ManifestSha256, "motiv: " + reason);
        WriteActive(packageId, version, approved.ManifestSha256);
        return StateResult.Success;
    }

    private sealed record ActiveRecord(string PackageId, int Version, string ManifestSha256);

    private void WriteActive(string packageId, int version, string sha) => Json.Write(ActivePath, new ActiveRecord(packageId, version, sha));

    /// <summary>
    /// The rules root to give the detection engine, or why there is none. Verified on every load: the active pointer must name an approved version
    /// with the same manifest hash as the history, and the folder must still pass <see cref="Verify"/>.
    /// </summary>
    public ActiveThreatIntel LoadActive(ICmsDetachedVerifier verifier, QualifiedSignatureTrust trust, IReadOnlyList<string> trustedPublishers, string? snapshotRoot = null)
    {
        if (Refusal.Length > 0) return ActiveThreatIntel.None(Refusal);
        if (!File.Exists(ActivePath)) return ActiveThreatIntel.None("niciun pachet de informații despre amenințări activ");
        if (!File.Exists(HistoryPath) || ReadHistory().Count == 0) return ActiveThreatIntel.None("există un pachet activ, dar istoricul pachetelor lipsește; nu se folosește");
        if (History.Verify() is { Status: ChainStatus.Broken } h) return ActiveThreatIntel.None("istoricul pachetelor este compromis (" + h.Reason + "); pachetul nu se folosește");
        ActiveRecord? a;
        try { a = Json.Read<ActiveRecord>(ActivePath); }
        catch (Exception ex) when (ex is JsonException or InvalidDataException) { return ActiveThreatIntel.None("pointerul pachetului activ nu poate fi citit"); }
        var hist = ReadHistory();
        var last = hist.LastOrDefault(e => e.Kind is "approved" or "rolled_back");
        if (last is null || last.PackageId != a.PackageId || last.Version != a.Version || last.ManifestSha256 != a.ManifestSha256)
            return ActiveThreatIntel.None("pachetul activ nu corespunde ultimei aprobări din istoric; nu se folosește");
        var dir = Path.Combine(VersionsDir, $"{a.PackageId}-v{a.Version}");
        if (!Directory.Exists(dir)) return ActiveThreatIntel.None($"folderul versiunii {a.Version} lipsește");
        var check = Verify(dir, verifier, trust, trustedPublishers);
        if (!check.Ok || check.ManifestSha256 != a.ManifestSha256) return ActiveThreatIntel.None("pachetul activ nu se mai verifică: " + check.Headline);

        // The engine reads a private snapshot written from bytes hashed while being copied, never the store folder itself (review 2026-11-08: a file
        // swapped between this check and the engine's load, much later in the run, would have been used).
        // one snapshot per manifest, rewritten from the verified bytes each time; older snapshots are removed (review round 2: one new folder per load)
        var snapBase = snapshotRoot ?? Path.Combine(Path.GetTempPath(), "LogAnalyzer-ti");
        var snap = Path.Combine(snapBase, $"{a.PackageId}-v{a.Version}-{a.ManifestSha256[..16]}");
        if (Directory.Exists(snapBase))
            foreach (var old in Directory.GetDirectories(snapBase))
                try { DeleteTree(old); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* in use by a running analysis: left for the next load */ }
        foreach (var f in check.Manifest!.Files)
        {
            var bytes = File.ReadAllBytes(Path.Combine(dir, f.Path.Replace('/', Path.DirectorySeparatorChar)));
            if (!string.Equals(Sha(bytes), f.Sha256, StringComparison.OrdinalIgnoreCase)) return ActiveThreatIntel.None($"fișierul „{f.Path}” s-a schimbat în timpul citirii; pachetul nu se folosește");
            var dst = Path.Combine(snap, f.Path.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
            File.WriteAllBytes(dst, bytes);
        }
        var expired = check.Manifest.ValidUntilUtc is { } until && until < DateTimeOffset.UtcNow
            ? $"pachetul a expirat la {until:yyyy-MM-dd}; regulile lui pot fi depășite" : "";
        return new ActiveThreatIntel(snap, a.PackageId, a.Version, expired);
    }

    private static void DeleteTree(string dir)
    {
        if (!Directory.Exists(dir)) return;
        foreach (var f in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories)) File.SetAttributes(f, FileAttributes.Normal);
        Directory.Delete(dir, true);
    }
}
