using System.IO;
using System.Text.RegularExpressions;
using LogAnalyzer.UI.Services;
using Xunit;

namespace LogAnalyzer.App.Tests;

/// <summary>
/// Security audit #15: the startup logs live in the per-user LocalAppData\LogAnalyzer\logs folder (not next to the shared executable) and every
/// line is redacted (Windows user, machine and domain names, the user segment of profile paths). An unhandled UI exception is handled only for
/// known, recoverable types; anything else is logged and the application closes (fail closed).
/// </summary>
public sealed class StartupLogTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "la_slog_" + Guid.NewGuid().ToString("N"));
    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }

    [Fact]
    public void Default_folder_is_per_user_local_app_data()
    {
        var expected = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LogAnalyzer", "logs");
        Assert.Equal(expected, StartupLog.DefaultDirectory);
        Assert.False(StartupLog.DefaultDirectory.StartsWith(AppContext.BaseDirectory, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Redaction_removes_user_machine_domain_and_profile_segments()
    {
        var text = @"System.IO.IOException: C:\Users\jdoe.smith\AppData\Local\x.db locked; profile c:/users/Other/Desktop/a.txt; " +
                   @"host LAB-PC07 (lab-pc07.corp.local) account CORPDOM\jdoe.smith; user=jdoe.smith";
        var r = StartupLog.Redact(text, user: "jdoe.smith", machine: "LAB-PC07", domain: "CORPDOM");
        Assert.DoesNotContain("jdoe", r, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("LAB-PC07", r, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("CORPDOM", r, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Other", r);
        Assert.Contains(@"C:\Users\<utilizator>\AppData\Local\x.db", r);
        Assert.Contains("c:/users/<utilizator>/Desktop/a.txt", r);
        Assert.Contains("<stație>", r);
        Assert.Contains("System.IO.IOException", r);                // the useful part stays
    }

    [Fact]
    public void Redaction_does_not_mangle_words_that_merely_contain_a_short_name()
    {
        var r = StartupLog.Redact("Database initialized for al; also: normal text", user: "al", machine: "PC", domain: "");
        Assert.Contains("Database initialized for <utilizator>;", r);
        Assert.Contains("normal text", r);
    }

    [Fact]
    public void Append_writes_redacted_lines_into_the_given_folder()
    {
        var path = StartupLog.PathIn(_dir, "startup_debug.log");
        StartupLog.Append(path, "starting as " + Environment.UserName + " on " + Environment.MachineName + "\n", reset: true);
        var text = File.ReadAllText(path);
        Assert.DoesNotContain(Environment.MachineName, text, StringComparison.OrdinalIgnoreCase);
        if (Environment.UserName.Length >= 2) Assert.DoesNotContain(Environment.UserName, text, StringComparison.OrdinalIgnoreCase);
        Assert.StartsWith(_dir, path);
    }

    public static IEnumerable<object[]> Known() =>
    [
        [new IOException("disk")], [new FileNotFoundException("f")], [new UnauthorizedAccessException("acl")], [new OperationCanceledException()],
        [new RegexMatchTimeoutException("x", "y", TimeSpan.FromMilliseconds(1))], [new InvalidDataException("bad file")],
        [new LogAnalyzer.Core.Services.Connectivity.NetworkBlockedException("AirGapped")],
        [new LogAnalyzer.Dfir.Auth.HostChangeNotAuthorizedException("role")],
        [new AggregateException(new IOException("a"), new UnauthorizedAccessException("b"))],
    ];

    public static IEnumerable<object[]> Unknown() =>
    [
        [new NullReferenceException()], [new InvalidOperationException("state")], [new ArgumentException("x")], [new OutOfMemoryException()],
        [new AggregateException(new IOException("a"), new NullReferenceException())], [new Exception("?")],
    ];

    [Theory]
    [MemberData(nameof(Known))]
    public void Known_recoverable_exceptions_keep_the_application_running(Exception ex) =>
        Assert.Equal(UnhandledDecision.ContinueWithMessage, StartupLog.Classify(ex));

    [Theory]
    [MemberData(nameof(Unknown))]
    public void Any_other_exception_closes_the_application(Exception ex) =>
        Assert.Equal(UnhandledDecision.CloseApplication, StartupLog.Classify(ex));

    [Fact]
    public void The_closing_message_is_romanian_and_names_the_log_folder_without_the_user()
    {
        var m = StartupLog.CloseMessage;
        Assert.Contains("se închide", m);
        Assert.Contains(@"%LOCALAPPDATA%\LogAnalyzer\logs", m);
    }
}
