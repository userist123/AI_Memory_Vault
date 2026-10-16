using System.Text.Json;
using System.Text.RegularExpressions;
using LogAnalyzer.Dfir.Reporting;

namespace LogAnalyzer.Dfir.Registers;

public enum MediaRegisterVerdict { Oficial, Neoficial, Invalid }

/// <summary>
/// The verdict on a signed media register offered for import. <see cref="Installable"/> only for an OFICIAL version that continues the installed one
/// (higher version, previous-version hash equal to the installed one's) or the first version on the station. A NEOFICIAL register is recorded in the
/// history but never used for compliance.
/// </summary>
public sealed record MediaRegisterCheck(MediaRegisterVerdict Verdict, bool Installable, string Headline, IReadOnlyList<VerificationLine> Lines,
                                       SignedMediaRegisterDoc? Doc, string Sha256, string SignerFingerprint, string SignerName)
{
    public string VerdictWord => Verdict switch { MediaRegisterVerdict.Oficial => "OFICIAL", MediaRegisterVerdict.Neoficial => "NEOFICIAL", _ => "INVALID" };

    public string ToPlainText() => VerdictWord + (Installable ? ", INSTALAT" : ", NEINSTALAT") + " — " + Headline + Environment.NewLine +
        string.Join(Environment.NewLine, Lines.Select(l => $"[{l.Mark}] {l.Text}"));
}

/// <summary>The official register a station has installed: version, hash and the fingerprint typed from the paper when it was imported.</summary>
public sealed record InstalledMediaRegister(int Version, string Sha256, string PaperFingerprint);

