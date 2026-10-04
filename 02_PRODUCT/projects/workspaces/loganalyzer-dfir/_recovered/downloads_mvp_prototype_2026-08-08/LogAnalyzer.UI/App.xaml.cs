using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace LogAnalyzer.UI
{
    public partial class App : Application
    {
        private static readonly string LogPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "crash_log.txt");

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
            DispatcherUnhandledException += OnDispatcherUnhandledException;
            TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
        }

        private void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            LogEroareCritica("AppDomain.UnhandledException", e.ExceptionObject as Exception);
        }

        private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            LogEroareCritica("DispatcherUnhandledException", e.Exception);

            MessageBox.Show(
                "A aparut o eroare neasteptata, dar aplicatia va continua sa functioneze.\n\nDetalii au fost salvate in crash_log.txt.",
                "LogAnalyzer.MVP - Eroare gestionata",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);

            e.Handled = true;
        }

        private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
        {
            LogEroareCritica("TaskScheduler.UnobservedTaskException", e.Exception);
            e.SetObserved();
        }

        private static void LogEroareCritica(string sursa, Exception? ex)
        {
            try
            {
                var mesaj = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{sursa}]\n{ex}\n{new string('-', 60)}\n";
                File.AppendAllText(LogPath, mesaj);
            }
            catch { }
        }
    }
}
