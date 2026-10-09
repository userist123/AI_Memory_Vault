using System.ComponentModel;
using System.Text.RegularExpressions;
using LogAnalyzer.Dfir.Analysis;
using LogAnalyzer.Dfir.Language;
using LogAnalyzer.Dfir.Model;
using LogAnalyzer.Dfir.Presentation;
using Xunit;

namespace LogAnalyzer.Dfir.Tests;

/// <summary>Tests that change the application-wide language run alone (the setting is process-wide); everything else uses <see cref="Loc.Use"/>, which is per async flow.</summary>
[CollectionDefinition("LocGlobal", DisableParallelization = true)]
public sealed class LocGlobalCollection;

/// <summary>WP6b / U17: the resource layer (Romanian default, English supported, no raw key, identifiers untouched).</summary>
public class Wp6bLocTests
{
    [Fact]
    public void Every_romanian_key_has_an_english_text_or_is_declared_romanian_only_with_a_reason()
    {
        var en = Loc.Keys(AppLanguage.English).ToHashSet();
        var roOnly = Loc.RomanianOnly;
        var missing = Loc.Keys(AppLanguage.Romanian).Where(k => !en.Contains(k) && !roOnly.ContainsKey(k)).ToList();
        Assert.True(missing.Count == 0, "Keys without an English text and not declared RO-only:\n" + string.Join("\n", missing));
        foreach (var (k, reason) in roOnly) Assert.False(string.IsNullOrWhiteSpace(reason), $"RO-only key '{k}' needs a reason");
        Assert.DoesNotContain(roOnly.Keys, en.Contains);   // a stale exception hides a missing translation later
    }

    [Fact]
    public void Every_english_key_has_a_romanian_text_and_no_text_is_empty()
    {
        foreach (var k in Loc.Keys(AppLanguage.English)) Assert.True(Loc.Has(k, AppLanguage.Romanian), $"English-only key '{k}'");
        foreach (var l in new[] { AppLanguage.Romanian, AppLanguage.English })
            foreach (var k in Loc.Keys(l).Where(k => !k.StartsWith("fmt.", StringComparison.Ordinal))) Assert.False(string.IsNullOrWhiteSpace(Loc.T(k, l)), $"{l}: '{k}' is empty");   // fmt.* keys are connectors and may be blank
    }

    [Fact]
    public void The_two_languages_use_the_same_placeholders()
    {
        static string Ph(string s) => string.Join(",", Regex.Matches(s, @"\{\d+\}").Select(m => m.Value).Distinct().OrderBy(x => x));
        foreach (var k in Loc.Keys(AppLanguage.English).Where(k => Loc.Has(k, AppLanguage.Romanian)))
            Assert.True(Ph(Loc.T(k, AppLanguage.Romanian)) == Ph(Loc.T(k, AppLanguage.English)), $"placeholders differ for '{k}'");
    }

    [Fact]
    public void English_differs_from_romanian_for_the_state_labels_and_an_unknown_english_text_falls_back_to_romanian_not_to_the_key()
    {
        Assert.Equal("Verificat", Loc.T("state.verified", AppLanguage.Romanian));
        Assert.Equal("Verified", Loc.T("state.verified", AppLanguage.English));
        // a key present only in Romanian (declared or not) never comes back as the raw key
        var roOnly = Loc.Keys(AppLanguage.Romanian).Except(Loc.Keys(AppLanguage.English)).FirstOrDefault();
        if (roOnly is not null) Assert.Equal(Loc.T(roOnly, AppLanguage.Romanian), Loc.T(roOnly, AppLanguage.English));
        Assert.Equal("no.such.key", Loc.T("no.such.key"));   // unknown keys are visible in tests, never silent
    }

    [Fact]
    public void Scoped_language_applies_to_this_flow_only_and_restores()
    {
        Assert.Equal(AppLanguage.Romanian, Loc.Current);
        using (Loc.Use(AppLanguage.English))
        {
            Assert.Equal("Verified", StateLabels.Label(StandardState.Verified));
            Assert.Equal("Windows logs", Glossary.Human("evtx"));
            Assert.Equal("Critical", SeverityLabels.Text(Severity.Critical));
            Assert.Equal("Completed", StateLabels.Operation(OperationState.Completed));
        }
        Assert.Equal("Verificat", StateLabels.Label(StandardState.Verified));
        Assert.Equal(AppLanguage.Romanian, Loc.Current);
    }