/// <summary>
/// Checks a signed media register like an officer-signed report (same officer list, same qualified-certificate checks, same paper-fingerprint rule):
/// the canonical file, the detached CMS over its exact bytes, the signer's certificate (non-repudiation, valid at the issue time, chain to an imported
/// qualified CA, not revoked in an imported CRL), the signer on the station's officer list, and the fingerprint typed from the proces-verbal. OFICIAL only
/// when all of them hold; then the version must continue the installed one (anti-rollback).
/// </summary>
public static class SignedMediaRegisterImport
{
    public static MediaRegisterCheck Verify(byte[] json, byte[] cms, ICmsDetachedVerifier verifier, IReadOnlyList<SecurityOfficerRow> trustedOfficers,
                                            string? paperFingerprint, QualifiedSignatureTrust trust, InstalledMediaRegister? installed)
    {
        var lines = new List<VerificationLine>();
        string sha = SignedMediaRegister.Sha256Hex(json), fp = "", name = "";
        SignedMediaRegisterDoc? doc = null;
        MediaRegisterCheck Done(MediaRegisterVerdict v, bool install, string headline) => new(v, install, headline, lines, doc, sha, fp, name);

        try { doc = SignedMediaRegister.Parse(json); }
        catch (FormatException ex) { lines.Add(new(false, "Fișierul registrului: " + ex.Message + ".")); return Done(MediaRegisterVerdict.Invalid, false, "fișierul registrului nu este valid"); }
        var problems = SignedMediaRegister.Problems(doc);
        if (problems.Count > 0) { lines.Add(new(false, "Conținutul registrului: " + string.Join("; ", problems) + ".")); return Done(MediaRegisterVerdict.Invalid, false, "conținutul registrului nu este valid"); }
        lines.Add(new(true, $"Registrul versiunea {doc.Version}, emis de stația „{doc.StationId}” la {doc.IssuedUtc}, {doc.Entries.Count} medii ({doc.Active.Count()} active), SHA-256 {sha}."));

        CmsResult c;
        try { c = verifier.Verify(cms, json); }
        catch (Exception ex) when (ex is System.Security.Cryptography.CryptographicException or ArgumentException or FormatException) { c = new CmsResult(false, null, ex.Message); }
        if (!c.Ok || c.Signer is null)
        {
            lines.Add(new(false, "Semnătura registrului (fișierul .p7s): " + (c.Problem.Length > 0 ? c.Problem : "nu se verifică") + "."));
            return Done(MediaRegisterVerdict.Invalid, false, "semnătura nu corespunde registrului (registrul a fost modificat sau semnătura este a altui fișier)");
        }
        var cert = c.Signer;
        fp = OfficerFingerprint.Of(cert); name = OfficerFingerprint.CommonName(cert);
        lines.Add(new(true, "Semnătura criptografică corespunde exact conținutului registrului."));

        var v = trust.Validate(cert, doc.Issued!.Value);
        if (v.Status == QualifiedCheck.Failed)
        {
            lines.Add(new(false, "Certificatul semnatarului: " + v.Message + "."));
            return Done(MediaRegisterVerdict.Invalid, false, "certificatul semnatarului nu este acceptat: " + v.Message);
        }
        lines.Add(new(true, $"Certificatul semnatarului „{name}” are utilizarea „nerepudiere” și era valabil la emiterea registrului."));
        lines.Add(new(v.Status == QualifiedCheck.Ok ? true : null, (v.Status == QualifiedCheck.Ok ? "Lanțul certificatului și revocarea: " : "Lanțul certificatului sau revocarea: ") + v.Message + "."));

        bool onList = trustedOfficers.Any(t => OfficerFingerprint.Same(t.Fingerprint, fp));
        bool paperGiven = OfficerFingerprint.Normalize(paperFingerprint).Length > 0;
        bool paperMatches = paperGiven && OfficerFingerprint.Same(paperFingerprint, fp);
        lines.Add(new(onList, onList ? $"Semnatarul este ofițer de securitate pe lista stației (amprenta {fp})." : $"Semnatarul (amprenta {fp}) NU este pe lista ofițerilor de securitate a stației."));
        if (paperGiven && !paperMatches)
        {
            lines.Add(new(false, "Amprenta de pe procesul-verbal NU este a certificatului care a semnat registrul."));
            return Done(MediaRegisterVerdict.Invalid, false, "registrul nu este semnat de ofițerul de pe procesul-verbal");
        }
        if (!onList && !paperMatches) return Done(MediaRegisterVerdict.Invalid, false, "semnatarul nu este ofițer de securitate înregistrat");
        lines.Add(new(paperMatches ? true : null, paperMatches ? "Amprenta certificatului este confirmată de pe procesul-verbal."
            : "Amprenta nu a fost comparată cu procesul-verbal: registrul este NEOFICIAL și nu se folosește la control. Scrieți amprenta de pe procesul-verbal și importați din nou."));

        bool official = paperMatches && onList && v.Status == QualifiedCheck.Ok;
        if (!official)
        {
            var why = !paperMatches ? "amprenta ofițerului nu a fost confirmată de pe procesul-verbal"
                    : !onList ? "ofițerul nu este pe lista stației (vezi verificarea A13)"
                    : "lanțul sau revocarea certificatului nu pot fi confirmate — importați certificatul CA și CRL-ul emitentului";
            return Done(MediaRegisterVerdict.Neoficial, false, "registru NEOFICIAL, nu se folosește la control: " + why);
        }

        // anti-rollback: only a version that continues the installed one is installed
        if (installed is null)
        {
            lines.Add(new(true, doc.Version == 1 ? "Este prima versiune a registrului." : $"Pe stație nu există un registru instalat; se instalează versiunea {doc.Version} (lanțul anterior nu se poate verifica aici)."));
            return Done(MediaRegisterVerdict.Oficial, true, $"registru OFICIAL versiunea {doc.Version}, semnat de „{name}”");
        }
        if (doc.Version <= installed.Version)
        {
            lines.Add(new(false, doc.Version == installed.Version && sha == installed.Sha256
                ? $"Versiunea {doc.Version} este deja instalată."
                : $"Versiunea {doc.Version} este mai veche sau egală cu cea instalată ({installed.Version}): o versiune anterioară nu înlocuiește una mai nouă."));
            return Done(MediaRegisterVerdict.Oficial, false, doc.Version == installed.Version && sha == installed.Sha256 ? "versiunea este deja instalată" : $"refuzat: versiune mai veche ({doc.Version} ≤ {installed.Version})");
        }
        if (!string.Equals(doc.PreviousVersionSha256, installed.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            lines.Add(new(false, $"Versiunea {doc.Version} nu continuă registrul instalat: versiunea anterioară numită ({Short(doc.PreviousVersionSha256)}) nu este cea instalată (v{installed.Version}, {Short(installed.Sha256)})."));
            return Done(MediaRegisterVerdict.Oficial, false, "refuzat: lanțul versiunilor nu continuă registrul instalat");
        }
        lines.Add(new(true, $"Continuă versiunea instalată {installed.Version} (amprenta versiunii anterioare corespunde)."));
        return Done(MediaRegisterVerdict.Oficial, true, $"registru OFICIAL versiunea {doc.Version}, semnat de „{name}”");
    }

    private static string Short(string h) => h.Length > 16 ? h[..16] : h;
}

/// <summary>The register in force on a station, as used by the control (check D04) and the transfer register: entries, or why there is none.</summary>
public sealed record ActiveMediaRegister(SignedMediaRegisterDoc? Doc, string Sha256, string Problem)
{
    public static ActiveMediaRegister None(string why) => new(null, "", why);
    public bool Usable => Doc is not null && Problem.Length == 0;
    /// <summary>When the FIRST official register was installed on this station, from the sealed audit; null = not known. Media seen only before it are history.</summary>
    public DateTimeOffset? FirstInstalledUtc { get; init; }
}

/// <summary>One version seen on this station (installed or not), kept so the officer sees every import.</summary>
public sealed class MediaRegisterHistoryRow
{
    public int Version { get; set; }
    public string Sha256 { get; set; } = "";
    public string Verdict { get; set; } = "";
    public bool Installed { get; set; }
    public string Reason { get; set; } = "";
    public string SignerFingerprint { get; set; } = "";
    public string ImportedUtc { get; set; } = "";
}

public sealed class MediaRegisterState
{
    public int ActiveVersion { get; set; }
    public string ActiveSha256 { get; set; } = "";
    public string ActiveFile { get; set; } = "";
    public string PaperFingerprint { get; set; } = "";
    public string SignerFingerprint { get; set; } = "";
    public List<MediaRegisterHistoryRow> History { get; set; } = [];
}

/// <summary>
/// The signed media registers of a station (%PROGRAMDATA%\LogAnalyzer\media_register): every installed version with its signature under versions\, and
/// state.json naming the active one. The state proves nothing alone: <see cref="LoadActive"/> re-reads the file, compares its hash, re-verifies the
/// signature against the fingerprint typed at import, and compares the version with the last import in the sealed audit (an older file put back by hand,
/// or a deleted state, is reported, never silently used).
/// </summary>
public sealed class SignedMediaRegisterStore(string? dir = null)
{
    public string Dir { get; } = dir ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "LogAnalyzer", "media_register");
    public static SignedMediaRegisterStore Shared { get; } = new();
    private string StatePath => Path.Combine(Dir, "state.json");
    private string VersionsDir => Path.Combine(Dir, "versions");

