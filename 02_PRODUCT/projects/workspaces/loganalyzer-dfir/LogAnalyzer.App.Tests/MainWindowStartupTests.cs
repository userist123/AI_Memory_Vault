using System.Diagnostics;
using System.Windows;
using LogAnalyzer.UI.Views;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Abstractions;

namespace LogAnalyzer.App.Tests;

/// <summary>
/// The main window is built with the application's real service registrations (App.BuildServices), as startup does after sign-in. Added after a
/// wrong resource key in MainWindow.xaml (2026-10-11) would have crashed the application on opening the main window while every other test passed.
/// The timings are written to the test output for the startup-speed work; they are not asserted (machine dependent).
/// </summary>
[Collection("Global authentication state")]
public sealed class MainWindowStartupTests(ITestOutputHelper output)
{
    [Fact]
    public void The_main_window_builds_with_the_real_services()
    {
        Wp18AccessibilityTests.OnSta(() =>
        {
            var required = LogAnalyzer.Dfir.Auth.OperatorIdentity.AuthenticationRequired;
            try
            {
                var sw = Stopwatch.StartNew();
                var provider = LogAnalyzer.UI.App.BuildServices().BuildServiceProvider();
                provider.GetRequiredService<LogAnalyzer.Core.Interfaces.IDatabaseService>().InitializeDatabase();
                LogAnalyzer.UI.Services.AuthApp.Initialize();
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
