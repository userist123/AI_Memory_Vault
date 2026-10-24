using System.ComponentModel;
using System.Diagnostics;
using System.Security.Cryptography;
using LogAnalyzer.Dfir.Response;

namespace LogAnalyzer.Dfir.Windows.Containment;

/// <summary>
/// The live process list, re-enumerated on every call (read only). A field that cannot be read (access denied, process ended between
/// the enumeration and the read) is null, so the re-check reports NOT_VERIFIED instead of guessing.
/// </summary>
public sealed class SystemProcessTable : IProcessTable
{
    public ProcessObservation? Observe(int pid)
    {
        var all = Process.GetProcesses();
        try
        {
            var p = all.FirstOrDefault(x => x.Id == pid);
            if (p is null) return null;
            DateTimeOffset? start = null;
            string? image = null;
            bool? suspended = null;
            try { start = new DateTimeOffset(p.StartTime.ToUniversalTime(), TimeSpan.Zero); }
            catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or NotSupportedException) { }
            try { image = p.MainModule?.FileName; }
            catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or NotSupportedException) { }
            try
            {
                var threads = p.Threads.Cast<ProcessThread>().ToList();
                suspended = threads.Count > 0 && threads.All(t => t.ThreadState == System.Diagnostics.ThreadState.Wait && t.WaitReason == ThreadWaitReason.Suspended);
            }
            catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or NotSupportedException) { }
            return new ProcessObservation(pid, start, image, suspended);
        }
        finally
        {
            foreach (var x in all) x.Dispose();
        }
    }

    public string? HashImage(string path)
    {
        try
        {
            using var f = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            return Convert.ToHexString(SHA256.HashData(f));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) { return null; }
    }
}
