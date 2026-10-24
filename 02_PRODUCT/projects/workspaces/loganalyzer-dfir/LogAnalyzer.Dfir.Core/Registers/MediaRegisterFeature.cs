namespace LogAnalyzer.Dfir.Registers;

/// <summary>
/// Owner decision 2026-10-16: the officer-signed media register ("Registru medii", the "Registrul de medii" and "Registrul semnat" tabs and the register
/// columns of the media page) is removed from the user interface; the real-time device monitor takes its place. The code stays compiled and tested, only
/// switched off. This is the single switch: set <see cref="DefaultEnabled"/> to true to bring the register back (tabs, columns, check D04 evaluated).
/// While it is off, check D04 is "Nu se aplică" with <see cref="NotInUseReason"/> and the transfer register does not require the signed register.
/// </summary>
public static class MediaRegisterFeature
{
    /// <summary>The value the application starts with. False: the signed media register is not used on this station.</summary>
    public const bool DefaultEnabled = false;

    /// <summary>Whether the signed media register is used (shown in the interface and evaluated by D04). Read once by the views and the control.</summary>
    public static bool Enabled { get; set; } = DefaultEnabled;

    /// <summary>Why D04 does not apply while the register is switched off (printed in the check detail).</summary>
    public const string NotInUseReason = "registrul semnat al mediilor nu este folosit pe această stație";
}
