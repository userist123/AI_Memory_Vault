namespace LogAnalyzer.Dfir.Devices;

/// <summary>Whether the pop-up of an alert is shown now, or why it is not (the alert itself is always recorded and sealed).</summary>
public enum AlertGate { Show, SuppressedSameDevice, SuppressedGlobal }

/// <summary>
/// Rate limit of the alert pop-ups (finding F6): per device and alert kind at most one pop-up every 10 minutes, and a global cap (20 pop-ups per minute) so a
/// flood of devices cannot bury the operator. Suppression only affects the pop-up: the journal and the sealed audit get a record of every alert, critical ones
/// included, and <see cref="Suppressed"/> counts what was held back (shown on the page as „N alerte suprimate”).
/// </summary>
public sealed class AlertRateLimiter
{
    public static readonly TimeSpan DefaultPerDevice = TimeSpan.FromMinutes(10);
    public const int DefaultGlobalPerMinute = 20;

    private readonly TimeSpan _perDevice;
    private readonly int _globalCap;
    private readonly Dictionary<string, DateTimeOffset> _lastShown = new(StringComparer.OrdinalIgnoreCase);
    private readonly Queue<DateTimeOffset> _shown = new();
    private readonly object _gate = new();

    public AlertRateLimiter(TimeSpan? perDevice = null, int globalPerMinute = DefaultGlobalPerMinute)
    {
        _perDevice = perDevice ?? DefaultPerDevice;
        _globalCap = globalPerMinute;
    }

    /// <summary>How many pop-ups were held back since the start (or the last <see cref="ResetCounter"/>).</summary>
    public int Suppressed { get; private set; }

    public void ResetCounter() { lock (_gate) Suppressed = 0; }

    /// <summary>Decides the pop-up of one alert of <paramref name="kind"/> (severity and reason) for the device <paramref name="identity"/> at <paramref name="now"/>.</summary>
    public AlertGate Check(string identity, string kind, DateTimeOffset now)
    {
        lock (_gate)
        {
            var key = identity + "|" + kind;
            if (_lastShown.TryGetValue(key, out var last) && now - last < _perDevice) { Suppressed++; return AlertGate.SuppressedSameDevice; }
            while (_shown.Count > 0 && now - _shown.Peek() >= TimeSpan.FromMinutes(1)) _shown.Dequeue();
            if (_shown.Count >= _globalCap) { Suppressed++; return AlertGate.SuppressedGlobal; }
            _shown.Enqueue(now);
            _lastShown[key] = now;
            if (_lastShown.Count > 4096)
                foreach (var old in _lastShown.Where(kv => now - kv.Value >= _perDevice).Select(kv => kv.Key).ToList()) _lastShown.Remove(old);
            return AlertGate.Show;
        }
    }
}
