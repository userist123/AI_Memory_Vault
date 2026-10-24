using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace LogAnalyzer.UI.Services
{
    /// <summary>What the UI does with an exception nobody caught.</summary>
    public enum UnhandledDecision { ContinueWithMessage, CloseApplication }

    /// <summary>
    /// Security audit #15. Startup diagnostics (<c>startup_debug.log</c>, <c>startup_crash.log</c>) go to the per-user
    /// <c>%LOCALAPPDATA%\LogAnalyzer\logs</c> folder, never next to the shared executable, and every line is redacted: the Windows user, machine
    /// and domain names and the user segment of profile paths are replaced by placeholders. <see cref="Classify"/> is the policy of the
    /// dispatcher's unhandled-exception handler: known, recoverable failures (I/O, access, cancellation, regex timeout, invalid input file,
    /// blocked network, refused host change) keep the application running; anything else closes it (fail closed).
    /// </summary>
    public static class StartupLog
    {
        public const string UserPlaceholder = "<utilizator>", MachinePlaceholder = "<stație>", DomainPlaceholder = "<domeniu>";

        public static string DefaultDirectory =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LogAnalyzer", "logs");

        public const string CloseMessage =
            "Eroare internă neașteptată. Aplicația se închide, pentru a nu continua într-o stare necunoscută (datele cazului deja scrise rămân pe disc). " +
            "Detaliile tehnice sunt în jurnalul de pornire din %LOCALAPPDATA%\\LogAnalyzer\\logs. Reporniți aplicația; dacă eroarea se repetă, trimiteți jurnalul administratorului.";

        /// <summary>The log folder, created; null when it cannot be created (nothing is logged then, and nothing is written next to the executable).</summary>
        public static string? EnsureDirectory(string? root = null)
        {
            var dir = root ?? DefaultDirectory;
            try { Directory.CreateDirectory(dir); return dir; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) { return null; }
        }

        /// <summary>Full path of <paramref name="file"/> in <paramref name="dir"/> (created); null when <paramref name="dir"/> is null or cannot be created.</summary>
        public static string? PathIn(string? dir, string file) => EnsureDirectory(dir) is { } d ? Path.Combine(d, file) : null;

        /// <summary>Appends (or, with <paramref name="reset"/>, rewrites) one redacted entry. Best effort: a log that cannot be written is skipped.</summary>
        public static void Append(string? path, string text, bool reset = false)
        {
            if (path is null) return;
            try
            {
                var redacted = Redact(text);
                if (reset) File.WriteAllText(path, redacted); else File.AppendAllText(path, redacted);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }

        private static readonly Regex ProfilePath = new(@"(?<prefix>[A-Za-z]:[\\/]+users[\\/]+)(?<user>[^\\/\r\n""'<>|:*?]+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

        /// <summary>Redacts with the current Windows identity.</summary>
        public static string Redact(string text) => Redact(text, SafeEnv(() => Environment.UserName), SafeEnv(() => Environment.MachineName), SafeEnv(() => Environment.UserDomainName));

        /// <summary>Replaces the user segment of profile paths and every whole-token occurrence of the user, machine and domain names.</summary>
        public static string Redact(string text, string? user, string? machine, string? domain)
        {
            if (string.IsNullOrEmpty(text)) return text ?? "";
            var r = ProfilePath.Replace(text, m => m.Groups["prefix"].Value + UserPlaceholder);
            // Machine first (a domain can equal the machine name on a workgroup station), then domain, then user.
            r = Token(r, machine, MachinePlaceholder);
            if (!string.Equals(domain, machine, StringComparison.OrdinalIgnoreCase)) r = Token(r, domain, DomainPlaceholder);
            r = Token(r, user, UserPlaceholder);
            return r;
        }

        private static string Token(string text, string? value, string placeholder)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Trim().Length < 2) return text;
            var rx = new Regex($@"(?<![\p{{L}}\p{{N}}_]){Regex.Escape(value.Trim())}(?![\p{{L}}\p{{N}}_])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
            try { return rx.Replace(text, placeholder); }
            catch (RegexMatchTimeoutException) { return "[jurnal omis: redactarea nu s-a putut aplica]"; }
        }

        private static string? SafeEnv(Func<string> read) { try { return read(); } catch (Exception ex) when (ex is InvalidOperationException or PlatformNotSupportedException) { return null; } }

        /// <summary>Known, recoverable failure types; an <see cref="AggregateException"/> counts only when every inner exception is known.</summary>
        public static UnhandledDecision Classify(Exception ex) => IsKnown(ex) ? UnhandledDecision.ContinueWithMessage : UnhandledDecision.CloseApplication;

        private static bool IsKnown(Exception ex) => ex switch
        {
            AggregateException a => a.Flatten().InnerExceptions.Count > 0 && a.Flatten().InnerExceptions.All(IsKnown),
            System.Reflection.TargetInvocationException { InnerException: { } inner } => IsKnown(inner),
            IOException or UnauthorizedAccessException or OperationCanceledException or RegexMatchTimeoutException or InvalidDataException => true,
            LogAnalyzer.Core.Services.Connectivity.NetworkBlockedException => true,
            LogAnalyzer.Dfir.Auth.HostChangeNotAuthorizedException => true,
            _ => false,
        };
    }
}
