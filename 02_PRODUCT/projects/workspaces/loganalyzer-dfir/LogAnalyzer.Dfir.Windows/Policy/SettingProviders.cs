using System.Globalization;
using System.Security.Principal;
using LogAnalyzer.Dfir.Policy;
using LogAnalyzer.Dfir.Windows.Native;
using Microsoft.Win32;

namespace LogAnalyzer.Dfir.Windows.Policy;

/// <summary>The providers the policy executor uses on this station.</summary>
public static class WindowsSettingProviders
{
    public static IReadOnlyList<ISettingProvider> All() =>
        [new RegistrySettingProvider(), new AuditSettingProvider(), new ServiceStartProvider(), new AccountPolicyProvider()];

    internal static bool IsElevated()
    {
        using var id = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
    }
}

/// <summary>
/// Registry values in HKLM/HKCU (64-bit view). Values are rendered the way policies state them: DWORD/QWORD as unsigned decimal,
/// strings unexpanded, REG_MULTI_SZ joined by spaces (read only: the parts cannot be rebuilt from that text), binary as hex.
/// </summary>
public sealed class RegistrySettingProvider : ISettingProvider
{
    public bool Handles(SettingRef s) => s.Type == "registry";

    private static RegistryKey Base(SettingRef s) => RegistryKey.OpenBaseKey(s.Hive switch
    {
        "HKLM" => RegistryHive.LocalMachine,
        "HKCU" => RegistryHive.CurrentUser,
        _ => throw new NotSupportedException($"hive {s.Hive}"),
    }, RegistryView.Registry64);

    public SettingValue Read(SettingRef s)
    {
        using var root = Base(s);
        using var k = root.OpenSubKey(s.Key);
        var v = k?.GetValue(s.Name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
        if (k is null || v is null) return SettingValue.Absent;
        return k.GetValueKind(s.Name) switch
        {
            RegistryValueKind.DWord => new(unchecked((uint)(int)v).ToString(CultureInfo.InvariantCulture), "dword"),
            RegistryValueKind.QWord => new(unchecked((ulong)(long)v).ToString(CultureInfo.InvariantCulture), "qword"),
            RegistryValueKind.String => new((string)v, "string"),
            RegistryValueKind.ExpandString => new((string)v, "expand_string"),
            RegistryValueKind.MultiString => new(string.Join(" ", (string[])v), "multi_string"),
            RegistryValueKind.Binary => new(Convert.ToHexString((byte[])v), "binary"),
            var other => new(v.ToString(), other.ToString().ToLowerInvariant()),
        };
    }

    public string? CannotWrite(SettingRef s) =>
        s.ValueType is not ("dword" or "qword" or "string" or "expand_string") ? $"tipul {s.ValueType} nu este scris de motor"
        : s.Hive == "HKLM" && !WindowsSettingProviders.IsElevated() ? "HKLM cere un proces rulat ca administrator"
        : null;

    public void Write(SettingRef s, string value)
    {
        if (CannotWrite(s) is { } why) throw new InvalidOperationException(why);
        using var root = Base(s);
        using var k = root.CreateSubKey(s.Key, writable: true);
        switch (s.ValueType)
        {
            case "dword": k.SetValue(s.Name, unchecked((int)uint.Parse(value, CultureInfo.InvariantCulture)), RegistryValueKind.DWord); break;
            case "qword": k.SetValue(s.Name, unchecked((long)ulong.Parse(value, CultureInfo.InvariantCulture)), RegistryValueKind.QWord); break;
            case "string": k.SetValue(s.Name, value, RegistryValueKind.String); break;
            case "expand_string": k.SetValue(s.Name, value, RegistryValueKind.ExpandString); break;
        }
    }

    public void Delete(SettingRef s)
    {
        using var root = Base(s);
        using var k = root.OpenSubKey(s.Key, writable: true);
        k?.DeleteValue(s.Name, throwOnMissingValue: false);
    }
}

/// <summary>Advanced audit policy per subcategory (AuditQuerySystemPolicy). Read only: applying audit policy is not implemented.</summary>
public sealed class AuditSettingProvider : ISettingProvider
{
    public bool Handles(SettingRef s) => s.Type == "audit";

    public SettingValue Read(SettingRef s)
    {
        if (!AuditSubcategories.TryResolve(s.Name, out var guid)) throw new ArgumentException($"subcategorie de audit necunoscută: {s.Name}");
        return new(AuditSubcategories.FromFlags(AuditPolicy.QueryFlags(guid)), null);
    }

    public string? CannotWrite(SettingRef s) => "aplicarea politicii de audit nu este implementată (se face manual, cu auditpol /set)";
    public void Write(SettingRef s, string value) => throw new NotSupportedException(CannotWrite(s));
    public void Delete(SettingRef s) => throw new NotSupportedException(CannotWrite(s));
}

/// <summary>Service start type as stored by the SCM (HKLM\SYSTEM\CurrentControlSet\Services\name\Start): 0 boot, 1 system, 2 automatic, 3 manual, 4 disabled.</summary>
public sealed class ServiceStartProvider : ISettingProvider
{
    public bool Handles(SettingRef s) => s.Type == "service";

    public SettingValue Read(SettingRef s)
    {
        using var root = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        using var k = root.OpenSubKey($@"SYSTEM\CurrentControlSet\Services\{s.Name}");
        return k?.GetValue("Start") is int start ? new(start.ToString(CultureInfo.InvariantCulture), null) : SettingValue.Absent;
    }

    public string? CannotWrite(SettingRef s) => "modificarea serviciilor nu este implementată";
    public void Write(SettingRef s, string value) => throw new NotSupportedException(CannotWrite(s));
    public void Delete(SettingRef s) => throw new NotSupportedException(CannotWrite(s));
}

/// <summary>
/// Account policy in the units of security templates: ages in days, lockout duration and reset window in minutes, -1 for
/// "never" (TIMEQ_FOREVER). Read only (NetUserModalsGet levels 0 and 3).
/// </summary>
public sealed class AccountPolicyProvider : ISettingProvider
{
    public bool Handles(SettingRef s) => s.Type == "secpol";

    public SettingValue Read(SettingRef s)
    {
        var p = LocalPasswordPolicy.Query() ?? throw new InvalidOperationException("NetUserModalsGet a eșuat");
        long v = s.Name.ToLowerInvariant() switch
        {
            "minimumpasswordlength" => p.MinLength,
            "maximumpasswordage" => p.MaxAge is { } max ? (long)max.TotalDays : -1,
            "minimumpasswordage" => (long)p.MinAge.TotalDays,
            "passwordhistorysize" => p.History,
            "lockoutbadcount" => p.LockoutThreshold,
            "lockoutduration" => p.LockoutDuration == TimeSpan.MaxValue ? -1 : (long)p.LockoutDuration.TotalMinutes,
            "resetlockoutcount" => (long)p.LockoutWindow.TotalMinutes,
            _ => throw new ArgumentException($"setarea de cont {s.Name} nu este citită"),
        };
        return new(v.ToString(CultureInfo.InvariantCulture), null);
    }

    public string? CannotWrite(SettingRef s) => "aplicarea politicii de cont nu este implementată";
    public void Write(SettingRef s, string value) => throw new NotSupportedException(CannotWrite(s));
    public void Delete(SettingRef s) => throw new NotSupportedException(CannotWrite(s));
}