    public MediaRegisterState LoadState()
    {
        try { return File.Exists(StatePath) ? JsonSerializer.Deserialize<MediaRegisterState>(File.ReadAllText(StatePath), LogAnalyzer.Dfir.IO.Json.Options) ?? new() : new(); }
        catch (JsonException) { return new(); }
    }

    public InstalledMediaRegister? Installed()
    {
        var s = LoadState();
        return s.ActiveVersion > 0 ? new InstalledMediaRegister(s.ActiveVersion, s.ActiveSha256, s.PaperFingerprint) : null;
    }

    /// <summary>Records a check in the history; installs it (files + active pointer) when <see cref="MediaRegisterCheck.Installable"/>. Returns the state written.</summary>
    public MediaRegisterState Record(MediaRegisterCheck check, byte[] json, byte[] cms, string? paperFingerprint, DateTimeOffset nowUtc)
    {
        var s = LoadState();
        s.History.Add(new MediaRegisterHistoryRow
        {
            Version = check.Doc?.Version ?? 0, Sha256 = check.Sha256, Verdict = check.VerdictWord, Installed = check.Installable, Reason = check.Headline,
            SignerFingerprint = check.SignerFingerprint, ImportedUtc = SignedMediaRegister.FormatUtc(nowUtc),
        });
        if (check.Installable && check.Doc is { } doc)
        {
            Directory.CreateDirectory(VersionsDir);
            var file = SignedMediaRegister.FileName(doc.Version);
            File.WriteAllBytes(Path.Combine(VersionsDir, file), json);
            File.WriteAllBytes(Path.Combine(VersionsDir, file + SignedMediaRegister.SignatureExtension), cms);
            s.ActiveVersion = doc.Version; s.ActiveSha256 = check.Sha256; s.ActiveFile = file;
            s.PaperFingerprint = OfficerFingerprint.Normalize(paperFingerprint); s.SignerFingerprint = check.SignerFingerprint;
        }
        LogAnalyzer.Dfir.IO.Json.Write(StatePath, s);
        return s;
    }

