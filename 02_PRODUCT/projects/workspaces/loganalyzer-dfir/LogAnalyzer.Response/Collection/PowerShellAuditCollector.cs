using System;
using System.Diagnostics;
using System.IO;
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
        public async Task RunCollectionAsync(string targetType, string outputDir, string hostname, Action<string> logCallback)
        {
            await Task.Run(() =>
            {
                try
                {
                    string scriptPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Scripts", "AuditCollector.ps1");
                    
                    // Fallback în caz că folderul Scripts este în rădăcina proiectului, nu în bin
                    if (!File.Exists(scriptPath))
                    {
                        scriptPath = Path.Combine("C:\\Users\\user\\Desktop\\LogAnalyzer.MVP\\Scripts", "AuditCollector.ps1");
                    }

                    if (!File.Exists(scriptPath))
                    {
                        logCallback($"[ERROR] Scriptul de colectare nu a fost găsit la calea: {scriptPath}");
                        return;
                    }

                    logCallback($"[INIT] Pornire colectare pentru [{targetType}] pe host-ul [{hostname}]...");
                    logCallback($"[INIT] Script rulat: {scriptPath}");
                    logCallback($"[INIT] Destinație: {outputDir}");

                    var startInfo = new ProcessStartInfo
                    {
                        FileName = "powershell.exe",
                        Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{scriptPath}\" -TargetType \"{targetType}\" -OutputDirectory \"{outputDir}\" -Hostname \"{hostname}\"",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    };

                    using var process = new Process { StartInfo = startInfo };
                    
                    process.OutputDataReceived += (s, e) =>
                    {
                        if (e.Data != null) logCallback(e.Data);
                    };

                    process.ErrorDataReceived += (s, e) =>
                    {
                        if (e.Data != null) logCallback($"[STDERR] {e.Data}");
                    };

                    process.Start();
                    process.BeginOutputReadLine();
                    process.BeginErrorReadLine();
                    process.WaitForExit();

                    logCallback($"[SUCCESS] Procesul de colectare s-a încheiat cu codul de ieșire: {process.ExitCode}");
                }
                catch (Exception ex)
                {
                    logCallback($"[ERROR] Excepție la rularea scriptului: {ex.Message}");
                }
            });
        }
    }
}
