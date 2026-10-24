using System.Security.AccessControl;
using System.Security.Principal;
using LogAnalyzer.Dfir.Windows.Audit;
using Xunit;

namespace LogAnalyzer.Dfir.Tests;

/// <summary>
/// Review finding: File.Exists / Directory.Exists return false on "access denied", so an unreadable phonebook or Wi-Fi profile folder looked
/// absent. Only a missing entry in a folder that could be listed is "absent"; anything else is "unread", with the real reason.
/// </summary>
public sealed class UnreadableLocalSourceTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "la-unread-" + Guid.NewGuid().ToString("N"));
    private readonly List<(FileSystemInfo Item, FileSystemAccessRule Rule)> _denied = [];

    public UnreadableLocalSourceTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        foreach (var (item, rule) in _denied)
        {
            if (item is DirectoryInfo d) { var s = d.GetAccessControl(); s.RemoveAccessRule(rule); d.SetAccessControl(s); }
            else if (item is FileInfo f) { var s = f.GetAccessControl(); s.RemoveAccessRule(rule); f.SetAccessControl(s); }
        }
        try { Directory.Delete(_dir, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    private static SecurityIdentifier Me => WindowsIdentity.GetCurrent().User!;

    private void DenyList(string dir)
    {
        var d = new DirectoryInfo(dir);
        var rule = new FileSystemAccessRule(Me, FileSystemRights.ListDirectory | FileSystemRights.Traverse, AccessControlType.Deny);
        var s = d.GetAccessControl(); s.AddAccessRule(rule); d.SetAccessControl(s);
        _denied.Add((d, rule));
    }

    private void DenyRead(string file)
    {
        var f = new FileInfo(file);
        var rule = new FileSystemAccessRule(Me, FileSystemRights.ReadData, AccessControlType.Deny);
        var s = f.GetAccessControl(); s.AddAccessRule(rule); f.SetAccessControl(s);
        _denied.Add((f, rule));
    }

    // ---- confirmed absent vs unreadable ------------------------------------------------------------------------------------------

    [Fact]
    public void A_path_missing_from_a_listable_folder_is_confirmed_absent()
    {
        Assert.True(EgressPathCollector.ConfirmAbsent(Path.Combine(_dir, "rasphone.pbk"), out _));
        Assert.True(EgressPathCollector.ConfirmAbsent(Path.Combine(_dir, "Network", "Connections", "Pbk", "rasphone.pbk"), out _));
    }

    [Fact]
    public void A_path_under_a_folder_that_cannot_be_listed_is_not_absent()
    {
        var locked = Directory.CreateDirectory(Path.Combine(_dir, "Pbk")).FullName;
        DenyList(locked);
        Assert.False(EgressPathCollector.ConfirmAbsent(Path.Combine(locked, "rasphone.pbk"), out var reason));
        Assert.False(string.IsNullOrWhiteSpace(reason));
    }

    // ---- Wi-Fi profile count -----------------------------------------------------------------------------------------------------

    [Fact]
    public void Wifi_profiles_are_counted_absent_folder_is_zero_and_locked_folder_is_unread()
    {
        var root = Directory.CreateDirectory(Path.Combine(_dir, "Interfaces")).FullName;
        Directory.CreateDirectory(Path.Combine(root, "{guid}"));
        File.WriteAllText(Path.Combine(root, "{guid}", "a.xml"), "<x/>");
        File.WriteAllText(Path.Combine(root, "{guid}", "b.xml"), "<x/>");
        Assert.Equal(2, EgressPathCollector.CountWifiProfiles(root, out _));

        Assert.Equal(0, EgressPathCollector.CountWifiProfiles(Path.Combine(_dir, "Wlansvc", "Profiles", "Interfaces"), out _));

        DenyList(root);
        Assert.Null(EgressPathCollector.CountWifiProfiles(root, out var reason));
        Assert.False(string.IsNullOrWhiteSpace(reason));
    }

    [Fact]
    public void Wifi_profile_folder_hidden_by_a_locked_parent_is_unread_not_zero()
    {
        // The profile folder is not found, but its parent cannot be listed: "missing" cannot be confirmed, so the count is unread, not 0.
        // (A child folder that exists stays reachable through a locked parent because of the bypass-traverse privilege; that case is a real 0.)
        var parent = Directory.CreateDirectory(Path.Combine(_dir, "Wlansvc")).FullName;
        DenyList(parent);
        Assert.Null(EgressPathCollector.CountWifiProfiles(Path.Combine(parent, "Profiles", "Interfaces"), out _));
    }

    [Fact]
    public void N11_names_the_real_reason_for_unread_wifi_profiles()
    {
        var f = new StationFacts { Posture = LogAnalyzer.Core.Services.Edition.StationPosture.IsolatedNetwork, IsAdministrator = true, EgressPaths = [] };
        ControlMissingSourceTests.N11SourcesRead(f);
        f.Settings.Remove("Wlan.ProfileCount");
        f.Settings["Wlan.ProfileUnread"] = "Access to the path is denied.";
        var c = Assert.Single(ControlEvaluator.Evaluate(f).Checks, c => c.Id == "N11");
        Assert.Equal(ControlStatus.Nedeterminat, c.Status);
        Assert.Contains("profilurile Wi-Fi salvate nu au putut fi citite: Access to the path is denied.", c.Detail);
        Assert.DoesNotContain("necesită drepturi de administrator", c.Detail);
    }

    // ---- RAS phonebooks ----------------------------------------------------------------------------------------------------------

    [Fact]
    public void Phonebook_present_absent_and_unreadable()
    {
        var paths = new List<EgressPathFact>(); var unread = new List<string>();
        var pbk = Path.Combine(_dir, "rasphone.pbk");
        File.WriteAllText(pbk, "[Office VPN]\r\nPhoneNumber=vpn.example\r\n");
        EgressPathCollector.ReadPhonebook(pbk, "toți utilizatorii", paths, unread);
        Assert.Contains(paths, p => p.Description.StartsWith("Office VPN", StringComparison.Ordinal));
        Assert.Empty(unread);

        paths.Clear();
        EgressPathCollector.ReadPhonebook(Path.Combine(_dir, "missing", "rasphone.pbk"), "utilizatorul curent", paths, unread);
        Assert.Empty(paths);
        Assert.Empty(unread);                                       // confirmed absent: nothing to report

        DenyRead(pbk);
        EgressPathCollector.ReadPhonebook(pbk, "toți utilizatorii", paths, unread);
        Assert.Empty(paths);
        Assert.Contains(unread, u => u.Contains("toți utilizatorii", StringComparison.Ordinal));   // unreadable is unread, never "no VPN profiles"

        var locked = Directory.CreateDirectory(Path.Combine(_dir, "lockedPbk")).FullName;
        DenyList(locked);
        unread.Clear();
        EgressPathCollector.ReadPhonebook(Path.Combine(locked, "rasphone.pbk"), "utilizatorul curent", paths, unread);
        Assert.Single(unread);
    }
}
