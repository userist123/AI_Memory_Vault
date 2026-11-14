namespace LogAnalyzer.Dfir.Windows.Acquisition;

/// <summary>Creates, counts and deletes Volume Shadow Copies through WMI Win32_ShadowCopy (ClientAccessible). Shared by the collectors that read from one shadow copy.</summary>
internal sealed class WmiShadowCopy : WindowsSearchIndexCollector.ISnapshot
{
    public (string Id, string DeviceObject) Create(string volumeRoot)
    {
        using var cls = new System.Management.ManagementClass(@"\\.\root\cimv2", "Win32_ShadowCopy", null);
        using var args = cls.GetMethodParameters("Create");
        args["Volume"] = volumeRoot; args["Context"] = "ClientAccessible";
        using var res = cls.InvokeMethod("Create", args, null);
        var rc = Convert.ToUInt32(res["ReturnValue"]);
        if (rc != 0) throw new InvalidOperationException($"Win32_ShadowCopy.Create a întors {rc}");
        var id = (string)res["ShadowID"];
        try
        {
            using var s = new System.Management.ManagementObjectSearcher(@"\\.\root\cimv2", $"SELECT DeviceObject FROM Win32_ShadowCopy WHERE ID='{id.Replace("'", "")}'");
            using var col = s.Get();
            foreach (System.Management.ManagementObject m in col) using (m) if (m["DeviceObject"] is string dev && dev.Length > 0) return (id, dev);
        }
        catch (Exception) { TryDeleteOrphan(id); throw; }
        // created but not found: delete it here, the caller never learns its id (a ClientAccessible copy is persistent and would stay behind)
        var deleted = TryDeleteOrphan(id);
        throw new InvalidOperationException($"copia de umbră {id} a fost creată, dar nu poate fi găsită" + (deleted ? "; a fost ștearsă" : $"; ștergeți-o manual (vssadmin delete shadows /shadow={id})"));
    }

    public void Delete(string id)
    {
        using var s = new System.Management.ManagementObjectSearcher(@"\\.\root\cimv2", $"SELECT * FROM Win32_ShadowCopy WHERE ID='{id.Replace("'", "")}'");
        using var col = s.Get();
        foreach (System.Management.ManagementObject m in col) using (m) m.Delete();
    }

    private bool TryDeleteOrphan(string id)
    {
        try { Delete(id); return true; } catch (Exception) { return false; }
    }

    /// <summary>Number of shadow copies of the station right now (before / after a collection, to prove that the collector left none behind).</summary>
    public static int Count()
    {
        using var s = new System.Management.ManagementObjectSearcher(@"\\.\root\cimv2", "SELECT ID FROM Win32_ShadowCopy");
        using var col = s.Get();
        int n = 0;
        foreach (System.Management.ManagementBaseObject m in col) using (m) n++;
        return n;
    }
}
