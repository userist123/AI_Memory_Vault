using System.Reflection;
using LogAnalyzer.Core.Services.Connectivity;
using LogAnalyzer.Dfir.Windows.Investigation;
using Xunit;

namespace LogAnalyzer.Dfir.Tests;

/// <summary>Owner decision 14: collection and interpretation are complete and deterministic without any AI; AI is an optional explanation layer.</summary>
public sealed class AiIndependenceTests
{
    private static readonly string[] SharedAssemblies = ["LogAnalyzer.Core", "LogAnalyzer.Dfir.Core", "LogAnalyzer.Dfir.Windows"];

    [Fact]
    public void No_shared_assembly_references_the_ai_layer()
    {
        foreach (var name in SharedAssemblies)
        {
            var asm = Assembly.Load(name);
            Assert.DoesNotContain(asm.GetReferencedAssemblies(), r => r.Name == "LogAnalyzer.Ai");
        }
    }

    [Fact]
    public void Findings_are_identical_with_the_ai_layer_present_but_unused()
    {
        var withoutAi = SelfTest.RunPipelineOnce();
        Assert.True(withoutAi.Timeline > 0);

        // The AI layer is loaded and initialised (types resolved), but never called.
        var aiAssembly = typeof(LogAnalyzer.Dfir.AI.EvidenceReasoner).Assembly;
        Assert.Equal("LogAnalyzer.Ai", aiAssembly.GetName().Name);
        _ = aiAssembly.GetTypes();
        AppModeContext.ResetForTests();
        AppModeContext.Initialize(new ModeDecision(AppMode.AirGapped, true, "test", ConnectivitySnapshot.Unknown("test")));
        try
        {
            var withAi = SelfTest.RunPipelineOnce();
            Assert.Equal(withoutAi, withAi);
        }
        finally { AppModeContext.ResetForTests(); }
    }

    [Fact]
    public void Self_test_steps_that_need_no_windows_all_pass_here()
    {
        var steps = SelfTest.Run(AppContext.BaseDirectory);
        foreach (var s in steps) Assert.True(s.Ok, $"{s.Name}: {s.Detail}");
        Assert.Contains(steps, s => s.Name == "sqlcipher");
        Assert.Contains(steps, s => s.Name == "pdf");
    }
}
