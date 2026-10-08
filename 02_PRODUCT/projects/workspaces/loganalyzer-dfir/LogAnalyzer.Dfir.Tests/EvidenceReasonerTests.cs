using System.Net.Sockets;
using LogAnalyzer.Core.Services.Connectivity;
using LogAnalyzer.Dfir.AI;
using LogAnalyzer.Dfir.Analysis;
using LogAnalyzer.Dfir.Model;
using Xunit;

namespace LogAnalyzer.Dfir.Tests;

/// <summary>Runs only when an Ollama server answers on 127.0.0.1:11434 with the model named by LADFIR_OLLAMA_MODEL (default qwen2.5:7b-instruct).</summary>
public sealed class OllamaFactAttribute : FactAttribute
{
    public static string Model => Environment.GetEnvironmentVariable("LADFIR_OLLAMA_MODEL") is { Length: > 0 } m ? m : "qwen2.5:7b-instruct";

    public OllamaFactAttribute()
    {
        try
        {
            using var c = new TcpClient();
            if (!c.ConnectAsync("127.0.0.1", 11434).Wait(1500)) { Skip = "Ollama not listening on 127.0.0.1:11434 — LOCAL MODEL UNAVAILABLE"; return; }
            new LocalModelClient("http://127.0.0.1:11434", Model).DigestAsync().GetAwaiter().GetResult();
        }
        catch (Exception ex) { Skip = $"Local model {Model} unavailable ({ex.GetBaseException().Message}) — LOCAL MODEL UNAVAILABLE"; }
    }
}

[Collection("AppMode")]
public sealed class EvidenceReasonerTests
{
    private static readonly IReadOnlyList<CatalogItem> Catalog =
    [
        new("F-0001", "finding", "DEF-DETECTION Microsoft Defender a detectat Trojan:Script/Wacatac.H!ml în 0000D0885B718B00.dll la 2026-09-19.", [new EvidenceRef("EV-1", "EventRecordID=10", "1116", new string('A', 64))]),
        new("E1", "event", "BrowserDownload: SamFw_302044.zip de pe https://mailjilq.tzd4is.cyou/ la 2026-09-19T14:52:42Z", [new EvidenceRef("EV-2", "downloads/1", "d", new string('B', 64))]),
        new("E2", "event", "Prefetch: SETUP.EXE a rulat de 4 ori, ultima 2026-09-19T14:56:26Z", [new EvidenceRef("EV-3", "SETUP.EXE-C0F23475.pf", "p", new string('C', 64))]),
        new("G1", "gap", "Gol de probă Security 4688 (PARTIAL): crearea proceselor nu este auditată.", []),
    ];

