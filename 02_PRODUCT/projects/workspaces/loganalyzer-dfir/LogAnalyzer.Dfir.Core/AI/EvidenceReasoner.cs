using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using LogAnalyzer.Dfir.Analysis;
using LogAnalyzer.Dfir.Model;

namespace LogAnalyzer.Dfir.AI;

/// <summary>One item the model may cite: a finding, an event behind it, an anti-forensics trace or a gap, with its evidence pointer.</summary>
public sealed record CatalogItem(string Id, string Kind, string Text, IReadOnlyList<EvidenceRef> Evidence);

public sealed record Citation(string Item, string Kind, IReadOnlyList<EvidenceRef> Evidence);

/// <summary>A statement that passed validation. It is still model output: classification UNPROVEN until the analyst checks its citations.</summary>
public sealed record AiStatement(string Section, string Text, IReadOnlyList<Citation> Citations)
{
    public string Classification => "UNPROVEN";
}

public sealed record RejectedStatement(string Section, string Text, IReadOnlyList<string> Cites, string Reason);

/// <summary>The model's output after validation, with everything needed to reproduce and audit the call.</summary>
public sealed record AiReasoning(
    string Model, string ModelDigest, string Endpoint, string CatalogSha256, string PromptSha256, string ResponseSha256, DateTimeOffset CreatedUtc,
    IReadOnlyList<AiStatement> Accepted, IReadOnlyList<RejectedStatement> Rejected, int CatalogItems, int OmittedItems, string RawResponse);

/// <summary>
/// Evidence-constrained reasoning (master spec §25). The model is not a parser and sees no raw evidence: it receives a numbered
/// catalog built from verified case results and must cite catalog items for every statement. Deterministic rules then reject a
/// statement that cites nothing, cites an unknown item, cites more than <see cref="MaxCites"/> items, names a file, address,
/// domain or date absent from the items it cites, or — for hypotheses and alternative explanations — is not worded as a
/// possibility or claims certainty. What survives is shown with its evidence (EvidenceId, locator, SHA-256) and stays UNPROVEN;
/// what is rejected is kept with the reason. A second model used as a judge was tried on the real case and left out: it
/// accepted a false statement and rejected correctly hedged ones (docs/dfir/AI_FORENSIC_REASONING.md).
/// </summary>
public static class EvidenceReasoner
{
    public static readonly string[] Sections = ["summary", "hypotheses", "alternative_explanations", "correlation_explanation", "next_steps", "questions"];

    /// <summary>A statement citing more items than this is not specific enough to be checked (citing everything proves nothing).</summary>
    public const int MaxCites = 4;

