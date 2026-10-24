using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using LogAnalyzer.Core.Interfaces;
using LogAnalyzer.Core.Services.Connectivity;

namespace LogAnalyzer.Connectors.Collection
{
    /// <summary>
    /// UDP syslog receiver (unclassified edition, network mode only). Security audit #7: listens on ONE chosen local interface (never all), accepts
    /// only allow-listed sources (empty list = loopback senders only), validates the host name used in the output path, and writes every datagram
    /// through <see cref="SyslogIngest"/> (escaped single line, size/rate caps, UI throttling, SHA-256 of the log as written).
    /// </summary>
    public class UdpSyslogReceiver : ISyslogReceiver
    {
        private static readonly Regex HostnameFormat = new("^[A-Za-z0-9][A-Za-z0-9._-]{0,62}$", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

        private UdpClient? _udpListener;
        private CancellationTokenSource? _syslogCts;
        private volatile bool _isActive;

        public bool IsSyslogListenerActive => _isActive;

        /// <summary>Null when the options are acceptable, otherwise the reason (Romanian), before any socket or folder is created.</summary>
        public static string? Validate(SyslogListenerOptions o)
        {
            if (o.Port is < 1 or > 65535) return "portul trebuie să fie între 1 și 65535";
            if (string.IsNullOrWhiteSpace(o.OutputDir)) return "alegeți folderul de destinație";
            if (string.IsNullOrEmpty(o.Hostname) || !HostnameFormat.IsMatch(o.Hostname) || o.Hostname.Contains("..", StringComparison.Ordinal))
                return "numele stației este invalid: doar litere, cifre, „.”, „_” și „-”, fără „..” și fără separatori de cale";
            if (o.BindAddress is null || o.BindAddress.Equals(IPAddress.Any) || o.BindAddress.Equals(IPAddress.IPv6Any) || o.BindAddress.Equals(IPAddress.Broadcast))
                return "alegeți o singură interfață locală pe care să asculte receptorul (nu toate interfețele)";
            if (!IPAddress.IsLoopback(o.BindAddress) && !IsLocalAddress(o.BindAddress))
                return $"adresa {o.BindAddress} nu aparține unei interfețe a acestei stații; alegeți o interfață locală";
            return null;
        }

        private static bool IsLocalAddress(IPAddress a)
        {
            try
            {
                return NetworkInterface.GetAllNetworkInterfaces()
                    .SelectMany(n => n.GetIPProperties().UnicastAddresses).Any(u => u.Address.Equals(a));
            }
            catch (NetworkInformationException) { return false; }
        }

        public void StartSyslogListener(int port, string outputDir, string hostname, Action<string> logCallback) =>
            StartSyslogListener(new SyslogListenerOptions(port, outputDir, hostname, IPAddress.Loopback, Array.Empty<IPAddress>()), logCallback);

        public void StartSyslogListener(SyslogListenerOptions options, Action<string> logCallback)
        {
            // Opening a UDP listener exposes the station on the network: refused in AirGapped mode.
            NetworkPolicy.EnsureAllowed("Receptor Syslog (UDP)");

            if (_isActive)
            {
                logCallback("[WARN] Ascultătorul Syslog este deja activ.");
                return;
            }
            if (Validate(options) is { } why)
            {
                logCallback("[SYSLOG ERROR] Receptorul nu a pornit: " + why + ".");
                return;
            }

            _syslogCts = new CancellationTokenSource();
            _isActive = true;
            var token = _syslogCts.Token;

            Task.Run(async () =>
            {
                SyslogIngest? ingest = null;
                try
                {
                    _udpListener = new UdpClient(new IPEndPoint(options.BindAddress, options.Port));
                    var sources = options.AllowedSources.Count == 0 ? "doar loopback" : string.Join(", ", options.AllowedSources);
                    logCallback($"[SYSLOG] Ascultător UDP pornit pe {options.BindAddress}:{options.Port} pentru host [{options.Hostname}]; surse acceptate: {sources}.");

                    // [OutputDir]\[Month-Year]\DC_[Hostname]; the host name was validated above and the result must stay under OutputDir.
                    var root = Path.GetFullPath(options.OutputDir);
                    var targetFolder = Path.GetFullPath(Path.Combine(root, DateTime.UtcNow.ToString("MM-yyyy"), $"DC_{options.Hostname}"));
                    if (!targetFolder.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException("calea jurnalului iese din folderul de destinație");
                    var logFilePath = Path.Combine(targetFolder, $"DC_{options.Hostname}_Syslog_{DateTime.UtcNow:dd-MM-yyyy}.log");
                    ingest = new SyslogIngest(logFilePath, options.AllowedSources, logCallback);
                    logCallback($"[SYSLOG] Jurnalele vor fi salvate în: {logFilePath}");

                    while (!token.IsCancellationRequested)
                    {
                        var result = await _udpListener.ReceiveAsync(token);
                        ingest.Accept(DateTimeOffset.UtcNow, result.RemoteEndPoint, result.Buffer);
                    }
                }
                catch (OperationCanceledException)
                {
                    logCallback("[SYSLOG] Ascultătorul a fost oprit.");
                }
                catch (Exception ex) when (ex is SocketException or IOException or UnauthorizedAccessException or InvalidOperationException or ObjectDisposedException)
                {
                    logCallback($"[SYSLOG ERROR] {ex.Message}");
                }
                finally
                {
                    try { ingest?.Dispose(); } catch (IOException ex) { logCallback($"[SYSLOG ERROR] jurnalul nu a putut fi închis: {ex.Message}"); }
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
