namespace LogAnalyzer.Dfir.Windows.Investigation;

/// <summary>One family of evidence as the intake screen lists it: present (how many files) or missing.</summary>
public sealed record IncomingFamily(string Id, string HumanName, int Files, long Bytes)
{
    public bool Present => Files > 0;
    public string Text => Present ? $"{HumanName}: {Files} fișiere ({Bytes / 1024.0 / 1024.0:0.#} MB)" : $"{HumanName}: lipsește";
}

/// <summary>What a folder brought from another PC contains, before anything is imported (WP18 S4, "Primește probe de la o stație", step 2).</summary>
public sealed record IncomingEvidenceScan(string Folder, IReadOnlyList<IncomingFamily> Families, IReadOnlyList<string> Importable, IReadOnlyList<string> Problems)
{
    /// <summary>Families with a parser that are absent; "Alte fișiere" is never listed as missing.</summary>
    public IReadOnlyList<string> MissingFamilies => Families.Where(f => !f.Present && f.Id != IncomingEvidence.OtherFamily).Select(f => f.HumanName).ToList();
    public int PresentFamilies => Families.Count(f => f.Present);
    public int OtherFiles => Families.FirstOrDefault(f => f.Id == IncomingEvidence.OtherFamily)?.Files ?? 0;
    public bool HasAnything => Importable.Count > 0;
    public string Summary => !HasAnything
        ? "Folderul nu conține fișiere."
        : $"{Importable.Count} fișiere se vor importa ca probe; lipsesc: {(MissingFamilies.Count == 0 ? "nimic" : string.Join(", ", MissingFamilies))}."
          + (OtherFiles > 0 ? $" {OtherFiles} fișiere nu au un analizor dedicat: se importă oricum, cu amprentă, și se verifică cu indicatorii suspecți și regulile pe conținut." : "");
}

public static class IncomingEvidence
{
    public const string OtherFamily = "file";

    /// <summary>Families shown on the intake screen, keyed by the evidence types of <see cref="InvestigationPipeline.ImportType"/> (one rule for both).</summary>
    private static readonly (string Id, string Human, string[] Types)[] Families =
    [
        ("evtx", "Jurnale Windows", ["evtx"]),
        ("prefetch", "Istoricul pornirii programelor (Prefetch)", ["prefetch"]),
        ("srum", "Consumul de rețea al programelor (SRUM)", ["srum"]),
        ("pcapng", "Captură de rețea", ["pcapng"]),
        ("hives", "Registrul Windows (SYSTEM, SOFTWARE, NTUSER, Amcache)", ["system_hive", "software_hive", "ntuser_hive", "amcache"]),
        ("tasks", "Sarcini programate", ["task_xml"]),
        ("usn", "Jurnalul modificărilor de fișiere (USN)", ["usn_journal"]),
        ("lnk", "Scurtături și liste de documente recente", ["lnk", "jumplist_auto"]),
        ("browser", "Istoric de navigare", ["chromium_history", "firefox_places"]),
        (OtherFamily, "Alte fișiere (inclusiv exporturi de control)", ["file"]),
    ];

    /// <summary>Lists, without copying or hashing, what the folder holds. Every file is imported, exactly as <see cref="InvestigationPipeline.Import"/> does.</summary>
    public static IncomingEvidenceScan Scan(string folder)
    {
        var counts = Families.ToDictionary(f => f.Id, _ => (Files: 0, Bytes: 0L));
        var importable = new List<string>(); var problems = new List<string>();
        IncomingEvidenceScan Result() => new(folder, Families.Select(f => new IncomingFamily(f.Id, f.Human, counts[f.Id].Files, counts[f.Id].Bytes)).ToList(), importable, problems);
        if (!Directory.Exists(folder)) { problems.Add("Folderul nu există sau nu poate fi citit."); return Result(); }
        List<string> files;
        try { files = Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories).ToList(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { problems.Add(ex.Message); return Result(); }
        foreach (var f in files)
        {
            string type;
            long size = 0;
            try { type = InvestigationPipeline.ImportType(f); size = new FileInfo(f).Length; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { problems.Add($"{f}: {ex.Message}"); continue; }
            var fam = Families.FirstOrDefault(x => x.Types.Contains(type)).Id ?? OtherFamily;
            var c = counts[fam]; counts[fam] = (c.Files + 1, c.Bytes + size);
            importable.Add(f);
        }
        return Result();
    }
}