    public static (List<CatalogItem> Items, int Omitted) BuildCatalog(IReadOnlyList<Finding> findings, IReadOnlyList<TimelineEvent> timeline,
        IReadOnlyList<AntiForensicCheck> antiForensics, IReadOnlyList<EvidenceGap> gaps, int maxItems = 120)
    {
        var byLocator = timeline.GroupBy(e => (e.EvidenceId, e.Locator)).ToDictionary(g => g.Key, g => g.First());
        var items = new List<CatalogItem>();
        int omitted = 0, e = 0, g = 0;
        static string Cut(string s, int n) { s = s.ReplaceLineEndings(" "); return s.Length <= n ? s : s[..n] + "…"; }

        foreach (var f in findings.OrderByDescending(f => f.Severity).ThenByDescending(f => f.Confidence))
        {
            if (items.Count >= maxItems) { omitted++; continue; }
            items.Add(new CatalogItem(f.FindingId, "finding",
                $"{f.RuleId} [{f.Severity}, {f.Classification.ToSpec()}, încredere {f.Confidence.ToSpec()}] {f.Title}. {Cut(f.Description, 600)}" +
                (f.FirstSeenUtc is { } t ? $" (de la {t:yyyy-MM-ddTHH:mm:ssZ})" : "") + (f.MitreTechniqueId.Length > 0 ? $" ATT&CK {f.MitreTechniqueId}." : ""),
                f.SupportingEvidence));
            foreach (var r in f.SupportingEvidence.Take(4))
            {
                if (!byLocator.TryGetValue((r.EvidenceId, r.Locator), out var ev) || items.Any(i => i.Kind == "event" && i.Evidence[0] == r)) continue;
                if (items.Count >= maxItems) { omitted++; continue; }
                items.Add(new CatalogItem($"E{++e}", "event",
                    $"{ev.Source} {ev.EventId} la {ev.Time.UtcIso} ({ev.TimeSemantics}): {Cut(ev.Summary, 300)}" + (ev.Path.Length > 0 ? $" Cale: {ev.Path}." : ""), [r]));
            }
        }
        foreach (var a in antiForensics.Where(a => a.Result == AntiForensicResult.Detected))
        {
            if (items.Count >= maxItems) { omitted++; continue; }
            items.Add(new CatalogItem(a.Id, "anti-forensics", $"{a.Technique} DETECTED: {Cut(a.Reason, 600)}", a.Evidence.Take(10).ToList()));
        }
        foreach (var gap in gaps)
        {
            if (items.Count >= maxItems) { omitted++; continue; }
            items.Add(new CatalogItem($"G{++g}", "gap", $"Gol de probă {gap.Artifact} ({gap.Status.ToSpec()}): {Cut(gap.Reason, 300)}. Impact: {Cut(gap.Impact, 200)}", []));
        }
        return (items, omitted);
    }

    public static string CatalogText(IReadOnlyList<CatalogItem> items) => string.Join("\n", items.Select(i => $"[{i.Id}] ({i.Kind}) {i.Text}"));

    public const string SystemPrompt =
        "You assist a digital forensics analyst. You are not a parser and you have no other source than the numbered catalog below. " +
        "Every statement must cite the catalog item ids it rests on, in \"cites\". Do not name any file, path, IP address, domain or date " +
        "that is not written in the items you cite. Keep observed facts, correlations and hypotheses apart; say when evidence is missing " +
        "(gap items). Each statement is ONE sentence with ONE fact or ONE hypothesis; cite 1 to 4 items, only those that state it. " +
        "Word hypotheses and alternative explanations as possibilities (poate, ar putea, posibil) and never as confirmed. " +
        "Write in Romanian. Sections: summary, hypotheses, alternative_explanations, correlation_explanation, next_steps, questions.";

