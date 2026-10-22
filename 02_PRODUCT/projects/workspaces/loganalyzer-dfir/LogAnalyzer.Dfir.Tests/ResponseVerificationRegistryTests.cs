using System.Reflection;
using System.Runtime.CompilerServices;
using LogAnalyzer.Dfir.Response;
using Xunit;

namespace LogAnalyzer.Dfir.Tests;

/// <summary>
/// No response path may return success without the verifier. The production assemblies are scanned (IL): every method that reaches the
/// host-change gate (OperatorIdentity.RequireHostChange / MayChangeHost) or a host-changing primitive (firewall rule add/remove, firewall
/// command, setting write/delete, registry write, process suspend/resume/kill) must be a [ControlledResponse] whose result carries a
/// ResponseOutcome, or a private helper reached only from such methods, or an explicitly reasoned exception below. A new, unverified
/// response therefore fails this test.
/// </summary>
public sealed class ResponseVerificationRegistryTests
{
    private static readonly string[] ProductionAssemblies =
        ["LogAnalyzer.Dfir.Core", "LogAnalyzer.Core", "LogAnalyzer.Dfir.Windows", "LogAnalyzer.Response", "LogAnalyzer.Infrastructure", "LogAnalyzer.Connectors"];

    /// <summary>Methods that touch the gate or a primitive but are not a response, each with the reason.</summary>
    private static readonly Dictionary<string, string> NotAResponse = new()
    {
        ["LogAnalyzer.Dfir.Policy.PolicyStore.Approve"] = "approval only records an authorisation in the store; it changes nothing on the station",
        ["LogAnalyzer.Dfir.Windows.Containment.ProcessScanner.DefenderScan"] = "kills only the Defender scan process it started itself, on timeout",
        ["LogAnalyzer.Dfir.Windows.Acquisition.ToolRunner.Run"] = "kills only the collector process it started itself, on timeout or cancel",
    };

    private static readonly (string Type, string Method)[] GateMembers =
    [
        ("LogAnalyzer.Dfir.Auth.OperatorIdentity", "RequireHostChange"),
        ("LogAnalyzer.Dfir.Auth.OperatorIdentity", "get_MayChangeHost"),
    ];

    /// <summary>Host-changing primitives (by declaring type or interface). Their own implementations are the primitive, not a caller.</summary>
    private static readonly (string Type, string[] Methods)[] Primitives =
    [
        ("LogAnalyzer.Dfir.Windows.Containment.IFirewallController", ["AddProgramBlockRule", "RemoveRule"]),
        ("LogAnalyzer.Infrastructure.Services.IFirewallCommandRunner", ["Netsh"]),
        ("LogAnalyzer.Dfir.Policy.ISettingProvider", ["Write", "Delete"]),
        ("LogAnalyzer.Dfir.Windows.Policy.IRegistryValueWriter", ["Write", "Delete"]),
        ("LogAnalyzer.Dfir.Windows.Native.ProcessSuspension", ["Suspend", "Resume"]),
        ("System.Diagnostics.Process", ["Kill"]),
        ("Microsoft.Win32.RegistryKey", ["SetValue", "DeleteValue", "DeleteSubKey", "DeleteSubKeyTree"]),
        ("System.ServiceProcess.ServiceController", ["Stop", "Pause"]),
    ];

    /// <summary>
    /// Every assembly of <see cref="ProductionAssemblies"/>, loaded by name. A missing one is a test failure, never a silent skip: an
    /// assembly that is not scanned could hold an unverified response (the test project references each of them).
    /// </summary>
    private static List<Assembly> Load()
    {
        var loaded = new List<Assembly>();
        var missing = new List<string>();
        foreach (var name in ProductionAssemblies)
        {
            try { loaded.Add(Assembly.Load(name)); }
            catch (FileNotFoundException ex) { missing.Add($"{name} ({ex.Message})"); }
        }
        Assert.True(missing.Count == 0, "Ansambluri de producție nescanate (lipsesc din bin-ul testelor): " + string.Join("; ", missing));
        return loaded;
    }

    [Fact]
    public void Every_listed_production_assembly_is_loaded_and_scanned()
    {
        var names = Load().Select(a => a.GetName().Name).ToList();
        Assert.Equal(ProductionAssemblies.OrderBy(n => n), names.OrderBy(n => n));
    }

