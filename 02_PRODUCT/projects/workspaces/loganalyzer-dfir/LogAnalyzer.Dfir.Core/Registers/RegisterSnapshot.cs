using System.Text.Json;
using LogAnalyzer.Dfir.Case;
using LogAnalyzer.Dfir.IO;

namespace LogAnalyzer.Dfir.Registers;

/// <summary>Using a register in a case: a copy of exactly what was used goes into the case and into its custody, so the result can be explained later.</summary>
public static class RegisterSnapshot
{
    public static string RelPath(IRegisterData reg) => $"Analysis/{reg.Kind}_register.json";

    /// <summary>
    /// Writes <c>Analysis/&lt;kind&gt;_register.json</c>, registers it with <see cref="CaseWorkspace.RecordOutput"/> (SHA-256 in the custody chain) and audits
    /// <c>register.used</c>. A register with no rows is "registru nedefinit": nothing is written, <c>register.undefined</c> is audited and "" is returned.
    /// </summary>
    public static string WriteTo(CaseWorkspace ws, IRegisterData reg)
    {
        if (!reg.IsDefined)
        {
            ws.Audit("register.undefined", $"{reg.Kind}: registru nedefinit (niciun rând); observațiile nu pot fi comparate cu un registru");
            return "";
        }
        var rel = RelPath(reg);
        var full = ws.FullPath(rel);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, JsonSerializer.Serialize(reg, reg.GetType(), Json.Options));
        var rec = ws.RecordOutput(rel, reg.Kind == "media" ? "MediaRegister" : "UsersRegister", reg.SchemaVersion);
        ws.Audit("register.used", $"{reg.Kind}: sha256={rec.Sha256}; {reg.Cells().Count} rânduri");
        return rec.Sha256;
    }
}
