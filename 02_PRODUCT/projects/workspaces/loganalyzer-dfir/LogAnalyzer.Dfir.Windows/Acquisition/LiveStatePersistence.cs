using System.Management;
using System.Security.Cryptography;
using LogAnalyzer.Dfir.FileSystem;
using LogAnalyzer.Dfir.Model;
using LogAnalyzer.Dfir.Windows.Containment;
using LogAnalyzer.Dfir.Windows.Native;

namespace LogAnalyzer.Dfir.Windows.Acquisition;

/// <summary>One permanent WMI event subscription of the live system: the filter, the consumer and what the consumer runs (root\subscription, read only).</summary>
public sealed record WmiSubscriptionRow(string Filter, string FilterQuery, string FilterNamespace, string Consumer, string ConsumerClass, string Command);

/// <summary>One file of a Startup folder (all users or one user) with hash, size, times and, for a shortcut, its target and arguments.</summary>
public sealed record StartupRow(string Scope, string Path, string Name, long Size, string Sha256, string CreatedUtc, string ModifiedUtc, string AccessedUtc,
                                string Target, string Arguments, string Signature, bool UserWritable);

/// <summary>
/// Planner #11 A8: persistence places the live snapshot did not read before. WMI permanent subscriptions (root\subscription:
/// __FilterToConsumerBinding with its __EventFilter and consumer) and the Startup folders. Only reads: no WMI method is called, no file is changed.
/// </summary>
public static class LiveStatePersistence
{
    public const long MaxHashBytes = 64L * 1024 * 1024;

    /// <summary>Class and name from a WMI object path such as <c>\\HOST\ROOT\subscription:CommandLineEventConsumer.Name="x"</c> (backslash and quote inside the name are escaped by WMI).</summary>
    public static (string Class, string Name) ParseWmiRef(string? path)
    {
        if (string.IsNullOrEmpty(path)) return ("", "");
        int colon = path.IndexOf(':');
        var rest = colon >= 0 ? path[(colon + 1)..] : path;
        int dot = rest.IndexOf('.');
        string cls = dot >= 0 ? rest[..dot] : rest;
        int q1 = rest.IndexOf('"'), q2 = rest.LastIndexOf('"');
        string name = q1 >= 0 && q2 > q1 ? rest[(q1 + 1)..q2].Replace("\\\\", "\u0001").Replace("\\\"", "\"").Replace("\u0001", "\\") : "";
        return (cls, name);
    }

    public static List<WmiSubscriptionRow> ReadWmiSubscriptions()
    {
        var rows = new List<WmiSubscriptionRow>();
        var scope = new ManagementScope(@"\\.\root\subscription");
        scope.Connect();
        using var bindings = new ManagementObjectSearcher(scope, new ObjectQuery("SELECT * FROM __FilterToConsumerBinding"));
        foreach (ManagementObject b in bindings.Get())
        {
            using (b)
            {
                string fRef = b["Filter"] as string ?? "", cRef = b["Consumer"] as string ?? "";
                var (_, filterName) = ParseWmiRef(fRef);
                var (consumerClass, consumerName) = ParseWmiRef(cRef);
                string query = "", ns = "", command = "";
                try
                {
                    using var f = new ManagementObject(scope, new ManagementPath(fRef), null);
                    f.Get();
                    query = f["Query"] as string ?? ""; ns = f["EventNamespace"] as string ?? "";
                }
                catch (ManagementException) { }
                try
                {
                    using var c = new ManagementObject(scope, new ManagementPath(cRef), null);
                    c.Get();
                    command = consumerClass switch
                    {
                        "CommandLineEventConsumer" => FirstNonEmpty(c["CommandLineTemplate"] as string, c["ExecutablePath"] as string),
                        "ActiveScriptEventConsumer" => $"{c["ScriptingEngine"]}: " + FirstNonEmpty(c["ScriptFileName"] as string, c["ScriptText"] as string),
                        _ => "",
                    };
                }
                catch (ManagementException) { }
                rows.Add(new WmiSubscriptionRow(filterName, query, ns, consumerName, consumerClass, command.Length > 2000 ? command[..2000] : command));
            }
        }
        return rows;
    }

    private static string FirstNonEmpty(string? a, string? b) => !string.IsNullOrWhiteSpace(a) ? a : b ?? "";

    private static readonly string[] SkipUsers = ["Public", "Default", "Default User", "All Users"];

