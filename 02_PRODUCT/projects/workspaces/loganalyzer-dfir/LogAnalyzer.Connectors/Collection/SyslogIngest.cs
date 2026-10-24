using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace LogAnalyzer.Connectors.Collection
{
    /// <summary>
    /// Turns received datagrams into evidence lines (security audit #7), independent of the socket so it can be tested:
    /// source allow-list (empty = loopback only), one line per datagram with CR/LF/control characters escaped (a sender cannot forge
    /// "[time] [endpoint]" lines), message size cap, per-second rate cap for the file and for the UI, and a SHA-256 of the log computed over
    /// exactly the bytes written (sidecar &lt;log&gt;.sha256 when closed).
    /// </summary>
    public sealed class SyslogIngest : IDisposable
    {
        public const int MaxMessageBytes = 8 * 1024;
        public const int MaxDatagramsPerSecond = 500;
        public const int MaxUiLinesPerSecond = 20;

        private readonly string _path;
        private readonly HashSet<IPAddress> _allowed;
        private readonly Action<string> _ui;
        private readonly FileStream _file;
        private readonly IncrementalHash _hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        private long _second = long.MinValue;
        private int _inSecond, _uiInSecond, _uiSuppressed, _rateDroppedInSecond;
        private bool _disposed;

        public int Written { get; private set; }
        public int RejectedSources { get; private set; }
        public int RateDropped { get; private set; }

        public SyslogIngest(string logPath, IReadOnlyCollection<IPAddress> allowedSources, Action<string> uiLine)
        {
            _path = logPath;
            _allowed = new HashSet<IPAddress>(allowedSources);
            _ui = uiLine;
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(logPath))!);
            _file = new FileStream(logPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.Read);
            // An existing log of the same day is continued: its bytes are part of the hash too.
            var buffer = new byte[81920];
            for (int n; (n = _file.Read(buffer, 0, buffer.Length)) > 0;) _hash.AppendData(buffer, 0, n);
            _file.Seek(0, SeekOrigin.End);
        }

        public string Sha256Hex => Convert.ToHexString(_hash.GetCurrentHash());

        public bool IsAllowed(IPAddress source) =>
            _allowed.Count == 0 ? IPAddress.IsLoopback(source) : _allowed.Contains(source.IsIPv4MappedToIPv6 ? source.MapToIPv4() : source);

        /// <summary>Handles one datagram; true when it was written to the log.</summary>
        public bool Accept(DateTimeOffset nowUtc, IPEndPoint remote, byte[] data)
        {
            if (_disposed) return false;
            if (!IsAllowed(remote.Address)) { RejectedSources++; return false; }

            var second = nowUtc.ToUnixTimeSeconds();
            if (second != _second) RollWindow(second);
            if (_inSecond >= MaxDatagramsPerSecond) { RateDropped++; _rateDroppedInSecond++; return false; }
            _inSecond++;

            bool truncated = data.Length > MaxMessageBytes;
            var message = Escape(Encoding.UTF8.GetString(data, 0, truncated ? MaxMessageBytes : data.Length));
            if (truncated) message += $" [TRUNCHIAT: {data.Length} octeți primiți, păstrați {MaxMessageBytes}]";
            var line = $"[{nowUtc.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)}Z] [{remote}] {message}";
            var bytes = Encoding.UTF8.GetBytes(line + "\n");
            _file.Write(bytes);
            _file.Flush();
            _hash.AppendData(bytes);
            Written++;

            if (_uiInSecond < MaxUiLinesPerSecond) { _uiInSecond++; _ui($"[SYSLOG RECV] {line}"); }
            else _uiSuppressed++;
            return true;
        }

        private void RollWindow(long second)
        {
            Summarise();
            _second = second; _inSecond = 0; _uiInSecond = 0;
        }

        private void Summarise()
        {
            if (_uiSuppressed > 0 || _rateDroppedInSecond > 0)
                _ui($"[SYSLOG] {_uiSuppressed} mesaje scrise în jurnal dar neafișate; {_rateDroppedInSecond} datagrame ignorate (peste {MaxDatagramsPerSecond}/s).");
            _uiSuppressed = 0; _rateDroppedInSecond = 0;
        }

        /// <summary>Unambiguous one-line form: "\" → "\\", CR/LF/TAB → \r \n \t, every other control character → \xHH.</summary>
        public static string Escape(string s)
        {
            var sb = new StringBuilder(s.Length + 16);
            foreach (var c in s)
            {
                switch (c)
                {
                    case '\\': sb.Append(@"\\"); break;
                    case '\r': sb.Append(@"\r"); break;
                    case '\n': sb.Append(@"\n"); break;
                    case '\t': sb.Append(@"\t"); break;
                    default:
                        if (char.IsControl(c) || c == (char)0x2028 || c == (char)0x2029 || c == (char)0x0085) sb.Append(@"\x").Append(((int)c).ToString(c < 0x100 ? "X2" : "X4", CultureInfo.InvariantCulture));
                        else sb.Append(c);
                        break;
                }
            }
            return sb.ToString();
        }

        public void Dispose()
        {
            if (_disposed) return;
            Summarise();
            if (RejectedSources > 0) _ui($"[SYSLOG] {RejectedSources} datagrame de la surse care nu sunt în lista permisă au fost refuzate.");
            _disposed = true;
            _file.Flush(true);
            _file.Dispose();
            var hex = Sha256Hex;
            _hash.Dispose();
            File.WriteAllText(_path + ".sha256", $"{hex}  {Path.GetFileName(_path)}\n");
            _ui($"[SYSLOG] SHA-256 al jurnalului ({Written} mesaje): {hex}");
        }
    }
}
