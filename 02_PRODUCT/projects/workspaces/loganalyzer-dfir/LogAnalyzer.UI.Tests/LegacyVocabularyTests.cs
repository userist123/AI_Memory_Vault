using LogAnalyzer.Core.Models;
using LogAnalyzer.Dfir.Analysis;
using LogAnalyzer.Dfir.Model;
using Xunit;

namespace LogAnalyzer.UI.Tests;

/// <summary>The legacy EvidenceStrength vocabulary (unwired legacy code) is fully covered by the conservative mapping onto the standard vocabulary.</summary>
public sealed class LegacyVocabularyTests
{
    [Fact]
    public void Every_legacy_evidence_strength_value_has_an_explicit_mapping()
    {
        Assert.Equal(Enum.GetNames<EvidenceStrength>().Order(), LegacyVocabulary.KnownStrengths.Order());
        foreach (var name in Enum.GetNames<EvidenceStrength>())
        {
            var (type, state) = LegacyVocabulary.FromEvidenceStrength(name, "Amcache");
            Assert.NotEqual(StandardState.NotAssessed, state);   // an unmapped value would fall through to NOT_ASSESSED
            Assert.NotEqual(StandardState.Verified, state);
            Assert.NotEqual(SemanticType.Execution, type);        // Amcache presence is never execution, whatever the legacy label said
        }
    }

    [Fact]
    public void Legacy_ExecutionProven_is_execution_only_for_artifacts_that_record_a_run()
    {
        Assert.Equal(SemanticType.Execution, LegacyVocabulary.FromEvidenceStrength(nameof(EvidenceStrength.ExecutionProven), "Prefetch").Type);
        Assert.Equal(SemanticType.Presence, LegacyVocabulary.FromEvidenceStrength(nameof(EvidenceStrength.ExecutionProven), "Amcache").Type);
    }
}
