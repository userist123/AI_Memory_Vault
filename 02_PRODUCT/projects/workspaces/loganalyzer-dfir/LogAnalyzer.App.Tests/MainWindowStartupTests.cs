using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Windows;
using LogAnalyzer.UI.Views;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Abstractions;

namespace LogAnalyzer.App.Tests;

/// <summary>
/// The main window is built with the application's real service registrations (AppComposition.BuildServices), as startup does after sign-in. Added after a
/// wrong resource key in MainWindow.xaml (2026-10-11) would have crashed the application on opening the main window while every other test passed.
/// The timings are written to the test output for the startup-speed work; they are not asserted (machine dependent).
/// </summary>
[Collection("Global authentication state")]
public sealed class MainWindowStartupTests(ITestOutputHelper output)
{
    [Fact]
    public void The_main_window_builds_with_the_real_services()
    {
        // Isolation (2026-10-14): the test used to write the machine's real auth store (it created the audit key and resealed the real chain) and
        // the real %LOCALAPPDATA%\LogAnalyzer database. It now runs against a temporary folder and checks the real folders were not touched.
        var machineAuth = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "LogAnalyzer", "auth");
        var machineData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LogAnalyzer");
        var authBefore = Snapshot(machineAuth, recursive: true);
        var dataBefore = Snapshot(machineData, recursive: false);
        var temp = Path.Combine(Path.GetTempPath(), "la-startup-" + Guid.NewGuid().ToString("N"));
        try
        {
            Run(Path.Combine(temp, "appdata"), Path.Combine(temp, "auth"));
            Assert.True(File.Exists(Path.Combine(temp, "appdata", "LogAnalyzer.db")), "the database was created in the temporary folder");
        }
        finally { try { Directory.Delete(temp, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
        Assert.Equal(authBefore, Snapshot(machineAuth, recursive: true));
        Assert.Equal(dataBefore, Snapshot(machineData, recursive: false));
    }

    /// <summary>Every file and folder under <paramref name="root"/> with its size and last-write time; empty when the folder does not exist.</summary>
    private static List<string> Snapshot(string root, bool recursive)
    {
        if (!Directory.Exists(root)) return [];
        var option = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        var rows = new List<string> { $"<root> {Directory.GetLastWriteTimeUtc(root):O}" };
        foreach (var d in Directory.EnumerateDirectories(root, "*", option)) rows.Add($"{Path.GetRelativePath(root, d)}/ {Directory.GetLastWriteTimeUtc(d):O}");
        foreach (var f in Directory.EnumerateFiles(root, "*", option))
        {
            var info = new FileInfo(f);
            rows.Add($"{Path.GetRelativePath(root, f)} {info.Length} {info.LastWriteTimeUtc:O}");
        }
        rows.Sort(StringComparer.Ordinal);
        return rows;
    }

    private void Run(string appData, string authStore)
    {
        Wp18AccessibilityTests.OnSta(() =>
        {
            var required = LogAnalyzer.Dfir.Auth.OperatorIdentity.AuthenticationRequired;
            try
            {
                var sw = Stopwatch.StartNew();
                var provider = LogAnalyzer.UI.AppComposition.BuildServices(appData).BuildServiceProvider();
                provider.GetRequiredService<LogAnalyzer.Core.Interfaces.IDatabaseService>().InitializeDatabase();
                LogAnalyzer.UI.Services.AuthApp.Initialize(authStore);
                Assert.Equal(Path.GetFullPath(authStore), Path.GetFullPath(LogAnalyzer.UI.Services.AuthApp.Service.Dir));
                var services = sw.ElapsedMilliseconds; sw.Restart();
                var vm = provider.GetRequiredService<LogAnalyzer.UI.ViewModels.MainViewModel>();
                var model = sw.ElapsedMilliseconds; sw.Restart();
                var window = new MainWindow(vm);
                window.Measure(new Size(1400, 900)); window.Arrange(new Rect(0, 0, 1400, 900)); window.UpdateLayout();
                output.WriteLine($"services + database: {services} ms · MainViewModel: {model} ms · MainWindow: {sw.ElapsedMilliseconds} ms");
                Assert.NotNull(window.Content);
            }
            finally { LogAnalyzer.Dfir.Auth.OperatorIdentity.AuthenticationRequired = required; }
            return 0;
        });
    }
}

/// <summary>Tests that change process-wide authentication state run alone.</summary>
[CollectionDefinition("Global authentication state", DisableParallelization = true)]
public sealed class GlobalAuthenticationStateCollection;