    /// <summary>
    /// The active register for the control. <paramref name="lastImportedVersion"/> is the highest installed version recorded in the sealed audit
    /// (<see cref="LastInstalledVersion"/>); null = the audit is not available.
    /// </summary>
    public ActiveMediaRegister LoadActive(ICmsDetachedVerifier verifier, int? lastImportedVersion)
    {
        var s = LoadState();
        if (s.ActiveVersion <= 0)
            return lastImportedVersion is > 0
                ? ActiveMediaRegister.None($"jurnalul aplicației arată registrul semnat versiunea {lastImportedVersion} instalat, dar pe stație nu mai există niciun registru instalat: a fost șters în afara aplicației")
                : ActiveMediaRegister.None("pe stație nu este instalat niciun registru de medii semnat de ofițerul de securitate");
        try
        {
            var path = Path.Combine(VersionsDir, Path.GetFileName(s.ActiveFile));
            var json = File.ReadAllBytes(path);
            var cms = File.ReadAllBytes(path + SignedMediaRegister.SignatureExtension);
            var sha = SignedMediaRegister.Sha256Hex(json);
            if (sha != s.ActiveSha256) return new(null, sha, "fișierul registrului activ a fost modificat după instalare (amprenta nu mai corespunde)");
            var doc = SignedMediaRegister.Parse(json);
            var c = verifier.Verify(cms, json);
            if (!c.Ok || c.Signer is null) return new(null, sha, "semnătura registrului activ nu se mai verifică: " + c.Problem);
            if (!OfficerFingerprint.Same(OfficerFingerprint.Of(c.Signer), s.PaperFingerprint)) return new(null, sha, "registrul activ nu este semnat de ofițerul confirmat de pe procesul-verbal la import");
            if (doc.Version != s.ActiveVersion) return new(null, sha, "versiunea din fișier nu este cea înregistrată la instalare");
            if (lastImportedVersion is { } last && last > doc.Version)
                return new(null, sha, $"jurnalul aplicației arată instalată versiunea {last}, dar pe stație este activă versiunea {doc.Version}: o versiune mai veche a fost repusă în afara aplicației");
            return new(doc, sha, "");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException or System.Security.Cryptography.CryptographicException)
        {
            return ActiveMediaRegister.None("registrul activ nu poate fi citit: " + ex.Message);
        }
    }

    /// <summary>The time of the first "media_register.imported" entry with "installed=da" (events as (whenUtc, detail)); null when none.</summary>
    public static DateTimeOffset? FirstInstalledUtc(IEnumerable<(string WhenUtc, string Detail)> importedEvents)
    {
        DateTimeOffset? first = null;
        foreach (var (when, detail) in importedEvents)
            if (Regex.IsMatch(detail, @"(?:^|; )installed=da(?:;|$)")
                && DateTimeOffset.TryParse(when, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal, out var t)
                && (first is null || t < first)) first = t.ToUniversalTime();
        return first;
    }

    /// <summary>The highest version recorded as installed in the sealed audit ("media_register.imported" with "installed=da"); null when none.</summary>
    public static int? LastInstalledVersion(IEnumerable<string> importedDetails)
    {
        int? max = null;
        foreach (var d in importedDetails)
            if (Regex.Match(d, @"(?:^|; )version=(\d+);.*installed=da") is { Success: true } m && int.TryParse(m.Groups[1].Value, out var v))
                max = max is { } x && x > v ? x : v;
        return max;
    }
}
