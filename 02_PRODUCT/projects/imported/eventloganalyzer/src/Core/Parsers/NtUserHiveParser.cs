using EventLogAnalyzer.Core.Models;
using RegistryLib = Registry; // NuGet package "Registry" by Eric Zimmerman

namespace EventLogAnalyzer.Core.Parsers;

/// <summary>
/// Parses an exported NTUSER.DAT into user-activity items: UserAssist
/// (GUI programs run, with run count / last-run, ROT13-decoded), RecentDocs
/// MRU, and RunMRU. Uses the raw-hive "Registry" library, so this never
/// mounts the hive into HKEY_USERS and needs no elevation to read.
/// </summary>
public sealed class NtUserHiveParser : IArtifactParser<UserActivityItem>
{
    private const string UserAssistRoot =
        @"Software\Microsoft\Windows\CurrentVersion\Explorer\UserAssist";
    private const string RunMruPath =
        @"Software\Microsoft\Windows\CurrentVersion\Explorer\RunMRU";
    private const string RecentDocsPath =
        @"Software\Microsoft\Windows\CurrentVersion\Explorer\RecentDocs";

    public bool CanParse(string filePath) =>
        Path.GetFileName(filePath).StartsWith("NTUSER", StringComparison.OrdinalIgnoreCase)
        && filePath.EndsWith(".dat", StringComparison.OrdinalIgnoreCase);

    public IReadOnlyList<UserActivityItem> Parse(string filePath)
    {
        var hive = new RegistryLib.RegistryHive(filePath)
        {
            // Recovery of dirty hives is common for exports taken while a
            // profile was in use; RecoverDeleted stays false - MVP does not
            // attempt to recover deleted keys, only live ones.
            RecoverDeleted = false
        };
        hive.ParseHive();

        var items = new List<UserActivityItem>();
        items.AddRange(ParseUserAssist(hive, filePath));
        items.AddRange(ParseSimpleMruList(hive, RunMruPath, "RunMRU", filePath));
        items.AddRange(ParseSimpleMruList(hive, RecentDocsPath, "RecentDocs", filePath));
        return items;
    }

    private static IEnumerable<UserActivityItem> ParseUserAssist(RegistryLib.RegistryHive hive, string filePath)
    {
        var root = hive.GetKey(UserAssistRoot);
        if (root is null) yield break;

        // UserAssist has one GUID subkey per "folder" of tracked activity,
        // each containing a "Count" subkey with ROT13-obfuscated value names.
        foreach (var guidKey in root.SubKeys)
        {
            var countKey = guidKey.SubKeys.FirstOrDefault(
                k => k.KeyName.Equals("Count", StringComparison.OrdinalIgnoreCase));
            if (countKey is null) continue;

            foreach (var value in countKey.Values)
            {
                var decodedName = Rot13(value.ValueName);
                // Value data layout (Win7+): 4-byte session id, 4-byte run count,
                // ... , 8-byte FILETIME of last run at a fixed offset.
                var bytes = value.ValueDataRaw;
                if (bytes.Length < 68) continue;

                var runCount = BitConverter.ToInt32(bytes, 4);
                var fileTime = BitConverter.ToInt64(bytes, 60);
                DateTimeOffset? lastRun = fileTime > 0
                    ? DateTimeOffset.FromFileTime(fileTime)
                    : null;

                yield return new UserActivityItem
                {
                    HiveFile = filePath,
                    Category = "UserAssist",
                    Description = decodedName,
                    RunCount = runCount,
                    LastExecuted = lastRun,
                    Path = decodedName
                };
            }
        }
    }

    private static IEnumerable<UserActivityItem> ParseSimpleMruList(
        RegistryLib.RegistryHive hive, string keyPath, string category, string filePath)
    {
        var key = hive.GetKey(keyPath);
        if (key is null) yield break;

        foreach (var value in key.Values.Where(v => v.ValueName != "MRUListEx"))
        {
            yield return new UserActivityItem
            {
                HiveFile = filePath,
                Category = category,
                Description = value.ValueData,
                Path = value.ValueData
            };
        }
    }

    private static string Rot13(string input) =>
        new(input.Select(c => c switch
        {
            >= 'a' and <= 'z' => (char)('a' + (c - 'a' + 13) % 26),
            >= 'A' and <= 'Z' => (char)('A' + (c - 'A' + 13) % 26),
            _ => c
        }).ToArray());
}
