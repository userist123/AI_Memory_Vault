using System.Text.RegularExpressions;

namespace LogAnalyzer.Dfir.Analysis;

/// <summary>
/// Windows defaults that look like persistence from a user-writable location. Found in the lab on 2026-11-10: the per-user OneDrive that Windows installs on
/// every profile (a Run value and a scheduled task under ...\AppData\Local\Microsoft\OneDrive) was reported as Medium / High persistence on every station.
/// Recognising a default never hides the finding: the location is user-writable, so another program can carry the same name. The severity is lowered and
/// the operator is told to check the Microsoft signature.
/// </summary>
public static class KnownDefaults
{
    public const string OneDriveNote =
        "Aceasta este calea implicită a componentei OneDrive, pe care Windows o instalează pentru fiecare utilizator. Locația poate fi însă scrisă de utilizator, deci un alt program poate purta același nume: verificați semnătura digitală Microsoft a fișierului.";

    public const string OneDriveAlternative = "Componenta OneDrive instalată de Windows pentru fiecare utilizator (comportament implicit).";
    public const string OneDriveNextStep = "Verificați semnătura digitală Microsoft a fișierului (Proprietăți → Semnături digitale).";

    private static readonly Regex DefaultOneDrive = new(
        @"^""?(%localappdata%|[a-z]:\\users\\(?!\.{1,2}\\)[^\\/:*?""<>|]+\\appdata\\local)\\microsoft\\onedrive\\(onedrive|onedrivestandaloneupdater)\.exe""?(\s|$)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(200));

    /// <summary>
    /// True only for the exact default per-user OneDrive executables (OneDrive.exe, OneDriveStandaloneUpdater.exe directly in ...\AppData\Local\Microsoft\OneDrive),
    /// as the first token of the command. A sub-folder, another name, a wrapper, a traversal or a network path is not the default.
    /// </summary>
    public static bool IsDefaultOneDrivePath(string? command)
    {
        if (string.IsNullOrWhiteSpace(command) || command.Contains("..", StringComparison.Ordinal)) return false;
        try { return DefaultOneDrive.IsMatch(command.Trim()); }
        catch (RegexMatchTimeoutException) { return false; }
    }
}
