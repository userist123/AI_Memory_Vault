namespace LogAnalyzer.Dfir.Windows.Investigation;

/// <summary>One family of evidence as the intake screen lists it: present (how many files) or missing.</summary>
public sealed record IncomingFamily(string Id, string HumanName, int Files, long Bytes)
{
    public bool Present => Files > 0;
    public string Text => Present ? $"{HumanName}: {Files} fișiere ({Bytes / 1024.0 / 1024.0:0.#} MB)" : $"{HumanName}: lipsește";
}

/// <summary>What a folder brought from another PC contains, before anything is imported (WP18 S4, "Primește probe de la o stație", step 2).</summary>
public sealed record IncomingEvidenceScan(string Folder, IReadOnlyList<IncomingFamily> Families, IReadOnlyList<string> Importable, IReadOnlyList<string> Ignored, IReadOnlyList<string> Problems)
{
    public int PresentFamilies => Families.Count(f => f.Present);
    public IReadOnlyList<string> MissingFamilies => Families.Where(f => !f.Present).Select(f => f.HumanName).ToList();
    public bool HasAnything => Importable.Count > 0;
    public string Summary => !HasAnything
        ? "Folderul nu conține probe pe care aplicația să le poată importa."
        : $"{Importable.Count} fișiere de probă în {PresentFamilies} familii; lipsesc: {(MissingFamilies.Count == 0 ? "nimic" : string.Join(", ", MissingFamilies))}."
          + (Ignored.Count > 0 ? $" {Ignored.Count} fișiere nu sunt probe recunoscute și nu se importă." : "");
}

public static class IncomingEvidence
{
    /// <summary>The families the import understands, in the order they are shown; the rules mirror <see cref="InvestigationPipeline.Import"/>.</summary>
    private static readonly (string Id, string Human, Func<string, bool> Match)[] Families =
    [
        ("evtx", "Jurnale Windows", f => f.EndsWith(".evtx", StringComparison.OrdinalIgnoreCase)),
        ("prefetch", "Istoricul pornirii programelor (Prefetch)", f => f.EndsWith(".pf", StringComparison.OrdinalIgnoreCase)),
        ("srum", "Consumul de rețea al programelor (SRUM)", f => Path.GetFileName(f).Equals("SRUDB.dat", StringComparison.OrdinalIgnoreCase)),
        ("pcapng", "Captură de rețea", f => f.EndsWith(".pcapng", StringComparison.OrdinalIgnoreCase)),
        ("hives", "Registrul Windows (SYSTEM, SOFTWARE, NTUSER, Amcache)", f =>
            Path.GetFileName(f).Equals("SYSTEM", StringComparison.OrdinalIgnoreCase) || Path.GetFileName(f).Equals("SOFTWARE", StringComparison.OrdinalIgnoreCase)
            || Path.GetFileName(f).Equals("NTUSER.DAT", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".hiv", StringComparison.OrdinalIgnoreCase)
            || Path.GetFileName(f).Equals("Amcache.hve", StringComparison.OrdinalIgnoreCase)),
        ("lnk", "Scurtături și liste de documente recente", f => f.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".automaticDestinations-ms", StringComparison.OrdinalIgnoreCase)),
        ("browser", "Istoric de navigare", f => Path.GetFileName(f).Equals("History", StringComparison.OrdinalIgnoreCase) || Path.GetFileName(f).Equals("places.sqlite", StringComparison.OrdinalIgnoreCase)),
        ("control", "Export de control de pe stația controlată", f => Path.GetFileName(f).Equals("control_report.json", StringComparison.OrdinalIgnoreCase)),
    ];

    /// <summary>Lists, without copying or hashing, what the folder holds. Hashing and custody happen at import, as they always did.</summary>
    public static IncomingEvidenceScan Scan(string folder)
    {
        var counts = Families.ToDictionary(f => f.Id, _ => (Files: 0, Bytes: 0L));
        var importable = new List<string>(); var ignored = new List<string>(); var problems = new List<string>();
        if (!Directory.Exists(folder))
            return new IncomingEvidenceScan(folder, Families.Select(f => new IncomingFamily(f.Id, f.Human, 0, 0)).ToList(), importable, ignored, ["Folderul nu există sau nu poate fi citit."]);
        IEnumerable<string> files;
        try { files = Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories).ToList(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return new IncomingEvidenceScan(folder, Families.Select(f => new IncomingFamily(f.Id, f.Human, 0, 0)).ToList(), importable, ignored, [ex.Message]); }
        foreach (var f in files)
        {
            var fam = Families.FirstOrDefault(x => x.Match(f));
            if (fam.Id is null) { ignored.Add(f); continue; }
            long size = 0;
            try { size = new FileInfo(f).Length; } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { problems.Add($"{f}: {ex.Message}"); }
            var c = counts[fam.Id]; counts[fam.Id] = (c.Files + 1, c.Bytes + size);
            importable.Add(f);
        }
        return new IncomingEvidenceScan(folder, Families.Select(f => new IncomingFamily(f.Id, f.Human, counts[f.Id].Files, counts[f.Id].Bytes)).ToList(), importable, ignored, problems);
    }
}
