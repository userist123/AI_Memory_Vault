using System.ComponentModel;
using System.Runtime.InteropServices;

namespace LogAnalyzer.Dfir.Windows.Native;

public enum EventLogExportStatus { Exported, ChannelNotFound, AccessDenied, Failed }

/// <summary>
/// Exports a local event log channel to a raw .evtx with the same engine wevtutil uses (EvtExportLog in wevtapi.dll), without a child
/// process, a PATH lookup or localized text. It deliberately avoids System.Diagnostics.Eventing.Reader.EventLogSession, which also
/// opens remote sessions and is excluded from the classified edition.
/// </summary>
public static class EventLogExport
{
    private const int EvtExportLogChannelPath = 0x1;
    private const int EvtExportLogOverwrite = 0x10000;
    private const int ErrorAccessDenied = 5;
    private const int ErrorEvtChannelNotFound = 15007;
    private const int ErrorEvtInvalidQuery = 15001;

    public static (EventLogExportStatus Status, int Win32Error) Export(string channel, string destination)
    {
        if (!OperatingSystem.IsWindows()) return (EventLogExportStatus.Failed, 0);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(destination))!);
        if (EvtExportLog(IntPtr.Zero, channel, "*", destination, EvtExportLogChannelPath | EvtExportLogOverwrite)) return (EventLogExportStatus.Exported, 0);
        int err = Marshal.GetLastWin32Error();
        return (err switch
        {
            ErrorEvtChannelNotFound or ErrorEvtInvalidQuery => EventLogExportStatus.ChannelNotFound,
            ErrorAccessDenied => EventLogExportStatus.AccessDenied,
            _ => EventLogExportStatus.Failed,
        }, err);
    }

    public static string Describe(int win32Error) => new Win32Exception(win32Error).Message;

    [DllImport("wevtapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EvtExportLog(IntPtr session, string path, string query, string targetFilePath, int flags);
}
