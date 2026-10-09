using LogAnalyzer.Dfir.Analysis;
using LogAnalyzer.Dfir.Model;
using LogAnalyzer.Dfir.Profile;
using LogAnalyzer.Dfir.Registers;
using Xunit;

namespace LogAnalyzer.Dfir.Tests;

/// <summary>Synthetic rows and registers for the WP14a rule tests (reuses the WP11 row builders).</summary>
internal static class W14
{
    public static CaseScope Classified(NetworkCategory n = NetworkCategory.AirGappedNetwork) => new() { Network = n, Classification = ClassificationLevel.Classified };
    public static CaseScope Unclassified(NetworkCategory n = NetworkCategory.AirGappedNetwork) => new() { Network = n, Classification = ClassificationLevel.Unclassified };

    // RegistrationNumber, Serial, VidPid, Type, Classification, AssignedUser, Zone, ValidFrom, ValidTo, Status, Notes
    public static MediaRegister Media(params string[][] rows) { var r = new MediaRegister(); r.SetCells(rows); return r; }
    public static string[] Row(string serial, string status = "active", string user = "", string zone = "", string from = "2026-01-01", string to = "2026-12-31",
        string cls = "", string vidpid = "", string type = "USB", string number = "") =>
        [number.Length > 0 ? number : "M-" + serial, serial, vidpid, type, cls, user, zone, from, to, status, ""];

    public static UsersRegister Users(params string[][] rows) { var r = new UsersRegister(); r.SetCells(rows); return r; }

    public static TimelineEvent Usb(string serial, double minute = 0, string vidpid = "VID_0951&PID_1666", string letter = "", string label = "Kingston DataTraveler") =>
        W11.Row("USB", minute, f: [("Serial", serial), ("VidPid", vidpid), ("FriendlyName", label), ("DriveLetter", letter), ("Vendor", "Kingston"), ("Product", "DataTraveler")]);

    public static List<Finding> Go(IEnumerable<TimelineEvent> events, CaseScope? scope = null, MediaRegister? media = null, UsersRegister? users = null, ProcedureProfile? profile = null,
        string zone = "", Wp14Data? data = null)
    {
        int n = 0;
        return Wp14Rules.Run(events.ToList(), () => $"F-{++n:D4}", new Wp14Input { Scope = scope ?? Classified(), Media = media, Users = users, Profile = profile, SystemZone = zone, Data = data });
    }

    public static ProcedureProfile ProfileWithDestinations(params (string Address, string Zone)[] rows)
    {
        var p = new ProcedureProfile();
        ProfileTables.SetCells(p, ProfileTable.Zones, rows.Select(r => r.Zone).Distinct().Select(z => (IReadOnlyList<string>)[z, ""]).ToList());
        ProfileTables.SetCells(p, ProfileTable.NetworkDestinations, rows.Select(r => (IReadOnlyList<string>)[r.Address, r.Zone, "test"]).ToList());
        return p;
    }

    public static ProcedureProfile ProfileWithChannel(string medium)
    {
        var p = new ProcedureProfile();
        ProfileTables.SetCells(p, ProfileTable.Zones, [["ROSU", ""], ["PUBLIC", ""]]);
        ProfileTables.SetCells(p, ProfileTable.TransferChannels, [["Canal aprobat", "ROSU", "PUBLIC", medium, ""]]);
        return p;
    }
}
