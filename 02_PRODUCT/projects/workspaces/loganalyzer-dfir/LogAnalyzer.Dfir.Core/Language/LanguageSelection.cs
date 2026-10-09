namespace LogAnalyzer.Dfir.Language;

/// <summary>
/// The language the operator chose, kept per user (<see cref="UserSettings"/>) and applied to the whole interface at once (<see cref="Loc.SetLanguage"/>).
/// <see cref="Apply"/> runs at startup (default Romanian); <see cref="Choose"/> runs when the operator picks a language and takes effect without a restart.
/// </summary>
public sealed class LanguageSelection(UserSettings settings)
{
    /// <summary>Reads the stored choice and applies it. Returns the language now in force.</summary>
    public AppLanguage Apply()
    {
        var l = settings.LoadLanguage();
        Loc.SetLanguage(l);
        return l;
    }

    /// <summary>Applies the choice and stores it; false when it could not be stored (it still holds for this session).</summary>
    public bool Choose(AppLanguage language)
    {
        Loc.SetLanguage(language);
        return settings.SaveLanguage(language);
    }
}
