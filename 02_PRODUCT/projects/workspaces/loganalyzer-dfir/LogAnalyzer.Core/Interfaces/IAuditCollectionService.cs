using System;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;

namespace LogAnalyzer.Core.Interfaces
{
    /// <summary>Runs the audit collection script on this station. Implemented in LogAnalyzer.Response (unclassified edition only).</summary>
    public interface IAuditCollector
    {
        Task RunCollectionAsync(string targetType, string outputDir, string hostname, Action<string> logCallback);
    }

    /// <summary>
    /// Where the syslog receiver listens and whom it accepts (security audit #7): one local interface (never all of them) and an allow-list of
    /// source addresses; an empty list accepts loopback senders only.
    /// </summary>
    public sealed record SyslogListenerOptions(int Port, string OutputDir, string Hostname, IPAddress BindAddress, IReadOnlyCollection<IPAddress> AllowedSources);

    /// <summary>Syslog (UDP) receiver. Implemented in LogAnalyzer.Connectors (unclassified edition only); gated by NetworkPolicy.</summary>
    public interface ISyslogReceiver
    {
        void StartSyslogListener(SyslogListenerOptions options, Action<string> logCallback);
        /// <summary>Loopback only (no allow-list): the safe default when no interface was chosen.</summary>
        void StartSyslogListener(int port, string outputDir, string hostname, Action<string> logCallback) =>
            StartSyslogListener(new SyslogListenerOptions(port, outputDir, hostname, IPAddress.Loopback, Array.Empty<IPAddress>()), logCallback);
        void StopSyslogListener();
        bool IsSyslogListenerActive { get; }
    }

    /// <summary>What the UI sees: both capabilities behind one contract; each edition supplies its own parts.</summary>
    public interface IAuditCollectionService : IAuditCollector, ISyslogReceiver
    {
    }

    /// <summary>Delegates to the edition's collector and receiver.</summary>
    public sealed class AuditCollectionService : IAuditCollectionService
    {
        private readonly IAuditCollector _collector;
        private readonly ISyslogReceiver _syslog;

        public AuditCollectionService(IAuditCollector collector, ISyslogReceiver syslog)
        {
            _collector = collector;
            _syslog = syslog;
        }

        public Task RunCollectionAsync(string targetType, string outputDir, string hostname, Action<string> logCallback) =>
            _collector.RunCollectionAsync(targetType, outputDir, hostname, logCallback);
        public void StartSyslogListener(SyslogListenerOptions options, Action<string> logCallback) =>
            _syslog.StartSyslogListener(options, logCallback);
        public void StartSyslogListener(int port, string outputDir, string hostname, Action<string> logCallback) =>
            _syslog.StartSyslogListener(port, outputDir, hostname, logCallback);
        public void StopSyslogListener() => _syslog.StopSyslogListener();
        public bool IsSyslogListenerActive => _syslog.IsSyslogListenerActive;
    }
}
