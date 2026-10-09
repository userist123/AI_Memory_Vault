using System.Globalization;
using LogAnalyzer.Dfir.Windows.Policy;
using Microsoft.Win32;

namespace LogAnalyzer.Response.Policy;

/// <summary>Registry writes of the policy executor (HKLM/HKCU, 64-bit view). Unclassified edition only.</summary>
public sealed class RegistryValueWriter : IRegistryValueWriter
{
    private static RegistryKey Base(string hive) => RegistryKey.OpenBaseKey(hive switch
    {
        "HKLM" => RegistryHive.LocalMachine,
        "HKCU" => RegistryHive.CurrentUser,
        _ => throw new NotSupportedException($"hive {hive}"),
    }, RegistryView.Registry64);

    public void Write(string hive, string key, string name, string valueType, string value)
    {
        using var root = Base(hive);
        using var k = root.CreateSubKey(key, writable: true);
        switch (valueType)
        {
            case "dword": k.SetValue(name, unchecked((int)uint.Parse(value, CultureInfo.InvariantCulture)), RegistryValueKind.DWord); break;
            case "qword": k.SetValue(name, unchecked((long)ulong.Parse(value, CultureInfo.InvariantCulture)), RegistryValueKind.QWord); break;
            case "string": k.SetValue(name, value, RegistryValueKind.String); break;
            case "expand_string": k.SetValue(name, value, RegistryValueKind.ExpandString); break;
        }
    }

    public void Delete(string hive, string key, string name)
    {
        using var root = Base(hive);
        using var k = root.OpenSubKey(key, writable: true);
        k?.DeleteValue(name, throwOnMissingValue: false);
    }
}