    public static JsonObject Schema()
    {
        JsonObject Statement() => new()
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["text"] = new JsonObject { ["type"] = "string" },
                ["cites"] = new JsonObject { ["type"] = "array", ["items"] = new JsonObject { ["type"] = "string" }, ["minItems"] = 1, ["maxItems"] = MaxCites },
            },
            ["required"] = new JsonArray("text", "cites"),
        };
        var props = new JsonObject();
        foreach (var s in Sections) props[s] = new JsonObject { ["type"] = "array", ["items"] = Statement() };
        return new JsonObject { ["type"] = "object", ["properties"] = props, ["required"] = new JsonArray(Sections.Select(s => (JsonNode)s).ToArray()) };
    }

    // Things a statement may not introduce on its own: file names, IPv4 addresses, domain names, dates.
    private static readonly Regex FileName = new(@"\b[\w\-.]+\.(?:exe|dll|sys|ps1|bat|cmd|vbs|js|hta|msi|zip|rar|7z|lnk|evtx|pf|targets|csproj|scr)\b", RegexOptions.IgnoreCase);
    private static readonly Regex Ipv4 = new(@"\b(?:\d{1,3}\.){3}\d{1,3}\b");
    private static readonly Regex Domain = new(@"\b(?:[a-z0-9-]+\.)+(?:com|net|org|ro|io|info|biz|ru|cn|xyz|top|cyou|site|online|app|dev|co|uk|de|eu)\b", RegexOptions.IgnoreCase);
    private static readonly Regex Date = new(@"\b20\d{2}-\d{2}-\d{2}\b");
    private static readonly Regex Hedge = new(@"\b(poate|pot|ar putea|ar fi|posibil\w*|probabil\w*|sugerea\w*|ipotez\w*|eventual|could|may|might|possibly)\b", RegexOptions.IgnoreCase);
    private static readonly Regex Certainty = new(@"\b(confirmat\w*|confirmă|dovede\w*|dovedit\w*|cert|sigur|fără îndoială|confirms?|confirmed|proves?|proven)\b", RegexOptions.IgnoreCase);

    public static IEnumerable<string> Mentions(string text) =>
        FileName.Matches(text).Select(m => m.Value)
            .Concat(Ipv4.Matches(text).Select(m => m.Value))
            .Concat(Domain.Matches(text).Select(m => m.Value).Where(d => !FileName.IsMatch(d)))
            .Concat(Date.Matches(text).Select(m => m.Value))
            .Distinct(StringComparer.OrdinalIgnoreCase);

    /// <summary>Parses the model's JSON and keeps only statements that pass every deterministic rule.</summary>
    public static (List<AiStatement> Accepted, List<RejectedStatement> Rejected) Validate(string modelJson, IReadOnlyList<CatalogItem> catalog)
    {
        var known = catalog.ToDictionary(i => i.Id, StringComparer.OrdinalIgnoreCase);
        var accepted = new List<AiStatement>();
        var rejected = new List<RejectedStatement>();
        JsonNode? root;
        try { root = JsonNode.Parse(modelJson); }
        catch (JsonException ex) { rejected.Add(new("*", modelJson.Length > 300 ? modelJson[..300] : modelJson, [], "răspunsul nu este JSON valid: " + ex.Message)); return (accepted, rejected); }
        foreach (var section in Sections)
        {
            if (root?[section] is not JsonArray arr) { rejected.Add(new(section, "", [], "secțiunea lipsește din răspuns")); continue; }
            foreach (var node in arr)
            {
                var text = node?["text"]?.GetValue<string>()?.Trim() ?? "";
                var cites = (node?["cites"] as JsonArray)?.Select(c => c?.GetValue<string>()?.Trim().Trim('[', ']') ?? "").Where(c => c.Length > 0).Distinct().ToList() ?? [];
                if (text.Length == 0) continue;
                string? why = null;
                if (cites.Count == 0) why = "nu citează nicio probă";
                else if (cites.Where(c => !known.ContainsKey(c)).ToList() is { Count: > 0 } unknown) why = "citează elemente inexistente: " + string.Join(", ", unknown);
                else if (cites.Count > MaxCites) why = $"citează {cites.Count} elemente (maximum {MaxCites}): afirmația nu este specifică";
                else
                {
                    var cited = string.Join("\n", cites.Select(c => known[c].Text));
                    var foreign = Mentions(text).Where(m => !cited.Contains(m, StringComparison.OrdinalIgnoreCase)).ToList();
                    if (foreign.Count > 0) why = "menționează ce nu apare în elementele citate: " + string.Join(", ", foreign);
                    else if (section is "hypotheses" or "alternative_explanations")
                    {
                        if (Certainty.Match(text) is { Success: true } m) why = $"o ipoteză prezentată ca sigură („{m.Value}”)";
                        else if (!Hedge.IsMatch(text)) why = "o ipoteză care nu este formulată ca posibilitate";
                    }
                }
                if (why is not null) rejected.Add(new(section, text, cites, why));
                else accepted.Add(new AiStatement(section, text, cites.Select(c => new Citation(known[c].Id, known[c].Kind, known[c].Evidence)).ToList()));
            }
        }
        return (accepted, rejected);
    }

    public static string Sha(string s) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(s)));
}

