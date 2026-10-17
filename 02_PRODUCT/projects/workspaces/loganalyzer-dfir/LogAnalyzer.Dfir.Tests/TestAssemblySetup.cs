using System.Runtime.CompilerServices;
using LogAnalyzer.Dfir.IO;

namespace LogAnalyzer.Dfir.Tests;

/// <summary>Test-wide setup: parsers run on files outside a case use a temporary work root, never the station's %LOCALAPPDATA%\LogAnalyzer\work.</summary>
internal static class TestAssemblySetup
{
    [ModuleInitializer]
    internal static void Initialize() =>
        WorkFolders.UserWorkRootOverride = Path.Combine(Path.GetTempPath(), "ladfir_userwork_" + Environment.ProcessId);
}
