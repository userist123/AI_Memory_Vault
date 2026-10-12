using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace LogAnalyzer.Dfir.IO;

public sealed record NativeManifestCheck(bool Ok, IReadOnlyList<string> Problems)
{
    public string Message => Ok
        ? "Bibliotecile native corespund manifestului de publicare."
        : "Aplicația nu pornește: bibliotecile native din folderul aplicației nu corespund manifestului scris la publicare " +
          $"({NativeLibraryManifest.FileName}). Cineva ar fi putut înlocui sau adăuga o bibliotecă pe care aplicația, pornită ca administrator, " +
          "ar fi încărcat-o. Reinstalați aplicația din pachetul original, într-un folder în care doar administratorii pot scrie. Detalii: " +
          string.Join("; ", Problems.Take(8));
}

/// <summary>
/// Security audit #5. The published executable is single-file WITHOUT native self-extraction: SQLCipher, WPF and QuestPDF native libraries sit next to
/// the exe, in the (administrator-only) install folder. At publish, <c>NativeLibraryManifest.targets</c> writes <see cref="FileName"/>
/// ("SHA256  name.dll" per line, every *.dll in the publish folder). At startup the published app checks that every listed DLL is present and
/// unchanged and that no other DLL was added (a planted DLL would be found first by the loader); any difference fails closed.
/// </summary>
public static class NativeLibraryManifest
{
    public const string FileName = "native-libraries.sha256";

    private static readonly Regex Line = new(@"^(?<h>[0-9A-Fa-f]{64})\s+(?<n>[^\\/:*?""<>|]+\.dll)$", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    public static NativeManifestCheck Verify(string directory)
    {
        var problems = new List<string>();
        var manifestPath = Path.Combine(directory, FileName);
        if (!File.Exists(manifestPath)) return new(false, [$"{FileName} lipsește"]);
        var expected = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var raw in File.ReadAllLines(manifestPath))
            {
                var l = raw.Trim().TrimStart('﻿');
                if (l.Length == 0) continue;
                var m = Line.Match(l);
                if (!m.Success) { problems.Add($"linie de manifest invalidă: „{(l.Length > 80 ? l[..80] : l)}”"); continue; }
                if (!expected.TryAdd(m.Groups["n"].Value, m.Groups["h"].Value)) problems.Add($"{m.Groups["n"].Value} apare de două ori în manifest");
            }
            if (expected.Count == 0 && problems.Count == 0) problems.Add("manifestul nu listează nicio bibliotecă");
            foreach (var (name, hash) in expected)
            {
                var p = Path.Combine(directory, name);
                if (!File.Exists(p)) { problems.Add($"{name} lipsește"); continue; }
                using var fs = File.OpenRead(p);
                if (!Convert.ToHexString(SHA256.HashData(fs)).Equals(hash, StringComparison.OrdinalIgnoreCase)) problems.Add($"{name} a fost modificat (SHA-256 diferit)");
            }
            foreach (var dll in Directory.EnumerateFiles(directory, "*.dll"))
                if (!expected.ContainsKey(Path.GetFileName(dll))) problems.Add($"{Path.GetFileName(dll)} nu este în manifest");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            problems.Add("folderul aplicației nu poate fi citit: " + ex.Message);
        }
        return new(problems.Count == 0, problems);
    }
}
