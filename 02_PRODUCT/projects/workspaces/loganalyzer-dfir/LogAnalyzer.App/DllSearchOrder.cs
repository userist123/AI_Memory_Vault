using System.Runtime.InteropServices;

namespace LogAnalyzer.UI
{
    /// <summary>
    /// Review of the remediations #4: restricts the DLL search order of the whole process before any of this assembly's code runs (module
    /// initializer: before <c>Main</c>, before the static constructor of <see cref="App"/> and before WPF or SQLCipher load a library). A library
    /// loaded by name is then looked for only in the application folder (checked against the embedded manifest) and in System32, never in the
    /// current directory or a folder on PATH. LogAnalyzer.App.Classified compiles this file too (linked sources).
    /// </summary>
    internal static class DllSearchOrder
    {
        private const uint LOAD_LIBRARY_SEARCH_APPLICATION_DIR = 0x00000200;
        private const uint LOAD_LIBRARY_SEARCH_SYSTEM32 = 0x00000800;

        [System.Runtime.CompilerServices.ModuleInitializer]
        internal static void Restrict()
        {
            if (!OperatingSystem.IsWindows()) return;
            // Failure is not fatal (the native manifest check and the install-folder check still run), but it is never silent.
            if (!SetDefaultDllDirectories(LOAD_LIBRARY_SEARCH_APPLICATION_DIR | LOAD_LIBRARY_SEARCH_SYSTEM32))
                System.Diagnostics.Trace.TraceWarning($"SetDefaultDllDirectories a eșuat (Win32 {Marshal.GetLastWin32Error()}): ordinea de căutare a bibliotecilor nu a fost restrânsă.");
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetDefaultDllDirectories(uint directoryFlags);
    }
}