    public static List<StartupRow> ReadStartupFolders(List<string> errors)
    {
        var rows = new List<StartupRow>();
        var dirs = new List<(string Scope, string Dir)> { ("toți utilizatorii", Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup)) };
        string users = Path.Combine(Path.GetPathRoot(Environment.SystemDirectory) ?? @"C:\", "Users");
        try
        {
            foreach (var u in Directory.EnumerateDirectories(users))
            {
                var name = Path.GetFileName(u);
                if (SkipUsers.Contains(name, StringComparer.OrdinalIgnoreCase)) continue;
                dirs.Add(("utilizatorul " + name, Path.Combine(u, @"AppData\Roaming\Microsoft\Windows\Start Menu\Programs\Startup")));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { errors.Add($"foldere Startup ale utilizatorilor: {ex.Message}"); }
        foreach (var (scope, dir) in dirs)
        {
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) continue;
            try
            {
                foreach (var f in Directory.EnumerateFiles(dir))
                {
                    if (Path.GetFileName(f).Equals("desktop.ini", StringComparison.OrdinalIgnoreCase)) continue;
                    try { rows.Add(RowFor(scope, f)); }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException) { errors.Add($"Startup {f}: {ex.Message}"); }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { errors.Add($"Startup {dir}: {ex.Message}"); }
        }
        return rows;
    }

    private static StartupRow RowFor(string scope, string file)
    {
        var fi = new FileInfo(file);
        string sha = "";
        if (fi.Length <= MaxHashBytes)
        {
            using var s = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            sha = Convert.ToHexString(SHA256.HashData(s));
        }
        string target = "", args = "", signature = "";
        bool writable = SuspiciousProcessDetector.IsUserWritableLocation(file);
        if (StartupRules.Ext(file) == ".lnk" && fi.Length <= LnkParser.MaxLinkFileBytes)
        {
            var link = LnkParser.Decode(File.ReadAllBytes(file));
            target = link.Target.Length > 0 ? link.Target : link.EnvTarget;
            args = link.Arguments.Trim();
            var exe = LiveStateCollector.ExePath(Environment.ExpandEnvironmentVariables(target));
            writable = SuspiciousProcessDetector.IsUserWritableLocation(exe);
            if (exe.Length > 0 && File.Exists(exe) && StartupRules.IsExecutable(exe)) signature = LiveStateCollector.Describe(Authenticode.Verify(exe));
        }
        else if (StartupRules.IsExecutable(file)) signature = LiveStateCollector.Describe(Authenticode.Verify(file));
        string Iso(DateTime t) => t.ToUniversalTime().ToString("o", System.Globalization.CultureInfo.InvariantCulture);
        return new StartupRow(scope, file, fi.Name, fi.Length, sha, Iso(fi.CreationTime), Iso(fi.LastWriteTime), Iso(fi.LastAccessTime), target, args, signature, writable);
    }
}

/// <summary>Which Startup entries are findings; shared by the analyzer and the tests. A signed, valid executable is a well-known entry and gives no finding.</summary>
public static class StartupRules
{
    private static readonly string[] Scripts = [".bat", ".cmd", ".ps1", ".vbs", ".vbe", ".js", ".jse", ".wsf", ".hta"];
    private static readonly string[] Executables = [".exe", ".scr", ".com", ".dll", ".jar", ".pif", ".msi"];
    private static readonly string[] Interpreters = ["powershell.exe", "pwsh.exe", "cmd.exe", "wscript.exe", "cscript.exe", "mshta.exe", "rundll32.exe", "regsvr32.exe"];

    public static string Ext(string path) => Path.GetExtension(path ?? "").ToLowerInvariant();
    public static bool IsExecutable(string path) => Executables.Contains(Ext(path));
    public static bool IsScript(string path) => Scripts.Contains(Ext(path));
    public static bool IsSigned(string signature) => signature.StartsWith("semnat (", StringComparison.OrdinalIgnoreCase);

    /// <summary>Severity and the reason in words, or null when the entry is not a finding.</summary>
    public static (Severity Severity, string Reason)? Classify(StartupRow r)
    {
        string ext = Ext(r.Name);
        if (ext == ".lnk")
        {
            string host = Path.GetFileName(r.Target.Replace('/', '\\')).ToLowerInvariant();
            bool env = r.Target.Contains("%TEMP%", StringComparison.OrdinalIgnoreCase) || r.Target.Contains("%APPDATA%", StringComparison.OrdinalIgnoreCase)
                       || r.Arguments.Contains("%TEMP%", StringComparison.OrdinalIgnoreCase) || r.Arguments.Contains("AppData", StringComparison.OrdinalIgnoreCase);
            if (Interpreters.Contains(host) && r.Arguments.Length > 0)
                return (Severity.High, $"scurtătura pornește {host} cu argumente");
            if ((r.UserWritable || env) && !IsSigned(r.Signature))
                return (r.Arguments.Length > 0 ? Severity.High : Severity.Medium, "scurtătura indică o locație în care utilizatorii pot scrie" + (r.Signature.Length > 0 ? $" ({r.Signature})" : ", fără semnătură verificată"));
            return null;
        }
        if (IsScript(r.Name)) return (Severity.Medium, $"script ({ext}) în folderul Startup, rulează la fiecare autentificare");
        if (IsExecutable(r.Name))
        {
            if (IsSigned(r.Signature)) return null;
            return r.Signature.Length > 0 ? (Severity.High, $"executabil în folderul Startup ({r.Signature})") : (Severity.Medium, "executabil în folderul Startup, semnătura nu a fost verificată");
        }
        return null;
    }
}
