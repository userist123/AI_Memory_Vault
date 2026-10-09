using System;
using LogAnalyzer.Core.Models;

namespace LogAnalyzer.Core.Interfaces
{
    /// <summary>Real-time event-log subscription (local or remote host). Implemented in LogAnalyzer.Connectors; absent from the classified edition.</summary>
    public interface ILiveEventSource : IDisposable
    {
        event Action<ParsedEvent>? OnEventReceived;
        event Action<string>? OnStatusChanged;
        event Action<Exception>? OnErrorOccurred;
        bool IsRunning { get; }
        void StartWatching(string? remoteHost = null);
        void StopWatching();
    }

    public interface ILiveEventSourceFactory
    {
        /// <summary>Null when the edition has no live monitoring.</summary>
        ILiveEventSource? Create();
    }

    /// <summary>
    /// Passive watcher that reports when an isolated station gains a network. Implemented in LogAnalyzer.Connectors;
    /// the classified edition contains no network code and registers a no-op.
    /// </summary>
    public interface IConnectivityWatcher
    {
        IDisposable Watch(Action<LogAnalyzer.Core.Services.Connectivity.ConnectivitySnapshot> onChange);
    }
}
