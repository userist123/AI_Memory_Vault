using System.Text.Json.Nodes;
using LogAnalyzer.Dfir.AI;
using LogAnalyzer.Dfir.Model;
using LogAnalyzer.Dfir.Windows.Investigation;
using LogAnalyzer.Verification;
using Xunit;

namespace LogAnalyzer.Dfir.Tests;

/// <summary>
/// WP4 final review round: (1) VALORI_CONFIRMATE only when ONE cited record holds every value of the claim; (2) look-alike characters (homoglyphs) make a
/// claim NESUSTINUT, the text is NFKC-normalised before extraction; (5) a time of day ("la ora 11") is compared with the cited record's hour. Each probe
/// fails on the code before the fix. Fakes only: no model, no network.
/// </summary>
public sealed class AiClaimValidationFinalTests : IDisposable
{
    private readonly AiCaseFixture _c = new();
    private readonly string _otherEvent;

    public AiClaimValidationFinalTests()
    {
        // a second event of the same file: svchost.exe talking to 10.9.9.9:443 one minute later
        _c.Lab.Event("Security", "a", "rec-2", AiCaseFixture.T.AddMinutes(1), eventId: "5156", summary: "svchost.exe a contactat 10.9.9.9:443", process: "svchost.exe");
        _otherEvent = $"{_c.EvidenceId}#rec-2";
    }

    public void Dispose() => _c.Dispose();

    // ---------------------------------------------------------------- (1) values from several cited records

    [Fact]
    public void Values_gathered_from_two_cited_events_are_at_most_PARTIAL_with_the_reason()
    {
        var v = _c.Validate("Procesul evil.exe a contactat 10.9.9.9 pe portul 443.", _c.EventKey, _otherEvent);   // evil.exe is in rec-1, the address and port in rec-2
        Assert.Equal(ClaimState.Partial, v.State);
        Assert.Contains("valorile provin din înregistrări diferite", v.Reason);
        Assert.All(v.Facts, f => Assert.True(f.Matched, f.Kind + " " + f.Value));   // every value exists somewhere in the cited set, just not in one record
    }

    [Fact]
    public void A_timestamp_from_another_cited_event_is_at_most_PARTIAL()
    {
        var v = _c.Validate("Procesul evil.exe a rulat la 2025-03-01T10:01:05Z.", _c.EventKey, _otherEvent);   // 10:01:05 is rec-2's time, evil.exe ran in rec-1 at 10:00:05
        Assert.Equal(ClaimState.Partial, v.State);
        Assert.Contains("valorile provin din înregistrări diferite", v.Reason);
    }

    [Fact]
    public void All_values_in_one_of_the_cited_records_is_VALORI_CONFIRMATE()
    {
        var v = _c.Validate($"Procesul evil.exe a rulat din {AiCaseFixture.ExePath} la 2025-03-01T10:00:05Z de la 10.1.2.3.", _c.EventKey, _otherEvent);
        Assert.Equal(ClaimState.ValoriConfirmate, v.State);
        Assert.Equal(ClaimStates.Caveat, v.Notice);
        // a count of the cited set is still recomputed over the whole set
        Assert.Equal(ClaimState.ValoriConfirmate, _c.Validate("Procesul evil.exe apare în 2 evenimente.", _c.EventKey, _otherEvent).State);
        Assert.Equal(ClaimState.ValoriConfirmate, _c.Validate("Conexiune svchost.exe la 10.9.9.9 pe portul 443.", _c.EventKey, _otherEvent).State);
    }

    // ---------------------------------------------------------------- (2) look-alike characters

    [Fact]
    public void A_hash_with_one_Cyrillic_letter_is_NESUSTINUT_with_the_look_alike_reason()
    {
        var spoof = "d2" + "\u0430" + "84f4b8b650937ec8f73cd8be2c74add5a911ba64df27458ed8229da804a26";   // 64 characters, the third one is Cyrillic "а"
        Assert.Equal(64, spoof.Length);
        var v = _c.Validate($"Procesul evil.exe a rulat din {AiCaseFixture.ExePath}, hash {spoof}.", "F-0001");
        Assert.Equal(ClaimState.Nesustinut, v.State);
        Assert.StartsWith("conține caractere care imită alte litere", v.Reason);
        Assert.Contains("U+0430", v.Reason);
    }

    [Fact]
    public void A_file_name_with_Cyrillic_e_next_to_a_correct_hash_is_NESUSTINUT()
    {
        Assert.Equal(ClaimState.ValoriConfirmate, _c.Validate($"Procesul evil.exe a rulat din {AiCaseFixture.ExePath}, hash {_c.EvidenceSha}.", "F-0001").State);   // control
        var v = _c.Validate($"Procesul evil.\u0435\u0445\u0435 a rulat din {AiCaseFixture.ExePath}, hash {_c.EvidenceSha}.", "F-0001");   // "evil.ехе"
        Assert.Equal(ClaimState.Nesustinut, v.State);
        Assert.StartsWith("conține caractere care imită alte litere", v.Reason);
    }

    [Theory]
    [InlineData("C:\\Users\\bob\\AppData\\Local\\Temp\\evil.\u0435x\u0435")]
    [InlineData("C:\\Users\\b\u043Eb\\AppData\\Local\\Temp\\evil.exe")]
    [InlineData("\u0421:\\Users\\bob\\AppData\\Local\\Temp\\evil.exe")]
    public void A_path_with_a_look_alike_letter_stays_NESUSTINUT(string path)
    {
        var v = _c.Validate($"Procesul a rulat din {path}.", "F-0001");
        Assert.Equal(ClaimState.Nesustinut, v.State);
        Assert.StartsWith("conține caractere care imită alte litere", v.Reason);
    }