    private static string Key(MethodBase m)
    {
        // A lambda or iterator body belongs to the method that declares it: <Contain>b__0 in a nested display class -> Contain.
        var t = m.DeclaringType;
        if (t is null) return m.Module.Name + "::" + m.Name;
        string name = m.Name;
        while ((t.IsDefined(typeof(CompilerGeneratedAttribute)) || t.Name.StartsWith('<')) && t.DeclaringType is not null)
        {
            if (t.Name.StartsWith('<') && t.Name.IndexOf('>') > 1) name = t.Name[1..t.Name.IndexOf('>')];
            t = t.DeclaringType!;
        }
        if (name.StartsWith('<') && name.IndexOf('>') > 1) name = name[1..name.IndexOf('>')];
        return $"{t.FullName}.{name}";
    }

    /// <summary>Methods called by <paramref name="m"/> (call, callvirt, newobj, ldftn, ldvirtftn), read from its IL.</summary>
    private static IEnumerable<MethodBase> Callees(MethodBase m)
    {
        byte[]? il;
        try { il = m.GetMethodBody()?.GetILAsByteArray(); } catch (Exception) { yield break; }
        if (il is null) yield break;
        var module = m.Module;
        Type[]? ta = m.DeclaringType is { IsGenericType: true } dt ? dt.GetGenericArguments() : null;
        Type[]? ma = m is MethodInfo { IsGenericMethod: true } mi ? mi.GetGenericArguments() : null;
        for (int i = 0; i + 4 < il.Length; i++)
        {
            bool call = il[i] is 0x28 or 0x6F or 0x73 || (il[i] == 0xFE && i + 5 < il.Length && il[i + 1] is 0x06 or 0x07);
            if (!call) continue;
            int at = il[i] == 0xFE ? i + 2 : i + 1;
            int token = BitConverter.ToInt32(il, at);
            if ((token >> 24) is not (0x06 or 0x0A or 0x2B)) continue;   // MethodDef, MemberRef, MethodSpec
            MethodBase? target = null;
            try { target = module.ResolveMethod(token, ta, ma); } catch (Exception) { }
            if (target is not null) yield return target;
        }
    }

    private static bool IsPrimitive(MethodBase callee) =>
        Primitives.Any(p => p.Methods.Contains(callee.Name) && (callee.DeclaringType?.FullName == p.Type
            || callee.DeclaringType?.GetInterfaces().Any(i => i.FullName == p.Type) == true));

    private static bool IsGate(MethodBase callee) => GateMembers.Any(g => callee.DeclaringType?.FullName == g.Type && callee.Name == g.Method);

    /// <summary>Methods of a type that implements a primitive interface are the primitive (the firewall controller, the setting providers).</summary>
    private static bool ImplementsPrimitive(Type t) =>
        t.GetInterfaces().Any(i => Primitives.Any(p => p.Type == i.FullName));

    private sealed record Scan(Dictionary<string, MethodBase> Methods, Dictionary<string, HashSet<string>> CallersOf, HashSet<string> Touching);

    private static Scan ScanAll()
    {
        const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
        var methods = new Dictionary<string, MethodBase>();
        var callersOf = new Dictionary<string, HashSet<string>>();
        var touching = new HashSet<string>();
        foreach (var asm in Load())
        {
            Type[] types;
            try { types = asm.GetTypes(); } catch (ReflectionTypeLoadException ex) { types = ex.Types.Where(t => t is not null).ToArray()!; }
            foreach (var t in types)
            {
                if (t.DeclaringType is null && t.Name.StartsWith('<')) continue;   // <Module>, <PrivateImplementationDetails>: compiler data, no responses
                foreach (var m in t.GetMethods(all).Cast<MethodBase>().Concat(t.GetConstructors(all)))
                {
                    var key = Key(m);
                    var outer = OuterType(t);
                    if (!t.IsDefined(typeof(CompilerGeneratedAttribute)) && !m.Name.StartsWith('<')) methods.TryAdd(key, m);
                    foreach (var c in Callees(m))
                    {
                        if (outer.FullName == GateMembers[0].Type) continue;   // the gate itself
                        if (IsGate(c) || (IsPrimitive(c) && !ImplementsPrimitive(outer) && !(c.DeclaringType == outer))) touching.Add(key);
                        if (c.DeclaringType is { } cd && OuterType(cd) == outer)
                        {
                            var ck = Key(c);
                            if (ck != key) { if (!callersOf.TryGetValue(ck, out var set)) callersOf[ck] = set = new(); set.Add(key); }
                        }
                    }
                }
            }
        }
        return new Scan(methods, callersOf, touching);
    }

