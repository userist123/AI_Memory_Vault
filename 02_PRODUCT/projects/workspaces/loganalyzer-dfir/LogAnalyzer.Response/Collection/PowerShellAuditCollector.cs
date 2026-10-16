using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LogAnalyzer.Core.Interfaces;
using LogAnalyzer.Core.Services.Connectivity;

namespace LogAnalyzer.Response.Collection
{
    public class PowerShellAuditCollector : IAuditCollector
    {
        /// <summary>The script's exit codes (AuditCollector.ps1 header): 0 = every section ran, 2 = at least one section failed, 1 = invalid parameters.</summary>
        public static string CompletionLine(int exitCode) => exitCode switch
        {
            0 => "[SUCCESS] Colectarea s-a încheiat: toate secțiunile au fost colectate.",
            2 => "[ATENȚIE] Colectarea s-a încheiat, dar cel puțin o secțiune a eșuat (cod 2). Vedeți rândurile „EROARE” de mai sus: acele date lipsesc din colectare.",
            1 => "[ERROR] Colectarea nu a pornit: parametri invalizi (cod 1). Vedeți mesajul de mai sus.",
            _ => $"[ERROR] Colectarea s-a oprit cu un cod neașteptat ({exitCode}). Datele pot fi incomplete.",
        };

        /// <summary>The host-name pattern of AuditCollector.ps1, enforced here too before anything is started (security audit #6).</summary>
        public const string HostnamePattern = "^[A-Za-z0-9][A-Za-z0-9._-]{0,62}$";

        private static readonly System.Text.RegularExpressions.Regex HostnameRegex =
            new(HostnamePattern, System.Text.RegularExpressions.RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

        private static readonly string[] TargetTypes = ["PC", "Server", "NAS", "DataCenter"];

        public static bool IsValidHostname(string? hostname) => hostname is not null && HostnameRegex.IsMatch(hostname);

        private static readonly Lazy<string> BuildSha = new(() =>
        {
            using var s = typeof(PowerShellAuditCollector).Assembly.GetManifestResourceStream("LogAnalyzer.Response.Scripts.AuditCollector.ps1");
            return s is null ? "" : Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(s));
        });

        /// <summary>SHA-256 of the collection script as it was when this assembly was built (embedded resource); empty if the resource is missing.</summary>
        public static string BuildScriptSha256 => BuildSha.Value;

        /// <summary>Windows PowerShell 5.1 by its fixed path (never from PATH).</summary>
        public static string PowerShellPath => Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");

        /// <summary>The collection script shipped next to the executable; the collector never looks anywhere else.</summary>
        public static string ScriptPath => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Scripts", "AuditCollector.ps1");

        private static readonly Lazy<string> BuildBlockSha = new(() =>
        {
            using var s = typeof(PowerShellAuditCollector).Assembly.GetManifestResourceStream("LogAnalyzer.Response.Scripts.AuditCollector.ps1");
            if (s is null) return "";
            using var r = new StreamReader(s, detectEncodingFromByteOrderMarks: true);
            return LogAnalyzer.Dfir.Auth.CollectorExec.ScriptSha256(r.ReadToEnd());
        });

        /// <summary>The script hash in the form "collector.exec" records it (text, line endings as "\n") of the script embedded at build time.</summary>
        public static string BuildScriptBlockSha256 => BuildBlockSha.Value;

        // "\z", not "$": a trailing line feed must not let a different command line match (finding B6)
        private static readonly System.Text.RegularExpressions.Regex ArgumentTail = new(
            @"^(PC|Server|NAS|DataCenter) -OutputDirectory (""(?:[^""\\]|\\.)*""|[^\s""]+) -Hostname ([A-Za-z0-9][A-Za-z0-9._-]{0,62})\z",
            System.Text.RegularExpressions.RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

        /// <summary>
        /// True only for the command line this collector emits (<see cref="BuildStartInfo"/>): <paramref name="powershell"/> with exactly these switches, the
        /// script <paramref name="scriptPath"/>, one of the target types, an output folder and a host name the collector accepts. Nothing before, between or after.
        /// </summary>
        public static bool RunsTheCollectionScript(string commandLine, string powershell, string scriptPath)
        {
            const string Marker = " -TargetType ";
            var sample = LogAnalyzer.Dfir.Auth.CollectorExec.CommandLineOf(BuildStartInfo(powershell, scriptPath, "PC", "x", "h"));
            var head = sample[..(sample.IndexOf(Marker, StringComparison.Ordinal) + Marker.Length)];
            return commandLine.StartsWith(head, StringComparison.Ordinal) && ArgumentTail.IsMatch(commandLine[head.Length..]);
        }

        /// <summary>
        /// Why the script at <paramref name="scriptPath"/> must not be started (null = it is byte for byte the script of the build). Checked before every start:
        /// a script changed after the build is refused, so the only script the application runs is the one whose hash is recorded in "collector.exec".
        /// </summary>
        public static string? ScriptRefusal(string scriptPath)
        {
            if (BuildScriptSha256.Length != 64) return "[ERROR] Amprenta scriptului de colectare nu există în aplicație (resursă lipsă); colectarea nu pornește.";
            string actual;
            try { actual = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(scriptPath))); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return $"[ERROR] Scriptul de colectare nu poate fi citit ({ex.Message}); colectarea nu pornește."; }
            return actual == BuildScriptSha256 ? null
                : $"[ERROR] Scriptul de colectare a fost modificat după construirea aplicației: amprenta SHA-256 a fișierului este {actual}, cea a versiunii livrate este {BuildScriptSha256}. Colectarea nu pornește; reinstalați pachetul.";
        }

