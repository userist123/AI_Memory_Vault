namespace LogAnalyzer.Dfir.Auth;

/// <summary>
/// The signed-in identity that custody / audit chains record as "who" (decision 33), instead of the operating-system account. The application sets it
/// at sign-in; before that (tests, headless tools) the operating-system account is used and the entry says so. When authentication is required and
/// nobody is signed in, writes that need an administrator are refused.
/// </summary>
public static class OperatorIdentity
{
    private sealed record Override(AuthSession? Session, bool Required);

    private static volatile AuthSession? _currentGlobal;
    private static volatile bool _requiredGlobal;
    private static readonly AsyncLocal<Override?> Scoped = new();

    private static AuthSession? _current => Scoped.Value is { } o ? o.Session : _currentGlobal;
    private static bool _required => Scoped.Value is { } o ? o.Required : _requiredGlobal;

    public static AuthSession? Current { get => _current; set => _currentGlobal = value; }

    /// <summary>Set by the application at startup: the sign-in window is mandatory, so gates fail closed without an administrator session.</summary>
    public static bool AuthenticationRequired { get => _required; set => _requiredGlobal = value; }

    /// <summary>Overrides the identity for the current async flow only (tests; parallel tests do not see each other's identity). Dispose restores the previous one.</summary>
    public static IDisposable Scope(AuthSession? session, bool authenticationRequired)
    {
        var previous = Scoped.Value;
        Scoped.Value = new Override(session, authenticationRequired);
        return new Restore(previous);
    }

    private sealed class Restore(Override? previous) : IDisposable { public void Dispose() => Scoped.Value = previous; }

    private static AuthSession? Active => _current is { Locked: false, Ended: false } s ? s : null;

    public static string Who => Active?.Account ?? (_required ? "(neautentificat)" : $"{Environment.UserName}");

    public static string WhoDomainQualified => Active?.Account ?? (_required ? "(neautentificat)" : $"{Environment.UserDomainName}\\{Environment.UserName}");

    public static string WhoSource => Active is { } s
        ? $"autentificat în aplicație ({s.Method}), rol {(s.Role == AuthRole.Administrator ? "administrator" : "operator")}"
        : _required ? "nimeni nu este autentificat" : "cont de sistem de operare; autentificarea în aplicație nu este activă în acest context";

    /// <summary>True when nobody is required to sign in (tests, tools) or an unlocked administrator is signed in.</summary>
    public static bool MayEditAdministration => !_required || Active is { IsAdministrator: true };

    public const string AdministratorOnlyMessage = "numai administratorul global autentificat poate modifica această resursă";
}
