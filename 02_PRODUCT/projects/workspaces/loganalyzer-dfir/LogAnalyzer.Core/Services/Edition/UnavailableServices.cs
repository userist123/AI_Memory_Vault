using System;
using System.Threading.Tasks;
using LogAnalyzer.Core.Interfaces;
using LogAnalyzer.Core.Services.Connectivity;

namespace LogAnalyzer.Core.Services.Edition
{
    // Registered by the classified edition in place of the capabilities that are not compiled into it. They contain no
    // networking and no host-modifying code: each one only states that the feature is not part of this edition.

    public sealed class UnavailableHostDefense : IHostDefense
    {
        private static DefenseActionResult No(string what) => new()
        {
            Success = false,
            Status = DefenseActionResult.Unavailable,
            Message = EditionText.Unavailable(what),
        };

        public DefenseActionResult IsolateHostFromNetwork() => No("Izolarea stației");
        public DefenseActionResult RestoreNetworkAccess() => No("Ridicarea izolării");
        public DefenseActionResult BlockMaliciousIoC(string iocTarget) => No("Blocarea IoC");
    }

    public sealed class UnavailableAuditCollector : IAuditCollector
    {
        public Task RunCollectionAsync(string targetType, string outputDir, string hostname, Action<string> logCallback)
        {
            logCallback("[INDISPONIBIL] " + EditionText.Unavailable("Colectarea prin script"));
            return Task.CompletedTask;
        }
    }

    public sealed class UnavailableSyslogReceiver : ISyslogReceiver
    {
        public bool IsSyslogListenerActive => false;
        public void StartSyslogListener(int port, string outputDir, string hostname, Action<string> logCallback) =>
            logCallback("[INDISPONIBIL] " + EditionText.Unavailable("Receptorul Syslog"));
        public void StopSyslogListener() { }
    }

    public sealed class NoLiveEventSourceFactory : ILiveEventSourceFactory
    {
        public ILiveEventSource? Create() => null;
    }

    public sealed class NoConnectivityWatcher : IConnectivityWatcher
    {
        private sealed class Nothing : IDisposable { public void Dispose() { } }
        public IDisposable Watch(Action<ConnectivitySnapshot> onChange) => new Nothing();
    }
}
