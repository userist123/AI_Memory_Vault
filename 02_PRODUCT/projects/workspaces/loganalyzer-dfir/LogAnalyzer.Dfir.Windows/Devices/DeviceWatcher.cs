using System.Management;
using System.Runtime.InteropServices;
using LogAnalyzer.Dfir.Devices;

namespace LogAnalyzer.Dfir.Windows.Devices;

/// <summary>One change seen live: a device arrived or left, or a medium (CD, ISO) was inserted or removed.</summary>
public sealed record DeviceChange(DateTimeOffset TimeUtc, bool Arrived, string InstanceId, string Name, DeviceKind? Kind, string Detail);

/// <summary>
/// Live notification without a driver (research §3): WMI events on Win32_PnPEntity (device arrival/removal, checked every 2 seconds) and
/// Win32_VolumeChangeEvent (a volume or medium appeared/disappeared: CD inserted, ISO mounted, stick letter assigned). <see cref="Changed"/> reports only
/// devices that <see cref="DeviceRules.Classify"/> keeps; <see cref="PnpChanged"/> reports every device of every class (the real-time device monitor).
/// Read-only; the station is never changed. Dispose to stop.
/// </summary>
public sealed class DeviceWatcher : IDisposable
{
    private readonly List<ManagementEventWatcher> _watchers = [];
    private volatile bool _disposing;
    public event Action<DeviceChange>? Changed;
    public event Action<string>? Failed;
    /// <summary>Any Plug and Play device arrived (true) or left (false), whatever its class: PNP device id, arrival.</summary>
    public event Action<string, bool>? PnpChanged;
    /// <summary>True when both Plug and Play event subscriptions started; false means the caller has to poll the device list.</summary>
    public bool PnpEventsActive { get; private set; }

    public void Start()
    {
        bool created = Watch("SELECT * FROM __InstanceCreationEvent WITHIN 2 WHERE TargetInstance ISA 'Win32_PnPEntity'", e => OnPnp(e, true));
        bool deleted = Watch("SELECT * FROM __InstanceDeletionEvent WITHIN 2 WHERE TargetInstance ISA 'Win32_PnPEntity'", e => OnPnp(e, false));
        PnpEventsActive = created && deleted;
        Watch("SELECT * FROM Win32_VolumeChangeEvent", OnVolume);
    }

    private bool Watch(string query, Action<ManagementBaseObject> handler)
    {
        try
        {
            var w = new ManagementEventWatcher(new WqlEventQuery(query));
            w.EventArrived += (_, a) =>
            {
                try { handler(a.NewEvent); }
                catch (Exception ex) when (ex is ManagementException or COMException or InvalidCastException) { Failed?.Invoke("Un eveniment de dispozitiv nu a putut fi citit: " + ex.Message); }
            };
            // the event session ended by itself (the WMI service stopped, the subscription broke): events are lost from here on
            w.Stopped += (_, _) => { if (!_disposing) Failed?.Invoke("Supravegherea în timp real s-a oprit (serviciul WMI sau abonamentul la evenimente a căzut): " + query.Split(' ')[3]); };
            w.Start();
            _watchers.Add(w);
            return true;
        }
        catch (Exception ex) when (ex is ManagementException or COMException or UnauthorizedAccessException)
        {
            Failed?.Invoke("Supravegherea în timp real nu a putut porni (" + query.Split(' ')[3] + "): " + ex.Message);
            return false;
        }
    }

    private void OnPnp(ManagementBaseObject e, bool arrived)
    {
        if (e["TargetInstance"] is not ManagementBaseObject t) return;
        var id = t["PNPDeviceID"]?.ToString() ?? "";
        var cls = t["PNPClass"]?.ToString() ?? "";
        var hw = t["HardwareID"] is string[] h ? string.Join(" ", h) : "";
        var name = t["Name"]?.ToString() ?? "";
        if (id.Length > 0) PnpChanged?.Invoke(id, arrived);
        var kind = DeviceRules.Classify(id, cls, hw, name);
        if (kind is null) return;
        Changed?.Invoke(new DeviceChange(DateTimeOffset.UtcNow, arrived, id, name, kind, arrived ? "conectat" : "deconectat"));
    }

    private void OnVolume(ManagementBaseObject e)
    {
        var type = Convert.ToInt32(e["EventType"] ?? 0);   // 2 = arrival, 3 = removal (1 = configuration changed, 4 = docking)
        if (type is not (2 or 3)) return;
        var drive = e["DriveName"]?.ToString() ?? "";
        Changed?.Invoke(new DeviceChange(DateTimeOffset.UtcNow, type == 2, "", drive, null, type == 2 ? $"unitatea {drive} a apărut (suport introdus sau montat)" : $"unitatea {drive} a dispărut"));
    }

    public void Dispose()
    {
        _disposing = true;
        foreach (var w in _watchers)
        {
            try { w.Stop(); } catch (Exception ex) when (ex is ManagementException or COMException) { }
            w.Dispose();
        }
        _watchers.Clear();
    }
}