    [Fact]
    public void Romanian_pinned_tables_stay_romanian_in_an_english_session()
    {
        using var _ = Loc.Use(AppLanguage.English);
        Assert.Equal("Verificat", StateLabels.Romanian(StandardState.Verified));
        Assert.Equal("Verificat", StateLabels.RomanianTable[StandardState.Verified]);
        Assert.Equal("Finalizat", StateLabels.RomanianOperation[OperationState.Completed]);
    }

    [Fact]
    public void Identifiers_are_never_translated_by_the_glossary()
    {
        using var _ = Loc.Use(AppLanguage.English);
        Assert.Equal("EVTX (Windows logs)", Glossary.Display("evtx", advanced: true));
        Assert.StartsWith("EVTX — Windows logs.", Glossary.Tooltip("evtx"));
        Assert.Equal("Security.evtx", Glossary.Human("Security.evtx"));   // not a glossary term: unchanged
        Assert.Equal("T1059.001", Glossary.Human("T1059.001"));
    }

    [Fact]
    public void Every_term_state_severity_and_operation_label_exists_in_both_languages()
    {
        foreach (var l in new[] { AppLanguage.Romanian, AppLanguage.English })
        {
            foreach (var s in Enum.GetValues<StandardState>()) { Assert.True(Loc.Has("state." + s.ToSpec().ToLowerInvariant(), l)); Assert.True(Loc.Has("state." + s.ToSpec().ToLowerInvariant() + ".meaning", l)); }
            foreach (var s in Enum.GetValues<OperationState>()) Assert.True(Loc.Has("op." + s.ToSpec().ToLowerInvariant(), l));
            foreach (var s in Enum.GetValues<Severity>()) Assert.True(Loc.Has("sev." + s.ToString().ToLowerInvariant(), l));
            foreach (var t in Glossary.Terms) { Assert.True(Loc.Has($"term.{t.Key}", l)); Assert.True(Loc.Has($"term.{t.Key}.explain", l)); }
        }
    }

    [Fact]
    public void User_settings_default_to_romanian_and_keep_the_choice()
    {
        var file = Path.Combine(Path.GetTempPath(), "wp6b-" + Guid.NewGuid().ToString("N"), "settings.json");
        try
        {
            var s = new UserSettings(file);
            Assert.Equal(AppLanguage.Romanian, s.LoadLanguage());                       // no file: Romanian
            Assert.True(s.SaveLanguage(AppLanguage.English));
            Assert.Equal(AppLanguage.English, new UserSettings(file).LoadLanguage());   // survives a restart
            Assert.True(s.SaveLanguage(AppLanguage.Romanian));
            Assert.Equal(AppLanguage.Romanian, new UserSettings(file).LoadLanguage());
            File.WriteAllText(file, "{ not json");
            Assert.Equal(AppLanguage.Romanian, s.LoadLanguage());                       // unreadable: Romanian, no exception
            File.WriteAllText(file, "{\"language\":\"fr\"}");
            Assert.Equal(AppLanguage.Romanian, s.LoadLanguage());                       // unknown: Romanian
        }
        finally { try { Directory.Delete(Path.GetDirectoryName(file)!, true); } catch (IOException) { } }
    }
}

[Collection("LocGlobal")]
public class Wp6bLocSwitchTests
{
    [Fact]
    public void Switching_the_language_updates_what_a_binding_to_the_source_reads_without_a_restart()
    {
        var raised = new List<string?>();
        PropertyChangedEventHandler h = (_, e) => raised.Add(e.PropertyName);
        LocSource.Instance.PropertyChanged += h;
        try
        {
            Assert.Equal("Verificat", LocSource.Instance["state.verified"]);
            Loc.SetLanguage(AppLanguage.English);
            Assert.Contains("Item[]", raised);
            Assert.Equal("Verified", LocSource.Instance["state.verified"]);
            Assert.Equal("Windows logs", LocSource.Instance["glossary.evtx"]);
            Assert.StartsWith("EVTX — Windows logs.", LocSource.Instance["glossary.evtx.tip"]);
            Assert.Equal("EVTX (Windows logs)", LocSource.Instance["glossary.evtx.adv"]);
        }
        finally { Loc.SetLanguage(AppLanguage.Romanian); LocSource.Instance.PropertyChanged -= h; }
        Assert.Equal("Verificat", LocSource.Instance["state.verified"]);
        Assert.Equal("Jurnale Windows", LocSource.Instance["glossary.evtx"]);
    }
}
