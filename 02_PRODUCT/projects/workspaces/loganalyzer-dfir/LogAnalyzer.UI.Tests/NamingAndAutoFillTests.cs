using LogAnalyzer.Dfir.Registers;
using Xunit;

namespace LogAnalyzer.UI.Tests;

public sealed class NamingConventionTests
{
    [Theory]
    [InlineData("1234-56SV", "1234", "56", "SV")]
    [InlineData("1234-7S", "1234", "7", "S")]
    [InlineData("0001-123456SSI", "0001", "123456", "SSI")]
    [InlineData(" 1234-56sv ", "1234", "56", "SV")]
    public void MediaLabel_parses(string label, string unit, string reg, string cls)
    {
        Assert.True(MediaLabel.TryParse(label, out var p));
        Assert.Equal((unit, reg, cls), (p.UnitCode, p.RegistrationNumber, p.Classification));
    }

    [Theory]
    [InlineData("1234_56SV")] [InlineData("ABCD-12S")] [InlineData("1234-56")] [InlineData("1234-56SSSS")] [InlineData(null)] [InlineData("")]
    public void MediaLabel_rejects(string? label) => Assert.False(MediaLabel.TryParse(label, out _));

    [Fact]
    public void Station_standalone()
    {
        var s = StationName.Parse("1234DOC5143SV");
        Assert.Equal(StationNameKind.Standalone, s.Kind);
        Assert.Equal(("1234", "DOC", "5143", "SV"), (s.UnitCode, s.Compartment, s.RegistrationNumber, s.Classification));
        Assert.Equal("Stație izolată: cod unitate 1234, compartiment DOC, număr de înregistrare 5143, clasificare SV", s.Describe());
    }

    [Theory]
    [InlineData("NETWS01", "NET", "01")]
    [InlineData("netws12", "NET", "12")]
    public void Station_networked(string name, string net, string num)
    {
        var s = StationName.Parse(name);
        Assert.Equal(StationNameKind.Networked, s.Kind);
        Assert.Equal((net, num), (s.Network, s.Number));
        Assert.Equal($"Stație în rețea: rețeaua {net}, stația de lucru {num}", s.Describe());
    }

    [Fact]
    public void Station_unknown()
    {
        var s = StationName.Parse("LAB-PC");
        Assert.Equal(StationNameKind.Unknown, s.Kind);
        Assert.Equal("Numele stației nu urmează convenția", s.Describe());
    }
}

public sealed class RegisterAutoFillTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 11, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Users_adds_new_and_skips_existing_by_account_and_sid_without_touching_rows()
    {
        var reg = new UsersRegister();
        reg.Rows.Add(new UserRow { Person = "Ion Popescu", Accounts = @"PC\ion", Clearance = "S", NeedToKnow = "x" });
        reg.Rows.Add(new UserRow { Person = "Ana", Accounts = "ana", Sids = "S-1-5-21-1-2-3-1001" });
        var c = RegisterAutoFill.MergeUsers(reg,
        [
            new ObservedAccount(@"PC\ion", "S-1-5-21-1-2-3-1000", "Conturi locale", null),
            new ObservedAccount("ANA2", "S-1-5-21-1-2-3-1001", "Profil", null),
            new ObservedAccount("maria", "S-1-5-21-1-2-3-1002", "Profil", null),
        ], Now);
        Assert.Equal(new MergeCounts(1, 2), c);
        Assert.Equal(3, reg.Rows.Count);
        Assert.Equal("S", reg.Rows[0].Clearance);
        Assert.Equal("x", reg.Rows[0].NeedToKnow);
        var added = reg.Rows[2];
        Assert.Equal(("maria", "maria", "S-1-5-21-1-2-3-1002", ""), (added.Person, added.Accounts, added.Sids, added.Clearance));
        Assert.DoesNotContain(reg.Validate(), i => i.IsError && i.Line == 3);
    }

    [Fact]
    public void Media_adds_conforming_label_and_validates()
    {
        var reg = new MediaRegister();
        var c = RegisterAutoFill.MergeMedia(reg, [new ObservedMedium("4C53000123", "0781:5567", "Cruzer", "1234-56S", "USB", "USBSTOR", null)], "1234", Now);
        Assert.Equal(new MergeCounts(1, 0), c);
        var r = reg.Rows[0];
        Assert.Equal(("1234-56S", "S", "USB", "active", "0781:5567"), (r.RegistrationNumber, r.Classification, r.Type, r.Status, r.VidPid));
        Assert.Equal("observat pe stație 2026-10-11, eticheta „1234-56S”", r.Notes);
        Assert.Empty(reg.Validate().Where(i => i.IsError));
    }

    [Fact]
    public void Media_notes_foreign_unit_and_nonconforming_label()
    {
        var reg = new MediaRegister();
        RegisterAutoFill.MergeMedia(reg,
        [
            new ObservedMedium("A1", "", "", "9999-5SV", "USB", "USBSTOR", null),
            new ObservedMedium("B2", "", "", "BACKUP", "USB", "USBSTOR", null),
        ], "1234", Now);
        Assert.Contains("cod de unitate diferit de al stației (9999 ≠ 1234)", reg.Rows[0].Notes);
        Assert.Equal("", reg.Rows[1].RegistrationNumber);
        Assert.Contains("eticheta nu urmează convenția xxxx-yyyyL", reg.Rows[1].Notes);
    }

    [Fact]
    public void Media_skips_existing_by_serial_and_registration_number_and_never_overwrites()
    {
        var reg = new MediaRegister();
        reg.Rows.Add(new MediaRow { RegistrationNumber = "1234-1S", Serial = "AAA&0", Type = "HDD", Zone = "Z1", Status = "withdrawn" });
        reg.Rows.Add(new MediaRow { RegistrationNumber = "1234-2S", Serial = "BBB", Type = "USB", Status = "active" });
        var c = RegisterAutoFill.MergeMedia(reg,
        [
            new ObservedMedium("aaa", "0781:5567", "", "other", "USB", "USBSTOR", null),
            new ObservedMedium("CCC", "", "", "1234-2S", "USB", "USBSTOR", null),
            new ObservedMedium("DDD", "", "", "1234-3S", "CD/DVD", "Volum montat", null),
        ], "1234", Now);
        Assert.Equal(new MergeCounts(1, 2), c);
        Assert.Equal(("HDD", "Z1", "withdrawn", ""), (reg.Rows[0].Type, reg.Rows[0].Zone, reg.Rows[0].Status, reg.Rows[0].VidPid));
        Assert.Equal("DVD", reg.Rows[2].Type);
    }
}
