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

namespace LogAnalyzer.Connectors.Collection
{
    public class UdpSyslogReceiver : ISyslogReceiver
    {
        private UdpClient? _udpListener;
        private CancellationTokenSource? _syslogCts;
        private bool _isActive;

        public bool IsSyslogListenerActive => _isActive;

        public void StartSyslogListener(int port, string outputDir, string hostname, Action<string> logCallback)
        {
            // Opening a UDP listener exposes the station on the network: refused in AirGapped mode.
            NetworkPolicy.EnsureAllowed("Receptor Syslog (UDP)");

            if (_isActive)
            {
                logCallback("[WARN] Ascultătorul Syslog este deja activ.");
                return;
            }

            _syslogCts = new CancellationTokenSource();
            _isActive = true;
            
            var token = _syslogCts.Token;

            Task.Run(async () =>
            {
                try
                {
                    _udpListener = new UdpClient(port);
                    logCallback($"[SYSLOG] Ascultător UDP pornit pe portul {port} pentru host [{hostname}]...");
                    
                    // Pregătim folderul conform structurii: [OutputDir]\[Month-Year]\[Prefix]_[Hostname]
                    string monthFolder = DateTime.Now.ToString("MM-yyyy");
                    string targetFolder = Path.Combine(outputDir, monthFolder, $"DC_{hostname}");
                    Directory.CreateDirectory(targetFolder);
                    
                    string logFileName = $"DC_{hostname}_Syslog_{DateTime.Now:dd-MM-yyyy}.log";
                    string logFilePath = Path.Combine(targetFolder, logFileName);
                    
                    logCallback($"[SYSLOG] Jurnalele vor fi salvate în: {logFilePath}");

                    while (!token.IsCancellationRequested)
                    {
                        var result = await _udpListener.ReceiveAsync(token);
                        string message = Encoding.UTF8.GetString(result.Buffer);
                        string logEntry = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{result.RemoteEndPoint}] {message}";
                        
                        // Scriere imediată în fișier (append)
                        await File.AppendAllTextAsync(logFilePath, logEntry + Environment.NewLine, token);
                        
                        logCallback($"[SYSLOG RECV] {logEntry}");
                    }
                }
                catch (OperationCanceledException)
                {
                    logCallback("[SYSLOG] Ascultătorul a fost oprit.");
                }
                catch (Exception ex)
                {
                    logCallback($"[SYSLOG ERROR] {ex.Message}");
                }
                finally
                {
                    _isActive = false;
                    _udpListener?.Close();
                    _udpListener = null;
                }
            }, token);
        }

        public void StopSyslogListener()
        {
            _syslogCts?.Cancel();
            _udpListener?.Close();
            _isActive = false;
        }
    }
}
