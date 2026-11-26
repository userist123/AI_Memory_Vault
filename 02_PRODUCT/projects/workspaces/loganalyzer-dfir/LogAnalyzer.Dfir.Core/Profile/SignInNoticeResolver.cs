namespace LogAnalyzer.Dfir.Profile;

/// <summary>What the sign-in window shows as the system-use notice, where it came from, and an optional one-line remark.</summary>
public sealed record SignInNoticeChoice(string Text, string Source, string? Remark);

/// <summary>
/// HG 585 ID-03. The notice shown before sign-in is the one in the procedure profile (field <see cref="ProcedureProfile.SignInNotice"/>) when it has text,
/// otherwise the module default. The window is shown before a session exists, so this reads the profile in force (<see cref="ProfileProvider.Current"/>)
/// and never throws: an unreadable profile means the default text plus a short remark, and sign-in is never blocked by it.
/// </summary>
public static class SignInNoticeResolver
{
    public const int MaxLength = 2000;
    public const string SourceProfile = "profil", SourceDefault = "implicit";
    public const string UnreadableRemark = "Profilul nu poate fi citit; se afișează textul implicit.";

    public static string Normalize(string? text) => (text ?? "").Trim();

    public static SignInNoticeChoice Resolve(ProfileProvider provider, string defaultText)
    {
        try
        {
            var profile = provider.Current;
            var own = Normalize(profile?.SignInNotice);
            if (own.Length > 0) return new SignInNoticeChoice(own.Length > MaxLength ? own[..MaxLength] : own, SourceProfile, null);
            if (profile is null && provider.Issues.Any(i => i.IsError)) return new SignInNoticeChoice(defaultText, SourceDefault, UnreadableRemark);
            return new SignInNoticeChoice(defaultText, SourceDefault, null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or InvalidOperationException)
        {
            return new SignInNoticeChoice(defaultText, SourceDefault, UnreadableRemark);
        }
    }
}
