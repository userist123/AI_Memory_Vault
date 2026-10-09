using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using LogAnalyzer.Dfir.Model;

namespace LogAnalyzer.Dfir.IO;

/// <summary>
/// Versions of the files a run writes. A file without <c>schema_version</c> is the original format, "1.0"; readers accept
/// every version with a supported major number and refuse a newer major instead of guessing.
/// </summary>
public static class SchemaVersions
{
    public const string Legacy = "1.0";
    public const string Findings = "2.0";
    public const string Graph = "2.0";
    public const string VaultProposals = "2.0";
    public const string Timeline = "2.0";
    public const string Manifest = "1.0";
    /// <summary>WP3b: Analysis/dependencies.json, invalidations.json, integrity_recheck.json and the export/report manifests.</summary>
    public const string Dependencies = "1.0";
    public const string Invalidations = "1.0";
    public const string IntegrityRecheck = "1.0";
    public const string ExportManifest = "1.0";
    /// <summary>Files that stay a bare JSON array (existing consumers index into them): their version lives in the manifest.</summary>
    public const string ParsersInventory = "1.1";
    public const int SupportedMajor = 2;
    public const string ManifestFile = "schema_manifest.json";

    public static int Major(string version) => int.TryParse(version.Split('.')[0], out var m) ? m : 0;

    /// <summary>Throws when the file is from a newer major version than this reader understands.</summary>
    public static string Accept(string? version, string file)
    {
        var v = string.IsNullOrWhiteSpace(version) ? Legacy : version!;
        if (Major(v) < 1 || Major(v) > SupportedMajor)
            throw new InvalidDataException($"{file}: schema_version '{v}' nu este suportată (cel mult {SupportedMajor}.x).");
        return v;
    }

    /// <summary>Serialises <paramref name="value"/> (default System.Text.Json options, as before) and puts schema_version first.</summary>
    public static string WithVersion(object value, string version, JsonSerializerOptions? options = null)
    {
        var node = JsonSerializer.SerializeToNode(value, options ?? new JsonSerializerOptions())!.AsObject();
        var o = new JsonObject { ["schema_version"] = version };
        foreach (var kv in node.ToList()) { node.Remove(kv.Key); o[kv.Key] = kv.Value; }
        return o.ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
    }

    /// <summary>The version of a JSON object file; "1.0" for a legacy file, for a bare array and for a missing property.</summary>
    public static string ReadVersion(string path)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var v = doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty("schema_version", out var p) ? p.GetString() : null;
        return Accept(v, Path.GetFileName(path));
    }
}

/// <summary>Analysis/findings.json, read in either format. Legacy files simply lack the contract fields (defaults: NOT_ASSESSED, empty lists).</summary>
public sealed class FindingsFile
{
    [JsonPropertyName("schema_version")] public string SchemaVersion { get; set; } = SchemaVersions.Legacy;
    public List<Finding> Findings { get; set; } = [];
    public List<EvidenceGap> Gaps { get; set; } = [];
    public JsonElement? Collection { get; set; }
    public JsonElement? RejectedFindings { get; set; }

    public static FindingsFile Read(string path)
    {
        var f = JsonSerializer.Deserialize<FindingsFile>(File.ReadAllText(path)) ?? throw new InvalidDataException($"Empty JSON: {path}");
        f.SchemaVersion = SchemaVersions.Accept(f.SchemaVersion, Path.GetFileName(path));
        return f;
    }
}

/// <summary>Analysis/schema_manifest.json: the version of every output of the run, including the files that stay bare arrays.</summary>
public sealed class SchemaManifest
{
    [JsonPropertyName("schema_version")] public string SchemaVersion { get; set; } = SchemaVersions.Manifest;
    public string ContractVersion { get; set; } = LogAnalyzer.Dfir.Analysis.FindingContract.Version;
    public Dictionary<string, string> Files { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Heads of the custody and audit chains when the manifest was written (WP3b); absent in manifests written before.</summary>
    public LogAnalyzer.Dfir.Case.ChainAnchor? Anchor { get; set; }

    public static SchemaManifest ForRun() => new()
    {
        Files =
        {
            ["findings.json"] = SchemaVersions.Findings, ["graph.json"] = SchemaVersions.Graph, ["timeline.csv"] = SchemaVersions.Timeline,
            ["Exports/vault_proposals.jsonl"] = SchemaVersions.VaultProposals, ["Exports/vault_refused.json"] = SchemaVersions.Legacy,
            ["parsers.json"] = SchemaVersions.ParsersInventory, ["parsing.json"] = SchemaVersions.Legacy, ["detections.json"] = SchemaVersions.Legacy,
            ["rules.json"] = SchemaVersions.Legacy, ["anti_forensics.json"] = SchemaVersions.Legacy, ["run_state.json"] = SchemaVersions.Legacy,
            ["dependencies.json"] = SchemaVersions.Dependencies, ["invalidations.json"] = SchemaVersions.Invalidations, ["integrity_recheck.json"] = SchemaVersions.IntegrityRecheck,
        },
    };

    public void Write(string path) => File.WriteAllText(path, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));

    /// <summary>Reads the manifest of an analysis folder; a case written before versioning has none and every file is "1.0".</summary>
    public static SchemaManifest Read(string analysisDir)
    {
        var p = Path.Combine(analysisDir, SchemaVersions.ManifestFile);
        if (!File.Exists(p)) return new SchemaManifest { Files = new(StringComparer.OrdinalIgnoreCase) };
        var m = JsonSerializer.Deserialize<SchemaManifest>(File.ReadAllText(p)) ?? new SchemaManifest();
        SchemaVersions.Accept(m.SchemaVersion, SchemaVersions.ManifestFile);
        m.Files = new Dictionary<string, string>(m.Files, StringComparer.OrdinalIgnoreCase);
        return m;
    }

    public string VersionOf(string file) => Files.TryGetValue(file, out var v) ? v : SchemaVersions.Legacy;
}
