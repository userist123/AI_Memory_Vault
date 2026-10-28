using System.Collections.ObjectModel;
using System.Reflection;
using System.Reflection.Emit;
using LogAnalyzer.Dfir.AI;
using LogAnalyzer.UI.ViewModels;
using Xunit;

namespace LogAnalyzer.App.Tests;

/// <summary>
/// R22.5, WP4, screen side: the AI screen binds validated rows, never the raw model statements. A statement without a validation is shown NEVERIFICABIL
/// with a warning; VALORI_CONFIRMATE always carries the model-statement caveat; NESUSTINUT is struck. The rest of the output registry is in
/// LogAnalyzer.Dfir.Tests (AiClaimValidationReportTests).
/// </summary>
public sealed class AiClaimValidationUiTests
{
    private static AiStatement Statement(ValidatedClaim? v) => new("summary", "Procesul evil.exe a rulat.", [new Citation("F-0001", "finding", [])]) { Validation = v };

    [Fact]
    public void The_AI_screen_collections_hold_validated_rows_not_raw_model_statements()
    {
        var p = typeof(AiAnalysisViewModel).GetProperty(nameof(AiAnalysisViewModel.AiStatements))!;
        Assert.Equal(typeof(ObservableCollection<AiClaimRow>), p.PropertyType);
        Assert.DoesNotContain(typeof(AiAnalysisViewModel).GetProperties(), x => x.PropertyType.IsGenericType && x.PropertyType.GetGenericArguments().Contains(typeof(AiStatement)));
        Assert.DoesNotContain(typeof(AiAnalysisViewModel).GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public),
            x => x.FieldType.IsGenericType && x.FieldType.GetGenericArguments().Contains(typeof(AiStatement)));
    }

    [Fact]
    public void A_statement_without_validation_is_shown_NEVERIFICABIL_with_a_warning()
    {
        var row = AiClaimRow.From(Statement(null));
        Assert.StartsWith("NEVERIFICABIL", row.State);
        Assert.NotEmpty(row.Warning);
        Assert.False(row.IsStruck);
    }

    [Theory]
    [InlineData(ClaimState.ValoriConfirmate, false)]
    [InlineData(ClaimState.Partial, false)]
    [InlineData(ClaimState.Neverificabil, false)]
    [InlineData(ClaimState.Nesustinut, true)]
    public void Every_state_is_shown_with_a_caveat_or_warning_and_NESUSTINUT_is_struck(ClaimState state, bool struck)
    {
        var row = AiClaimRow.From(Statement(new ValidatedClaim("summary", "t", ["F-0001"], state, state == ClaimState.ValoriConfirmate ? "" : "motivul", [])));
        Assert.Equal(state.ToDisplay(), row.State);
        Assert.NotEmpty(row.Warning);
        Assert.Equal(struck, row.IsStruck);
        Assert.StartsWith("UNPROVEN", row.Classification);   // a model statement, whatever the validation says
        if (state == ClaimState.ValoriConfirmate)
        {
            Assert.Equal("valori confirmate", row.State);
            Assert.Equal("afirmație a modelului, nu constatare; s-au confirmat doar valorile citate, nu sensul", row.Warning);
        }
        else Assert.Contains("motivul", row.Warning);
    }

    // ---------------------------------------------------------------- no App type reads raw AI text except the validated screen

    private static readonly string[] RawAiTypes =
        ["LogAnalyzer.Dfir.AI.AiStatement", "LogAnalyzer.Dfir.AI.AiReasoning", "LogAnalyzer.Dfir.AI.RejectedStatement", "LogAnalyzer.Verification.AiStatementVerdict"];

    /// <summary>The only App types that may touch the raw AI types: the AI screen (it binds validated rows and takes its statements from the validating gate) and its row.</summary>
    private static readonly string[] AllowedInApp = ["LogAnalyzer.UI.ViewModels.AiAnalysisViewModel", "LogAnalyzer.UI.ViewModels.AiClaimRow"];

    private static readonly Dictionary<short, OpCode> Ops =
        typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static).Select(f => (OpCode)f.GetValue(null)!).ToDictionary(o => o.Value);

    private static bool Mentions(Type? t) =>
        t is not null && (RawAiTypes.Contains(t.FullName ?? "") || Mentions(t.HasElementType ? t.GetElementType() : null) || (t.IsGenericType && t.GetGenericArguments().Any(Mentions)));

    private const BindingFlags AllMembers = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

    private static IEnumerable<Type> WithNested(Type t)
    {
        yield return t;
        foreach (var n in t.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic)) foreach (var x in WithNested(n)) yield return x;
    }

    private static IEnumerable<MemberInfo?> IlMembers(MethodBase m)
    {
        byte[]? il;
        try { il = m.GetMethodBody()?.GetILAsByteArray(); } catch (Exception) { yield break; }
        if (il is null) yield break;
        Type[]? ta = m.DeclaringType is { IsGenericType: true } dt ? dt.GetGenericArguments() : null;
        Type[]? ma = m is MethodInfo { IsGenericMethod: true } mi ? mi.GetGenericArguments() : null;
        int i = 0;
        while (i < il.Length)
        {
            short code = il[i++];
            if (code == 0xFE) code = (short)(0xFE00 | il[i++]);
            if (!Ops.TryGetValue(code, out var op)) yield break;
            int size = op.OperandType switch
            {
                OperandType.InlineNone => 0,
                OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
                OperandType.InlineVar => 2,
                OperandType.InlineI8 or OperandType.InlineR => 8,
                OperandType.InlineSwitch => 4 + 4 * BitConverter.ToInt32(il, i),
                _ => 4,
            };
            if (i + size > il.Length) yield break;
            if (op.OperandType is OperandType.InlineMethod or OperandType.InlineField or OperandType.InlineTok or OperandType.InlineType)
            {
                MemberInfo? member = null;
                try { member = m.Module.ResolveMember(BitConverter.ToInt32(il, i), ta, ma); } catch (Exception) { }
                yield return member;
            }
            i += size;
        }
    }

    /// <summary>Every top-level type of the App assembly (view models and everything else) that mentions a raw AI type in a signature, a field, a property or an IL operand.</summary>
    private static List<string> AppTypesTouchingRawAi()
    {
        var asm = typeof(AiAnalysisViewModel).Assembly;
        Type[] types;
        try { types = asm.GetTypes(); } catch (ReflectionTypeLoadException ex) { types = ex.Types.Where(t => t is not null).ToArray()!; }
        var hits = new List<string>();
        foreach (var top in types.Where(t => t.DeclaringType is null))
        {
            bool raw = false;
            foreach (var t in WithNested(top))
            {
                raw |= t.GetFields(AllMembers).Any(f => Mentions(f.FieldType)) || t.GetProperties(AllMembers).Any(p => Mentions(p.PropertyType));
                foreach (var m in t.GetMethods(AllMembers).Cast<MethodBase>().Concat(t.GetConstructors(AllMembers)))
                {
                    if (m is MethodInfo info && Mentions(info.ReturnType)) raw = true;
                    if (m.GetParameters().Any(p => Mentions(p.ParameterType))) raw = true;
                    foreach (var member in IlMembers(m))
                    {
                        if (Mentions(member as Type ?? member?.DeclaringType)) raw = true;
                        if (member is MethodInfo called && Mentions(called.ReturnType)) raw = true;
                    }
                }
            }
            if (raw) hits.Add(top.FullName!);
        }
        return hits;
    }

    [Fact]
    public void No_App_view_model_or_other_App_type_reads_raw_AI_types_except_the_validated_AI_screen()
    {
        var hits = AppTypesTouchingRawAi();
        Assert.Contains("LogAnalyzer.UI.ViewModels.AiAnalysisViewModel", hits);   // the scan really sees the known reader: an empty scan cannot pass
        var unexpected = hits.Where(h => !AllowedInApp.Contains(h)).OrderBy(h => h).ToList();
        Assert.True(unexpected.Count == 0, "Tipuri din aplicație care ating text AI brut fără validare (adăugați-le la o cale validată sau la lista permisă, cu motiv):\n" + string.Join("\n", unexpected));
    }
}
