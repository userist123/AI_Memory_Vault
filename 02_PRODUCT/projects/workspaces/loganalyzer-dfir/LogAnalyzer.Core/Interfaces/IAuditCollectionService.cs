using System;
using System.Threading.Tasks;

namespace LogAnalyzer.Core.Interfaces
{
    /// <summary>Runs the audit collection script on this station. Implemented in LogAnalyzer.Response (unclassified edition only).</summary>
    public interface IAuditCollector
    {
        Task RunCollectionAsync(string targetType, string outputDir, string hostname, Action<string> logCallback);
    }

    /// <summary>Syslog (UDP) receiver. Implemented in LogAnalyzer.Connectors (unclassified edition only); gated by NetworkPolicy.</summary>
    public interface ISyslogReceiver
    {
        void StartSyslogListener(int port, string outputDir, string hostname, Action<string> logCallback);
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
        public void StartSyslogListener(int port, string outputDir, string hostname, Action<string> logCallback) =>
            _syslog.StartSyslogListener(port, outputDir, hostname, logCallback);
        public void StopSyslogListener() => _syslog.StopSyslogListener();
        public bool IsSyslogListenerActive => _syslog.IsSyslogListenerActive;
    }
}
