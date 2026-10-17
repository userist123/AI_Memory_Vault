using System.Text;
using System.Text.Json;
using LogAnalyzer.Dfir.Auth;
using LogAnalyzer.Dfir.Case;
using LogAnalyzer.Dfir.Devices;
using Xunit;

namespace LogAnalyzer.Dfir.Tests;

/// <summary>
/// The per-device history from the sealed audit, the CSV of the device list and the monitor running without the right to seal. Neutral fixtures: LAB-PC, user,
/// FAKESN0001.
/// </summary>
public sealed class DeviceHistoryAndCsvTests : IDisposable
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 16, 8, 0, 0, TimeSpan.Zero);
    private const string Id = "FAKESN0001|0781:5567";
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "devhist_" + Guid.NewGuid().ToString("N"));
    public DeviceHistoryAndCsvTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }

    private static SealedDeviceEvent Ev(int minutes, string ev, string op, params (string K, string V)[] fields) =>
        new(T0.AddMinutes(minutes), ev, Id, op, fields.ToDictionary(x => x.K, x => x.V));

    private static DeviceAuditReading Audit(bool valid, params SealedDeviceEvent[] events) =>
        new(valid, new Dictionary<string, string>(), [], 0) { Events = events };

    // ---- history ---------------------------------------------------------------------------------------------------------------------------

    [Fact]
    public void The_history_lists_the_sealed_events_of_one_device_in_time_order()
    {
        var other = new SealedDeviceEvent(T0, OperationAudit.DeviceFirstSeen, "OTHER|1111:2222", "", new Dictionary<string, string>());
        var audit = Audit(true,
            Ev(30, OperationAudit.DeviceDisconnected, "", ("user", "LAB-PC\\user"), ("device", "Fake Stick")),
            Ev(0, OperationAudit.DeviceFirstSeen, "", ("device", "Fake Stick"), ("serial", "FAKESN0001"), ("user", "LAB-PC\\user")),
            Ev(0, OperationAudit.DeviceAlertCritical, "", ("device", "Fake Stick"), ("text", "mediu de stocare fără număr de înregistrare"), ("user", "LAB-PC\\user")),
            Ev(10, OperationAudit.DeviceRegistrationChanged, "LAB-PC\\admin", ("old", "(gol)"), ("new", "RN-7")),
            Ev(20, OperationAudit.DeviceStateChanged, "", ("code", "28"), ("active", "da")),
            Ev(40, OperationAudit.DeviceAlertInfo, "", ("device", "Fake Stick"), ("text", "dispozitiv conectat")));
        var h = DeviceHistory.Build(Id, audit);
        Assert.DoesNotContain(h, x => x.Details.Contains("OTHER"));
        Assert.Equal([DeviceHistory.FirstSeen, DeviceHistory.Connected, DeviceHistory.Alert, DeviceHistory.RegistrationChanged, DeviceHistory.StateChanged, DeviceHistory.Disconnected, DeviceHistory.Connected],
            h.Select(x => x.Kind).ToArray());
        Assert.Equal(h.Select(x => x.TimeUtc).OrderBy(t => t).ToList(), h.Select(x => x.TimeUtc).ToList());
        var reg = h.Single(x => x.Kind == DeviceHistory.RegistrationChanged);
        Assert.Equal("(gol) → RN-7", reg.Details);
        Assert.Equal("LAB-PC\\admin", reg.User);
        Assert.Contains("Nu funcționează (fără driver)", h.Single(x => x.Kind == DeviceHistory.StateChanged).Details);
        Assert.Equal("LAB-PC\\user", h.First(x => x.Kind == DeviceHistory.Connected).User);
        Assert.Contains("critică", h.Single(x => x.Kind == DeviceHistory.Alert).Details);
        Assert.All(h, x => Assert.False(x.Pending));
    }

    [Fact]
    public void Pending_records_are_marked_unsealed_and_come_after_the_sealed_ones()
    {
        var audit = Audit(true, Ev(0, OperationAudit.DeviceFirstSeen, "", ("device", "Fake Stick")));
        var pending = new[]
        {
            new PendingSeal { Event = OperationAudit.DeviceDisconnected, Detail = OperationAudit.DeviceDisconnectedDetail(Id, "UsbStorage", "Fake Stick", "LAB-PC\\user") },
            new PendingSeal { Event = OperationAudit.DeviceDisconnected, Detail = OperationAudit.DeviceDisconnectedDetail("OTHER|1", "UsbStorage", "Other", "u") },
        };
        var h = DeviceHistory.Build(Id, audit, pending);
        Assert.Equal(2, h.Count);
        Assert.False(h[0].Pending);
        Assert.True(h[1].Pending);
        Assert.Contains("în așteptare (nesigilat)", h[1].Kind);
        Assert.Null(h[1].TimeUtc);
    }

    [Fact]
    public void When_the_chain_does_not_verify_every_sealed_row_is_marked_unverified()
    {
        var audit = Audit(false, Ev(0, OperationAudit.DeviceFirstSeen, "", ("device", "Fake Stick")), Ev(10, OperationAudit.DeviceDisconnected, "", ("device", "Fake Stick")));
        var h = DeviceHistory.Build(Id, audit);
        Assert.Equal(2, h.Count);
        Assert.All(h, x => { Assert.Contains(DeviceHistory.UnverifiedMark, x.Kind); Assert.True(x.Unverified); });
        Assert.All(DeviceHistory.Build(Id, Audit(true, Ev(0, OperationAudit.DeviceFirstSeen, "", ("device", "Fake Stick")))), x => Assert.False(x.Unverified));
    }

    [Fact]
    public void Rows_after_a_record_with_an_earlier_time_are_marked_with_a_date_that_cannot_be_relied_on()
    {
        static SealedDeviceEvent At(int minutes, int index, string ev) => new(T0.AddMinutes(minutes), ev, Id, "", new Dictionary<string, string> { ["device"] = "Fake Stick" }, index);
        var audit = new DeviceAuditReading(true, new Dictionary<string, string>(), [], 0)
        {
            Events = [At(0, 1, OperationAudit.DeviceFirstSeen), At(60, 2, OperationAudit.DevicePresent), At(5, 3, OperationAudit.DeviceDisconnected), At(70, 4, OperationAudit.DevicePresent)],
            FirstRegression = 3,
        };
        var h = DeviceHistory.Build(Id, audit);
        Assert.DoesNotContain(DeviceHistory.BackdatedMark, h.Single(x => x.Kind.StartsWith(DeviceHistory.FirstSeen)).Kind);
        Assert.Equal(2, h.Count(x => x.Kind.Contains(DeviceHistory.BackdatedMark)));
        Assert.Single(h, x => x.Kind.StartsWith(DeviceHistory.PresentAtStart) && !x.Unverified);
    }

    [Fact]
    public void The_real_audit_without_a_backdated_record_marks_nothing()
    {
        var svc = new AuthService(Path.Combine(_dir, "authb"));
        svc.AuditOperation(null, OperationAudit.DeviceFirstSeen, OperationAudit.DeviceFirstSeenDetail(Id, "UsbStorage", "Fake Stick", "FAKESN0001", DateTimeOffset.UtcNow, user: "LAB-PC\\user"));
        svc.AuditOperation(null, OperationAudit.DeviceDisconnected, OperationAudit.DeviceDisconnectedDetail(Id, "UsbStorage", "Fake Stick", "LAB-PC\\user"));
        var r = OperationAudit.ReadDeviceMonitor(svc.AuditPath, chainValid: true);
        Assert.Equal(int.MaxValue, r.FirstRegression);
        Assert.All(DeviceHistory.Build(Id, r), x => Assert.False(x.Unverified));
    }

    [Fact]
    public void A_pending_record_of_an_old_identity_stays_in_the_history_of_the_rekeyed_card()
    {
        const string oldId = "CARD|0781:5567|old", newId = "FAKESN0001|0781:5567";
        var audit = new DeviceAuditReading(true, new Dictionary<string, string>(), [], 0)
        {
            Renames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { [OperationAudit.DeviceField(oldId)] = OperationAudit.DeviceField(newId) },
        };
        var pending = new[] { new PendingSeal { Event = OperationAudit.DeviceDisconnected, Detail = OperationAudit.DeviceDisconnectedDetail(oldId, "UsbStorage", "Fake Card", "LAB-PC\\user") } };
        var h = DeviceHistory.Build(newId, audit, pending);
        var one = Assert.Single(h);
        Assert.True(one.Pending);
        Assert.Contains(DeviceHistory.PendingMark, one.Kind);
        Assert.Empty(DeviceHistory.Build("OTHER|1", audit, pending));
    }

    [Fact]
    public void The_real_audit_follows_a_rekey_so_pending_records_of_the_old_identity_match()
    {
        var svc = new AuthService(Path.Combine(_dir, "authc"));
        svc.AuditOperation(null, OperationAudit.DeviceMediumRekeyed, OperationAudit.DeviceMediumRekeyedDetail("CARD|1|old", Id, "UsbStorage", "Fake Card"));
        var r = OperationAudit.ReadDeviceMonitor(svc.AuditPath, chainValid: true);
        var pending = new[] { new PendingSeal { Event = OperationAudit.DeviceDisconnected, Detail = OperationAudit.DeviceDisconnectedDetail("CARD|1|old", "UsbStorage", "Fake Card", "u") } };
        Assert.Single(DeviceHistory.Build(Id, r, pending));
    }

    [Fact]
    public void The_report_never_says_registered_for_a_number_only_the_journal_has()
    {
        var e = new DeviceJournalEntry { Identity = Id, Class = DeviceClass.UsbStorage, Name = "Fake Stick", Serial = "FAKESN0001", RegistrationNumber = "RN-9", FirstSeenUtc = T0 };
        DeviceAuditReading A(string? sealedNumber, bool valid = true) =>
            new(valid, sealedNumber is null ? new Dictionary<string, string>() : new Dictionary<string, string> { [OperationAudit.DeviceField(Id)] = sealedNumber }, [], 0);
        Assert.EndsWith("registru: Înregistrat", DeviceMonitorReport.Line(e, A("RN-9")));
        Assert.EndsWith("registru: Neconfirmat (nesigilat)", DeviceMonitorReport.Line(e, A("RN-1")));          // the journal was edited
        Assert.EndsWith("registru: Neconfirmat (nesigilat)", DeviceMonitorReport.Line(e, A(null)));
        Assert.EndsWith("registru: Neconfirmat (nesigilat)", DeviceMonitorReport.Line(e, A("RN-9", valid: false)));
        Assert.EndsWith("registru: Neconfirmat (nesigilat)", DeviceMonitorReport.Line(e));                 // no sealed audit at all
        var pend = new[] { new PendingSeal { Event = OperationAudit.DeviceRegistrationChanged, Detail = OperationAudit.DeviceRegistrationChangedDetail(Id, "UsbStorage", "Fake Stick", "RN-1", "RN-9") } };
        Assert.EndsWith("registru: Neconfirmat (nesigilat)", DeviceMonitorReport.Line(e, A("RN-9"), pend));
        e.RegistrationNumber = "";
        Assert.EndsWith("registru: Neînregistrat", DeviceMonitorReport.Line(e, A(null)));
        Assert.EndsWith("registru: Neconfirmat (nesigilat)", DeviceMonitorReport.Line(e, A("RN-1")));          // the journal lost a sealed number
    }

    [Fact]
    public void An_unreadable_or_broken_audit_is_said_so_and_the_history_is_empty_or_pending_only()
    {
        Assert.Contains("nu a putut fi citit", DeviceHistory.Notice(null));
        Assert.Contains("nu se verifică", DeviceHistory.Notice(Audit(false)));
        Assert.Equal("", DeviceHistory.Notice(Audit(true)));
        Assert.Empty(DeviceHistory.Build(Id, null));
        Assert.Equal("din jurnalul sigilat", DeviceHistory.Title);
    }

    [Fact]
    public void The_real_sealed_audit_yields_the_events_with_the_operator()
    {
        var svc = new AuthService(Path.Combine(_dir, "auth"));
        svc.AuditOperation(null, OperationAudit.DeviceFirstSeen, OperationAudit.DeviceFirstSeenDetail(Id, "UsbStorage", "Fake Stick", "FAKESN0001", T0, user: "LAB-PC\\user"));
        svc.AuditOperation(null, OperationAudit.DeviceRegistrationChanged, OperationAudit.DeviceRegistrationChangedDetail(Id, "UsbStorage", "Fake Stick", "", "RN-1"));
        svc.AuditOperation(null, OperationAudit.DeviceDisconnected, OperationAudit.DeviceDisconnectedDetail(Id, "UsbStorage", "Fake Stick", "LAB-PC\\user"));
        svc.AuditOperation(null, OperationAudit.DeviceExportCsv, OperationAudit.DeviceExportCsvDetail("LAB-PC\\admin", 3, new string('a', 64)));
        var r = OperationAudit.ReadDeviceMonitor(svc.AuditPath, chainValid: true);
        var h = DeviceHistory.Build(Id, r);
        Assert.Equal([DeviceHistory.FirstSeen, DeviceHistory.RegistrationChanged, DeviceHistory.Disconnected], h.Select(x => x.Kind).ToArray());
        Assert.Equal("LAB-PC\\user", h[0].User);
        Assert.Equal(OperatorIdentity.Who, h[1].User);
        Assert.Equal("(gol) → RN-1", h[1].Details);
        Assert.Equal(ChainStatus.Valid, svc.VerifyAudit().Status);
    }

    [Fact]
    public void The_export_note_has_the_operator_the_rows_and_the_hash_and_no_path()
    {
        var d = OperationAudit.DeviceExportCsvDetail("LAB-PC\\admin", 12, "ab12");
        Assert.Equal("operator=LAB-PC\\admin; rows=12; sha256=ab12", d);
        Assert.DoesNotContain("path", d, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("device.export_csv", OperationAudit.DeviceExportCsv);
    }

    // ---- CSV -------------------------------------------------------------------------------------------------------------------------------

    [Fact]
    public void The_csv_quotes_values_with_semicolons_quotes_and_line_breaks()
    {
        Assert.Equal("simplu", DeviceCsv.Quote("simplu"));
        Assert.Equal("\"a;b\"", DeviceCsv.Quote("a;b"));
        Assert.Equal("\"cu \"\"ghilimele\"\"\"", DeviceCsv.Quote("cu \"ghilimele\""));
        Assert.Equal("\"două\r\nrânduri\"", DeviceCsv.Quote("două\r\nrânduri"));
        Assert.Equal("", DeviceCsv.Quote(null));
        Assert.Equal("'=1+1", DeviceCsv.Quote("=1+1"));          // a spreadsheet never runs a device name that starts like a formula
    }

    [Fact]
    public void The_csv_has_the_headers_the_rows_in_order_and_the_bom_and_keeps_the_diacritics()
    {
        var rows = new List<IReadOnlyList<string>>
        {
            Row("Conectat", "Stick; Țară", "Stick USB", "Fake", "Model „Ș”", "0781", "5567", "FAKESN0001", "14,9 GB", "E:", "ETICHETĂ", "LAB-PC\\user",
                "16.10.2026 11:00:00", "16.10.2026 11:00:00", "", "RN-7", "Înregistrat", ""),
        };
        var bytes = DeviceCsv.ToBytes(rows);
        Assert.Equal([0xEF, 0xBB, 0xBF], bytes.Take(3).ToArray());
        var text = new UTF8Encoding(false).GetString(bytes, 3, bytes.Length - 3);
        var lines = text.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(2, lines.Length);
        Assert.Equal("Stare;Nume;Tip;Producător;Model;VID;PID;Serie;Capacitate;Literă;Etichetă;Utilizator;Prima apariție;Ultima conectare;Ultima deconectare;Nr. de înregistrare;Registru;Alertă", lines[0]);
        Assert.StartsWith("Conectat;\"Stick; Țară\";Stick USB;Fake;Model „Ș”;0781;5567;FAKESN0001;14,9 GB;E:;ETICHETĂ;LAB-PC\\user;", lines[1]);
        Assert.Equal(DeviceCsv.Headers.Count, DeviceCsv.Headers.Distinct().Count());
        Assert.Equal(18, DeviceCsv.Headers.Count);
        Assert.EndsWith(";RN-7;Înregistrat;", lines[1]);
    }

    private static IReadOnlyList<string> Row(params string[] cells) => cells;

    [Theory]
    [InlineData("+1")]
    [InlineData("-1")]
    [InlineData("@SUM(A1)")]
    [InlineData("\tx")]
    [InlineData("\rx")]
    public void A_cell_that_starts_like_a_formula_is_defused_for_each_trigger_character(string value)
    {
        var q = DeviceCsv.Quote(value);
        Assert.True(q.TrimStart('"').StartsWith('\''), q);
        Assert.DoesNotContain(q[0], new[] { '+', '-', '@', '\t', '\r' });
    }

    [Fact]
    public void The_csv_of_no_rows_is_the_header_alone()
    {
        var bytes = DeviceCsv.ToBytes([]);
        Assert.Equal(DeviceCsv.Line(DeviceCsv.Headers) + "\r\n", new UTF8Encoding(false).GetString(bytes, 3, bytes.Length - 3));
    }

    [Fact]
    public void Capacity_and_time_are_human_readable()
    {
        Assert.Equal("14,9 GB", DeviceText.CapacityRo(14_900_000_000));
        Assert.Equal("", DeviceText.CapacityRo(0));
        Assert.Equal("512 B", DeviceText.CapacityRo(512));
        Assert.Equal("", DeviceText.LocalTime(null));
        Assert.Matches(@"^\d{2}\.\d{2}\.\d{4} \d{2}:\d{2}:\d{2}$", DeviceText.LocalTime(T0));
        Assert.Equal(T0.ToLocalTime().ToString("dd.MM.yyyy HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture), DeviceText.LocalTime(T0));
    }

    // ---- the monitor without the right to seal ---------------------------------------------------------------------------------------------

    [Fact]
    public void The_real_refusal_of_the_audit_when_the_key_cannot_be_read_is_recognised()
    {
        var svc = new AuthService(Path.Combine(_dir, "auth2"));
        svc.AuditOperation(null, "test.first", "x=1");
        HashChain.RegisterHeadSeal(svc.AuditPath, null);    // sealed, but the key cannot be read
        try
        {
            var ex = Assert.ThrowsAny<Exception>(() => svc.AuditOperation(null, "test.second", "x=2"));
            Assert.IsType<SealingKeyUnavailableException>(ex);                          // a type, not a guess from the words of the message
            Assert.IsAssignableFrom<UnauthorizedAccessException>(ex);
            Assert.True(new AuditRefusal(ex.Message, KeyUnavailable: true).KeyUnavailable);
            AuditRefusal? plain = "discul este plin";
            Assert.False(plain!.KeyUnavailable);
        }
        finally { HashChain.RegisterHeadSeal(svc.AuditPath, [1, 2, 3]); }
    }

    [Fact]
    public void The_journal_remembers_that_the_monitor_ran_without_the_right_to_seal()
    {
        var j = new DeviceJournal();
        Assert.False(SealingHealth.InPeriod(j, T0, T0.AddDays(1)));
        Assert.True(SealingHealth.Note(j, T0.AddHours(1)));
        Assert.False(SealingHealth.Note(j, T0.AddHours(1).AddSeconds(5)));      // a retry a moment later does not rewrite the journal
        Assert.True(SealingHealth.Note(j, T0.AddHours(2)));
        Assert.Equal(T0.AddHours(1), j.SealingRefused!.FirstUtc);
        Assert.Equal(T0.AddHours(2), j.SealingRefused.LastUtc);
        Assert.True(SealingHealth.InPeriod(j, T0, T0.AddDays(1)));
        Assert.False(SealingHealth.InPeriod(j, T0.AddDays(1), T0.AddDays(2)));
        var back = JsonSerializer.Deserialize<DeviceJournal>(JsonSerializer.Serialize(j))!;
        Assert.Equal(T0.AddHours(2), back.SealingRefused!.LastUtc);
        Assert.Equal(T0.AddHours(2), j.Copy().SealingRefused!.LastUtc);
        Assert.Contains("FĂRĂ DREPT DE SIGILARE", SealingHealth.Evidence(j.SealingRefused));
        Assert.Contains("porniți LogAnalyzer ca administrator", SealingHealth.Banner);
        Assert.Contains("D05 rămâne DE VERIFICAT", SealingHealth.Banner);
        Assert.Null(JsonSerializer.Deserialize<DeviceJournal>("{\"version\":2}")!.SealingRefused);
    }
}