    private static string Json(string section, string text, params string[] cites) =>
        "{" + string.Join(",", EvidenceReasoner.Sections.Select(s => $"\"{s}\":" + (s == section
            ? $"[{{\"text\":{System.Text.Json.JsonSerializer.Serialize(text)},\"cites\":[{string.Join(",", cites.Select(c => $"\"{c}\""))}]}}]" : "[]"))) + "}";

    private static RejectedStatement Rejected(string section, string text, params string[] cites)
    {
        var (a, r) = EvidenceReasoner.Validate(Json(section, text, cites), Catalog);
        Assert.Empty(a);
        return Assert.Single(r);
    }

    [Fact]
    public void A_statement_must_cite_existing_items_and_not_too_many()
    {
        var (ok, _) = EvidenceReasoner.Validate(Json("summary", "SETUP.EXE a rulat de 4 ori.", "E2"), Catalog);
        var s = Assert.Single(ok);
        Assert.Equal(("UNPROVEN", "EV-3", new string('C', 64)), (s.Classification, s.Citations[0].Evidence[0].EvidenceId, s.Citations[0].Evidence[0].Sha256));
        Assert.Contains("nu citează", Rejected("summary", "Ceva s-a întâmplat.").Reason);
        Assert.Contains("inexistente: F-9", Rejected("summary", "Ceva s-a întâmplat.", "F-9").Reason);
        Assert.Contains("inexistente: F-0001x", Rejected("summary", "Ceva.", "F-0001", "E1", "E2", "G1", "F-0001x").Reason);   // an unknown citation is reported first
        var many = new List<CatalogItem>(Catalog) { new("E3", "event", "x", []), new("E4", "event", "y", []) };
        var (_, r) = EvidenceReasoner.Validate(Json("summary", "Ceva.", "F-0001", "E1", "E2", "E3", "E4"), many);
        Assert.Contains("maximum 4", Assert.Single(r).Reason);
    }

    [Fact]
    public void Files_addresses_domains_and_dates_must_be_in_the_cited_items()
    {
        // The real failure seen with a 7B model: linking NanAgent32.exe to SETUP.EXE while citing only the Prefetch item.
        Assert.Contains("NanAgent32.exe", Rejected("correlation_explanation", "NanAgent32.exe a fost asociat cu rularea SETUP.EXE.", "E2").Reason);
        Assert.Contains("185.220.101.5", Rejected("summary", "SETUP.EXE a contactat 185.220.101.5.", "E2").Reason);
        Assert.Contains("evil.com", Rejected("summary", "Descărcarea a venit de pe evil.com.", "E1").Reason);
        Assert.Contains("2026-09-18", Rejected("summary", "SETUP.EXE a rulat pe 2026-09-18.", "E2").Reason);
        var (ok, _) = EvidenceReasoner.Validate(Json("summary", "Arhiva SamFw_302044.zip a fost descărcată de pe mailjilq.tzd4is.cyou pe 2026-09-19.", "E1"), Catalog);
        Assert.Single(ok);
    }

    [Fact]
    public void Hypotheses_must_be_worded_as_possibilities()
    {
        Assert.Contains("ca sigură", Rejected("hypotheses", "Arhiva a instalat troianul, confirmat de Defender.", "E1", "F-0001").Reason);
        Assert.Contains("nu este formulată ca posibilitate", Rejected("alternative_explanations", "Detecția este o eroare a Defender.", "F-0001").Reason);
        var (ok, _) = EvidenceReasoner.Validate(Json("hypotheses", "Arhiva descărcată poate fi sursa fișierului detectat.", "E1", "F-0001"), Catalog);
        Assert.Single(ok);
    }

    [Fact]
    public void Broken_or_incomplete_answers_are_reported_not_hidden()
    {
        var (a, r) = EvidenceReasoner.Validate("nu e JSON", Catalog);
        Assert.Empty(a);
        Assert.Contains("nu este JSON valid", Assert.Single(r).Reason);
        var (_, r2) = EvidenceReasoner.Validate("{\"summary\":[]}", Catalog);
        Assert.Equal(5, r2.Count(x => x.Reason == "secțiunea lipsește din răspuns"));
    }

    [Fact]
    public void Catalog_puts_findings_first_with_their_events_and_counts_what_it_leaves_out()
    {
        var ev = new TimelineEvent { Time = Timestamp.FromUtc(new DateTime(2026, 9, 19, 14, 56, 26, DateTimeKind.Utc), "t", "x"), Source = "Prefetch", EvidenceId = "EV-3",
                                     Locator = "L1", Summary = "SETUP.EXE", Path = @"C:\Users\a\Downloads\SETUP.EXE" };
        Finding F(string id, Severity s) => new() { FindingId = id, RuleId = "R", Title = id, Description = "d", Severity = s, SupportingEvidence = [new EvidenceRef("EV-3", "L1", "x")] };
        var af = new AntiForensicCheck("AF01", "golit", "T1070.001", AntiForensicResult.Detected, "3 goliri", [new EvidenceRef("EV-1", "EventRecordID=5", "104")]);
        var gap = new EvidenceGap("Prefetch", EvidenceStatus.NotAvailable, "admin", "fără rulări", "SRUM", "x");
        var (items, omitted) = EvidenceReasoner.BuildCatalog([F("F-1", Severity.Low), F("F-2", Severity.Critical)], [ev], [af], [gap]);
        Assert.Equal(["F-2", "E1", "F-1", "AF01", "G1"], items.Select(i => i.Id));   // the event is listed once, after the first finding citing it
        Assert.Equal(0, omitted);
        Assert.Contains(@"C:\Users\a\Downloads\SETUP.EXE", items[1].Text);
        var (few, left) = EvidenceReasoner.BuildCatalog([F("F-1", Severity.Low), F("F-2", Severity.Critical)], [ev], [af], [gap], maxItems: 2);
        Assert.Equal((2, 3), (few.Count, left));
    }

    [Theory]
    [InlineData("http://10.0.0.5:11434")]
    [InlineData("http://localhost:11434")]
    [InlineData("https://127.0.0.1:11434")]
    [InlineData("http://ollama.example.com")]
    public void The_model_is_called_only_on_a_numeric_loopback_address(string endpoint) =>
        Assert.Throws<ArgumentException>(() => new LocalModelClient(endpoint, "m"));

    [Fact]
    public void Loopback_addresses_are_accepted()
    {
        Assert.Equal("127.0.0.1", new LocalModelClient("http://127.0.0.1:11434", "m").Endpoint.Host);
        Assert.Equal("[::1]", new LocalModelClient("http://[::1]:11434", "m").Endpoint.Host);
    }

    /// <summary>The real local model, with the station in AirGapped mode: loopback only, every kept statement passes the rules again.</summary>
    [OllamaFact]
    public async Task Local_model_answer_is_validated_and_recorded()
    {
        AppModeContext.ResetForTests();
        AppModeContext.Initialize(new ModeDecision(AppMode.AirGapped, true, "test", ConnectivitySnapshot.Unknown("test")));
        try
        {
            var client = new LocalModelClient("http://127.0.0.1:11434", OllamaFactAttribute.Model);
            var ai = await client.ReasonAsync(Catalog, 0, 8192);
            Assert.Equal(64, ai.ResponseSha256.Length);
            Assert.Equal(EvidenceReasoner.Sha(ai.RawResponse), ai.ResponseSha256);
            Assert.Matches("^[0-9a-f]{64}$", ai.ModelDigest);
            Assert.NotEmpty(ai.Accepted);
            var (again, _) = EvidenceReasoner.Validate(ai.RawResponse, Catalog);
            Assert.Equal(ai.Accepted.Select(a => a.Text), again.Select(a => a.Text));
            Assert.All(ai.Accepted, a => Assert.All(a.Citations, c => Assert.Contains(Catalog, i => i.Id == c.Item)));
            Assert.True(AppModeContext.IsAirGapped);
        }
        finally { AppModeContext.ResetForTests(); }
    }
}
