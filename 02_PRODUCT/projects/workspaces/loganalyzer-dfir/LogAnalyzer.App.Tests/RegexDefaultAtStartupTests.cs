using System.Reflection;
using System.Reflection.Emit;
using LogAnalyzer.Dfir;
using Xunit;

namespace LogAnalyzer.App.Tests;

/// <summary>
/// Security audit #11: .NET reads REGEX_DEFAULT_MATCH_TIMEOUT once, when the first regex of the process is built. The application's static
/// constructor runs before WPF creates anything; its first call must be <see cref="RegexDefaults.Apply"/>, otherwise the default never takes effect.
/// </summary>
public sealed class RegexDefaultAtStartupTests
{
    [Fact]
    public void App_static_constructor_applies_the_regex_default_before_anything_else()
    {
        var cctor = typeof(LogAnalyzer.UI.App).TypeInitializer;
        Assert.NotNull(cctor);
        var calls = LeadingCalls(cctor!.Module, cctor.GetMethodBody()!.GetILAsByteArray()!);
        var apply = calls.FindIndex(m => m.DeclaringType == typeof(RegexDefaults) && m.Name == nameof(RegexDefaults.Apply));
        Assert.True(apply >= 0, "RegexDefaults.Apply is not among the first calls: " + string.Join(", ", calls.Select(c => c.DeclaringType?.Name + "." + c.Name)));
        // Only static field initialisers that never build a regex may run before it.
        Assert.All(calls.Take(apply), m => Assert.Equal(typeof(System.Diagnostics.Stopwatch), m.DeclaringType));
    }

    /// <summary>The methods called at the start of <paramref name="il"/>, up to the first instruction this small reader does not know.</summary>
    private static List<MethodBase> LeadingCalls(Module module, byte[] il)
    {
        var calls = new List<MethodBase>();
        for (int i = 0; i < il.Length;)
        {
            var op = il[i];
            if (op == OpCodes.Nop.Value) { i += 1; continue; }
            if (op == (byte)OpCodes.Call.Value) { calls.Add(module.ResolveMethod(BitConverter.ToInt32(il, i + 1))!); i += 5; continue; }
            if (op == (byte)OpCodes.Stsfld.Value) { i += 5; continue; }
            break;
        }
        return calls;
    }
}
