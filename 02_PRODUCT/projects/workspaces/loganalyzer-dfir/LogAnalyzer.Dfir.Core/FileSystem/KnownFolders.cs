using System.Text.RegularExpressions;
using LogAnalyzer.Dfir.Model;

namespace LogAnalyzer.Dfir.FileSystem;

/// <summary>
/// Resolves the root of a ShellBags path (a known-folder GUID such as Documents, Desktop or Downloads, or the network root) to an absolute path, so a folder that Explorer
/// showed as "Documente\Clasificat" can be matched against a classified-folder root such as C:\Users\x\Documents\Clasificat. Pure; the display path is never changed,
/// the resolved one is a separate field. Limits (stated in the rule contract): a redirected known folder (OneDrive, a server share, another drive) resolves to the default
/// location under the profile, and a profile folder that cannot be found (an imported hive, whose path is on the examiner's machine) leaves the path unresolved.
/// </summary>
public static class KnownFolders
{
    public const string NetworkRootGuid = "{F02C1A0D-BE21-4350-88B0-7367FC96EF3C}";

    // the backslash is written as a Unicode escape so that it can never be mistaken for an escape character
    private const char Bs = '\u005C';
    private const string BsText = "\u005C";
    private const string Unc = "\u005C\u005C";

    /// <summary>Known-folder shell GUID (upper case, with braces) to the folder name under the profile ("" = the profile folder itself).</summary>
    private static readonly Dictionary<string, string> UnderProfile = new(StringComparer.OrdinalIgnoreCase)
    {
        ["{59031A47-3F72-44A7-89C5-5595FE6B30EE}"] = "",
        ["{450D8FBA-AD25-11D0-98A8-0800361B1103}"] = "Documents",
        ["{FDD39AD0-238F-46AF-ADB4-6C85480369C7}"] = "Documents",
        ["{D3162B92-9365-467A-956B-92703ACA08AF}"] = "Documents",
        ["{374DE290-123F-4565-9164-39C4925E467B}"] = "Downloads",
        ["{088E3905-0323-4B02-9826-5D99428E115F}"] = "Downloads",
        ["{24AD3AD4-A569-4530-98E1-AB02F9417AA8}"] = "Pictures",
        ["{4BD8D571-6D19-48D3-BE97-422220080E43}"] = "Music",
        ["{A0953C92-50DC-43BF-BE83-3742FED03C9C}"] = "Videos",
        ["{B4BFCC3A-DB2C-424C-B029-7FE99A87C641}"] = "Desktop",
    };

    // \x5C is the backslash
    private static readonly Regex NoteProfile = new(@"^Profil \S+ \((?<p>.+?)\)\. ", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// The profile folder of the hive's owner, ONLY from the collection note "Profil &lt;SID&gt; (&lt;path&gt;)" written by UserArtifactsCollector on the station itself
    /// (Collector == UserArtifactsCollector and AcquisitionMethod == "acquired": the same trust rule as the Recycle Bin $R state). An imported hive's original path is on the
    /// examiner's machine, not on the station's disk, so it is never used and a crafted note on an import is ignored. "" when unknown or when the path is a device path.
    /// </summary>
    public static string ProfileDirOf(EvidenceItem item)
    {
        if (item.Collector != RecycleBinInfoParser.CollectedBy || item.AcquisitionMethod != "acquired" || string.IsNullOrEmpty(item.Notes)) return "";
        return NoteProfile.Match(item.Notes) is { Success: true } n && IsAbsolute(n.Groups["p"].Value) ? n.Groups["p"].Value.TrimEnd(Bs, '/') : "";
    }

    /// <summary>True when the GUID is a known-folder root that needs the profile folder to be placed on a disk (Documents, Desktop, Downloads...).</summary>
    public static bool IsProfileRoot(string guid) => guid.Length > 0 && UnderProfile.ContainsKey(guid);

    /// <summary>True when the GUID is a root that <see cref="Resolve"/> can anchor (a profile known folder or the network root).</summary>
    public static bool IsKnownRoot(string guid) => IsProfileRoot(guid) || guid.Equals(NetworkRootGuid, StringComparison.OrdinalIgnoreCase);

    // a device prefix (question mark or dot right after the two leading backslashes) is refused, like for the hive path: it names a device, not a folder of the station's disk
    private static bool IsAbsolute(string p) => p.Length >= 3 && ((char.IsAsciiLetter(p[0]) && p[1] == ':' && (p[2] == Bs || p[2] == '/')) || (p.StartsWith(Unc, StringComparison.Ordinal) && p.Length > 3 && p[2] != '?' && p[2] != '.'));

    /// <summary>
    /// The absolute path for a BagMRU path whose first item is a root folder with GUID <paramref name="rootGuid"/>; <paramref name="segments"/> are all items, the root first.
    /// null when the root is not a known folder, the profile is unknown (for profile folders) or there is nothing to resolve.
    /// </summary>
    public static string? Resolve(string rootGuid, IReadOnlyList<string> segments, string profileDir)
    {
        if (segments.Count == 0 || rootGuid.Length == 0) return null;
        var rest = segments.Skip(1).Select(s => s.Trim(Bs)).Where(s => s.Length > 0).ToList();
        if (rootGuid.Equals(NetworkRootGuid, StringComparison.OrdinalIgnoreCase))
        {
            // a server item may carry the full "\\server" and a share item "\\server\share"; the last item that already begins with a UNC prefix wins
            var parts = new List<string>();
            foreach (var s in segments.Skip(1))
            {
                var t = s.Trim(Bs);
                if (t.Length == 0) continue;
                if (s.StartsWith(Unc, StringComparison.Ordinal)) parts = [.. t.Split(Bs, StringSplitOptions.RemoveEmptyEntries)];
                else parts.Add(t);
            }
            return parts.Count == 0 ? null : Unc + string.Join(Bs, parts);
        }
        if (profileDir.Length == 0 || !UnderProfile.TryGetValue(rootGuid, out var sub)) return null;
        var baseDir = sub.Length > 0 ? profileDir.TrimEnd(Bs) + BsText + sub : profileDir.TrimEnd(Bs);
        return rest.Count == 0 ? baseDir : baseDir + BsText + string.Join(Bs, rest);
    }
}