    private static Type OuterType(Type t)
    {
        while ((t.IsDefined(typeof(CompilerGeneratedAttribute)) || t.Name.StartsWith('<')) && t.DeclaringType is not null) t = t.DeclaringType;
        return t;
    }

    private static bool IsControlled(MethodBase m) => m.IsDefined(typeof(ControlledResponseAttribute), false);

    /// <summary>The result of a controlled response must carry a ResponseOutcome (itself, or a public property, Task unwrapped).</summary>
    private static bool CarriesOutcome(MethodBase m)
    {
        if (m is not MethodInfo mi) return false;
        var t = mi.ReturnType;
        if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(Task<>)) t = t.GetGenericArguments()[0];
        return t == typeof(ResponseOutcome)
               || t.GetProperties(BindingFlags.Public | BindingFlags.Instance).Any(p => p.PropertyType == typeof(ResponseOutcome));
    }

    [Fact]
    public void Every_method_that_changes_the_station_is_a_verified_controlled_response()
    {
        var scan = ScanAll();
        Assert.NotEmpty(scan.Touching);

        // Covered: [ControlledResponse], or (fixpoint) a non-public helper whose every caller in the same type is covered.
        var covered = new HashSet<string>(scan.Methods.Where(kv => IsControlled(kv.Value)).Select(kv => kv.Key));
        bool changed = true;
        while (changed)
        {
            changed = false;
            foreach (var (key, m) in scan.Methods)
            {
                if (covered.Contains(key) || !m.IsPrivate) continue;   // only private helpers inherit coverage from their callers
                if (scan.CallersOf.TryGetValue(key, out var callers) && callers.Count > 0 && callers.All(covered.Contains)) { covered.Add(key); changed = true; }
            }
        }

        var uncovered = scan.Touching.Where(k => !covered.Contains(k) && !NotAResponse.ContainsKey(k)).OrderBy(k => k).ToList();
        Assert.True(uncovered.Count == 0, "Methods that change the station without the response verifier:\n" + string.Join("\n", uncovered));
        // The exceptions stay honest: each one must still exist and still touch the gate or a primitive.
        var stale = NotAResponse.Keys.Where(k => !scan.Touching.Contains(k)).ToList();
        Assert.True(stale.Count == 0, "Stale exceptions (remove them):\n" + string.Join("\n", stale));
    }

    [Fact]
    public void Every_controlled_response_returns_a_response_outcome_and_the_inventory_is_complete()
    {
        var scan = ScanAll();
        var controlled = scan.Methods.Values.Where(IsControlled).ToList();
        var wrong = controlled.Where(m => !CarriesOutcome(m)).Select(Key).ToList();
        Assert.True(wrong.Count == 0, "Controlled responses whose result has no ResponseOutcome:\n" + string.Join("\n", wrong));

        var names = controlled.Select(Key).ToHashSet();
        foreach (var expected in new[]
        {
            "LogAnalyzer.Dfir.Windows.Containment.ProcessContainmentService.Contain",
            "LogAnalyzer.Dfir.Windows.Containment.ProcessContainmentService.Release",
            "LogAnalyzer.Infrastructure.Services.SystemDefenseExecutionService.IsolateHostFromNetwork",
            "LogAnalyzer.Infrastructure.Services.SystemDefenseExecutionService.RestoreNetworkAccess",
            "LogAnalyzer.Infrastructure.Services.SystemDefenseExecutionService.BlockMaliciousIoC",
            "LogAnalyzer.Dfir.Policy.PolicyExecutor.Apply",
            "LogAnalyzer.Dfir.Policy.PolicyExecutor.Rollback",
            "LogAnalyzer.Response.Containment.AuditPolicyChange.EnableBlockedConnectionFailureAudit",
            "LogAnalyzer.Core.Services.MediaSanitizationEngine.SanitizeMediaAsync",
        })
            Assert.Contains(expected, names);
        Assert.All(controlled, m => Assert.False(string.IsNullOrWhiteSpace(m.GetCustomAttribute<ControlledResponseAttribute>()!.ReRead)));
    }

    [Fact]
    public void The_scan_detects_an_unverified_response()
    {
        // Guard against a scan that sees nothing: this test type has an unmarked method that calls the gate.
        var m = typeof(ResponseVerificationRegistryTests).GetMethod(nameof(UnverifiedResponseForTheScan), BindingFlags.NonPublic | BindingFlags.Static)!;
        Assert.Contains(Callees(m), IsGate);
        Assert.False(IsControlled(m));
    }

    private static void UnverifiedResponseForTheScan() => LogAnalyzer.Dfir.Auth.OperatorIdentity.RequireHostChange();
}
