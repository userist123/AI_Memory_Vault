using System.Runtime.InteropServices;

namespace LogAnalyzer.Dfir.Windows.Native;

/// <summary>Suspends / resumes every thread of a process (NtSuspendProcess / NtResumeProcess). Reversible.</summary>
public static class ProcessSuspension
{
    private const uint PROCESS_SUSPEND_RESUME = 0x0800;

    public static void Suspend(int pid) => Invoke(pid, NtSuspendProcess, "suspendare");
    public static void Resume(int pid) => Invoke(pid, NtResumeProcess, "reluare");

    private static void Invoke(int pid, Func<IntPtr, int> op, string what)
    {
        IntPtr h = OpenProcess(PROCESS_SUSPEND_RESUME, false, pid);
        if (h == IntPtr.Zero)
            throw new InvalidOperationException($"Procesul {pid} nu poate fi deschis pentru {what} (eroare {Marshal.GetLastWin32Error()}).");
        try
        {
            int status = op(h);
            if (status != 0) throw new InvalidOperationException($"{what} procesului {pid} a eșuat (NTSTATUS 0x{status:X8}).");
        }
        finally
        {
            CloseHandle(h);
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr OpenProcess(uint access, bool inherit, int pid);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr h);
    [DllImport("ntdll.dll")] private static extern int NtSuspendProcess(IntPtr h);
    [DllImport("ntdll.dll")] private static extern int NtResumeProcess(IntPtr h);
}