    [Theory]
    [InlineData("Procesul evil.exe a rulat din {0} în ședința de lucru; câteva țări, băieți și mâini.")]
    [InlineData("ĂÂÎȘȚ: procesul evil.exe a rulat din {0} (forme vechi: ş ţ Ş Ţ).")]
    [InlineData("Procesul evil.exe a rulat din {0} în stația „STATIE-1”, în aceeași zi.")]
    public void Plain_Romanian_diacritics_are_not_flagged(string template)
    {
        var v = _c.Validate(string.Format(template, AiCaseFixture.ExePath), "F-0001");
        Assert.DoesNotContain("imită", v.Reason);
        Assert.Equal(ClaimState.ValoriConfirmate, v.State);
    }

    [Fact]
    public void The_claim_is_NFKC_normalised_before_extraction()
    {
        // full-width "ｅｖｉｌ" is the compatibility form of "evil": after NFKC it is the plain name, compared and confirmed, not refused as a look-alike
        var v = _c.Validate($"Procesul \uFF45\uFF56\uFF49\uFF4C.exe a rulat din {AiCaseFixture.ExePath}.", "F-0001");
        Assert.Equal(ClaimState.ValoriConfirmate, v.State);
        Assert.Contains(v.Facts, f => f.Kind == "nume de fișier" && f.Value == "evil.exe" && f.Matched);
        // and a full-width wrong name is compared as the plain wrong name
        Assert.Equal(ClaimState.Nesustinut, _c.Validate($"Procesul \uFF4D\uFF41\uFF4C.exe a rulat din {AiCaseFixture.ExePath}.", "F-0001").State);
    }

    [Fact]
    public void An_invisible_character_inside_a_name_is_NESUSTINUT()
    {
        var v = _c.Validate($"Procesul evil\u200B.exe a rulat din {AiCaseFixture.ExePath}.", "F-0001");
        Assert.Equal(ClaimState.Nesustinut, v.State);
        Assert.StartsWith("conține caractere care imită alte litere", v.Reason);
    }

    // ---------------------------------------------------------------- (5) time of day

    [Theory]
    [InlineData("Procesul evil.exe a rulat la ora 11.", ClaimState.Nesustinut)]
    [InlineData("Procesul evil.exe a rulat la ora 10:30.", ClaimState.Nesustinut)]
    [InlineData("Procesul evil.exe a rulat la ora 25.", ClaimState.Nesustinut)]
    [InlineData("Procesul evil.exe a rulat la ora 10.", ClaimState.ValoriConfirmate)]
    [InlineData("Procesul evil.exe a rulat la ora 10:00.", ClaimState.ValoriConfirmate)]
    [InlineData("Procesul evil.exe a rulat, ora 10:00, din Temp.", ClaimState.ValoriConfirmate)]
    public void A_time_of_day_is_compared_with_the_hour_of_the_cited_record(string text, ClaimState expected)
    {
        var v = _c.Validate(text, _c.EventKey);   // the event is at 10:00:05 UTC
        Assert.True(v.State == expected, $"{text}: {v.State} {v.Reason}");
        Assert.Contains(v.Facts, f => f.Kind == "oră");
        if (expected == ClaimState.Nesustinut) Assert.Contains("10:00 UTC", v.Reason);
    }
}

/// <summary>WP4 final review round, item 4: verification.json never copies an unknown claim state from ai_reasoning.json.</summary>
public sealed class AiClaimValidationFinalReportTests : IDisposable
{
    private readonly AiCaseFixture _c = new();
    public void Dispose() => _c.Dispose();

    [Theory]
    [InlineData("SUSTINUT", "NEVERIFICABIL")]
    [InlineData("TOTUL_BINE", "NEVERIFICABIL")]
    [InlineData("valori_confirmate", "VALORI_CONFIRMATE")]
    [InlineData("partial", "PARTIAL")]
    public void A_claim_state_in_ai_reasoning_is_normalised_to_the_four_known_states(string stored, string expected)
    {
        var r = new InvestigationResult { Case = _c.Lab.Ws };
        r.Findings.AddRange(_c.Lab.Findings);
        r.Timeline.AddRange(_c.Lab.Events);
        var model = new AiReasoning("fake-model", "digest0123456789ab", "http://127.0.0.1:11434", "cat", "prompt", "resp", DateTimeOffset.UtcNow,
            [new AiStatement("summary", $"Procesul evil.exe a rulat din {AiCaseFixture.ExePath} la 2025-03-01T10:00:05Z.", [new Citation("F-0001", "finding", [])])], [], 1, 0, "{\"raw\":true}");
        AiCaseAnalysis.ValidateAndStore(r, model, [new CatalogItem("F-0001", "finding", "Proces suspect", _c.Finding.SupportingEvidence)]);

        var file = Path.Combine(_c.Lab.Ws.Root, "Analysis", "ai_reasoning.json");
        var root = JsonNode.Parse(File.ReadAllText(file))!.AsObject();
        int changed = 0;
        foreach (var st in Prop(root, "accepted")!.AsArray())
            if (Prop(st!.AsObject(), "validation") is JsonObject val && val.FirstOrDefault(p => p.Key.Equals("state", StringComparison.OrdinalIgnoreCase)) is { Key: { } key })
            { val[key] = stored; changed++; }
        Assert.Equal(1, changed);
        File.SetAttributes(file, FileAttributes.Normal);
        File.WriteAllText(file, root.ToJsonString());

        var s = CaseVerifier.Evaluate(_c.Lab.Ws, new VerificationOptions()).AiStatements.Single();
        Assert.Equal(expected, s.ClaimValidation);
    }

    private static JsonNode? Prop(JsonObject o, string name) => o.FirstOrDefault(p => p.Key.Equals(name, StringComparison.OrdinalIgnoreCase)).Value;
}