/// <summary>
/// A local model served by Ollama on this machine. Only a loopback IP literal is accepted (no DNS lookup, no other host), the
/// system proxy and redirects are disabled, and the model's digest is recorded, so the call never leaves the station.
/// </summary>
public sealed class LocalModelClient
{
    private readonly HttpClient _http;
    public Uri Endpoint { get; }
    public string Model { get; }

    public LocalModelClient(string endpoint, string model, TimeSpan? timeout = null)
    {
        var uri = new Uri(endpoint);
        if (uri.Scheme != Uri.UriSchemeHttp || !IPAddress.TryParse(uri.Host.Trim('[', ']'), out var ip) || !IPAddress.IsLoopback(ip))
            throw new ArgumentException($"Modelul se apelează doar pe o adresă loopback numerică (127.0.0.1 sau [::1]), nu „{endpoint}”.");
        if (string.IsNullOrWhiteSpace(model)) throw new ArgumentException("Lipsește numele modelului.");
        Endpoint = uri;
        Model = model;
        _http = new HttpClient(new HttpClientHandler { UseProxy = false, AllowAutoRedirect = false }) { BaseAddress = uri, Timeout = timeout ?? TimeSpan.FromMinutes(15) };
    }

    /// <summary>The installed model's digest (sha256 of its manifest), or an exception if the model is not installed.</summary>
    public async Task<string> DigestAsync(CancellationToken ct = default)
    {
        var tags = await _http.GetFromJsonAsync<JsonObject>("/api/tags", ct) ?? throw new InvalidOperationException("Ollama nu a răspuns la /api/tags.");
        var m = (tags["models"] as JsonArray)?.FirstOrDefault(x => x?["name"]?.GetValue<string>() == Model)
            ?? throw new InvalidOperationException($"Modelul {Model} nu este instalat local.");
        return m["digest"]?.GetValue<string>() ?? "";
    }

    public async Task<string> ChatAsync(string system, string user, JsonObject schema, int contextTokens, CancellationToken ct = default)
    {
        var body = new JsonObject
        {
            ["model"] = Model, ["stream"] = false, ["think"] = false, ["format"] = schema,
            ["options"] = new JsonObject { ["temperature"] = 0, ["seed"] = 0, ["num_ctx"] = contextTokens },
            ["messages"] = new JsonArray(new JsonObject { ["role"] = "system", ["content"] = system }, new JsonObject { ["role"] = "user", ["content"] = user }),
        };
        using var resp = await _http.PostAsync("/api/chat", new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"), ct);
        var text = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode) throw new InvalidOperationException($"Ollama {(int)resp.StatusCode}: {text}");
        return JsonNode.Parse(text)?["message"]?["content"]?.GetValue<string>() ?? throw new InvalidOperationException("Răspuns Ollama fără conținut.");
    }

    /// <summary>Catalog → model → validation. Nothing the model writes reaches the analyst without passing <see cref="EvidenceReasoner.Validate"/>.</summary>
    public async Task<AiReasoning> ReasonAsync(IReadOnlyList<CatalogItem> catalog, int omitted, int contextTokens = 16384, CancellationToken ct = default)
    {
        var digest = await DigestAsync(ct);
        var catalogText = EvidenceReasoner.CatalogText(catalog);
        var user = "Răspunde în limba română.\nCATALOG\n" + catalogText;
        var raw = await ChatAsync(EvidenceReasoner.SystemPrompt, user, EvidenceReasoner.Schema(), contextTokens, ct);
        var (accepted, rejected) = EvidenceReasoner.Validate(raw, catalog);
        return new AiReasoning(Model, digest, Endpoint.ToString(), EvidenceReasoner.Sha(catalogText), EvidenceReasoner.Sha(EvidenceReasoner.SystemPrompt + "\n" + user),
            EvidenceReasoner.Sha(raw), DateTimeOffset.UtcNow, accepted, rejected, catalog.Count, omitted, raw);
    }
}