        /// <summary>
        /// The process start for the collection script: every value is one element of <see cref="ProcessStartInfo.ArgumentList"/>, so .NET quotes it
        /// (a trailing backslash such as "E:\", spaces, quotes) and nothing typed by the operator can add or shift script parameters.
        /// </summary>
        public static ProcessStartInfo BuildStartInfo(string powershell, string scriptPath, string targetType, string outputDir, string hostname)
        {
            var psi = new ProcessStartInfo
            {
                FileName = powershell,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = System.Text.Encoding.UTF8,
                StandardErrorEncoding = System.Text.Encoding.UTF8,
                CreateNoWindow = true,
            };
            foreach (var a in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", scriptPath,
                                      "-TargetType", targetType, "-OutputDirectory", outputDir, "-Hostname", hostname })
                psi.ArgumentList.Add(a);
            return psi;
        }

        public async Task RunCollectionAsync(string targetType, string outputDir, string hostname, Action<string> logCallback)
        {
            await Task.Run(() =>
            {
                try
                {
                    if (!IsValidHostname(hostname))
                    {
                        logCallback("[ERROR] Hostname invalid: se acceptă doar litere, cifre, „.”, „_” și „-” (cel mult 63 de caractere, începe cu literă sau cifră). Colectarea nu a pornit.");
                        return;
                    }
                    if (!TargetTypes.Contains(targetType, StringComparer.Ordinal))
                    {
                        logCallback($"[ERROR] Tip de țintă necunoscut: „{targetType}” (PC, Server, NAS, DataCenter). Colectarea nu a pornit.");
                        return;
                    }
                    if (string.IsNullOrWhiteSpace(outputDir) || outputDir.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
                    {
                        logCallback("[ERROR] Folderul de destinație este gol sau conține caractere nepermise. Colectarea nu a pornit.");
                        return;
                    }
                    string scriptPath = ScriptPath;
                    
                    if (!File.Exists(scriptPath))
                    {
                        logCallback($"[ERROR] Scriptul de colectare (livrat în pachet, Scripts\\AuditCollector.ps1) nu a fost găsit la: {scriptPath}. Reinstalați pachetul; colectarea nu caută în alte locații.");
                        return;
                    }

                    if (ScriptRefusal(scriptPath) is { } refused)
                    {
                        logCallback(refused);
                        return;
                    }

                    logCallback($"[INIT] Pornire colectare pentru [{targetType}] pe host-ul [{hostname}]...");
                    logCallback($"[INIT] Script rulat: {scriptPath}");
                    logCallback($"[INIT] Destinație: {outputDir}");

                    // Windows PowerShell 5.1 by full path (never from PATH); it ships with every supported Windows version.
                    string powershell = PowerShellPath;
                    if (!File.Exists(powershell))
                    {
                        logCallback($"[ERROR] Windows PowerShell 5.1 indisponibil pe acest sistem ({powershell} lipsește); colectarea de audit nu poate rula.");
                        return;
                    }

                    var startInfo = BuildStartInfo(powershell, scriptPath, targetType, outputDir, hostname);

                    // through the one recording path, with the script's SHA-256 (its PowerShell 4104 block is then recognised as the application's)
                    using var process = LogAnalyzer.Dfir.Auth.CollectorExec.Start(startInfo, scriptPath);

                    process.OutputDataReceived += (s, e) =>
                    {
                        if (e.Data != null) logCallback(e.Data);
                    };

                    process.ErrorDataReceived += (s, e) =>
                    {
                        if (e.Data != null) logCallback($"[STDERR] {e.Data}");
                    };

                    process.BeginOutputReadLine();
                    process.BeginErrorReadLine();
                    process.WaitForExit();
                    LogAnalyzer.Dfir.Auth.CollectorExec.Done(process);

                    logCallback(CompletionLine(process.ExitCode));
                }
                catch (Exception ex)
                {
                    logCallback($"[ERROR] Excepție la rularea scriptului: {ex.Message}");
                }
            });
        }
    }
}
