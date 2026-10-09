using System.Text.Json.Serialization;
using LogAnalyzer.Dfir.Auth;
using LogAnalyzer.Dfir.Model;

namespace LogAnalyzer.Dfir.Registers;

/// <summary>One person with ONE clearance (a person with several clearances has several rows). Cells are text as typed; lists are separated by ; or |.</summary>
public sealed class UserRow
{
    public string Person { get; set; } = "";
    public string Accounts { get; set; } = "";
    public string Sids { get; set; } = "";
    public string Clearance { get; set; } = "";
    public string ClearanceValidFrom { get; set; } = "";
    public string ClearanceValidTo { get; set; } = "";
    public string NeedToKnow { get; set; } = "";
    public string ZonesAllowed { get; set; } = "";

    [JsonIgnore] public List<string> AccountList => RegisterRules.SplitList(Accounts);
    [JsonIgnore] public List<string> SidList => RegisterRules.SplitList(Sids);
    [JsonIgnore] public List<string> ZoneList => RegisterRules.SplitList(ZonesAllowed);
    [JsonIgnore] public SecrecyLevel? Level => SecrecyLevels.TryParse(Clearance, out var l) ? l : null;
}

/// <summary>
/// Users and clearances (owner decision 17): clearances and need-to-know are entered MANUALLY by the global administrator. Only the signed-in global administrator
/// may edit it (decision 33; <see cref="OperatorIdentity"/>); the register stays data plus an audit chain (see <see cref="EditNotice"/>).
/// </summary>
public sealed class UsersRegister : IRegisterData
{
    public const string CurrentSchemaVersion = "1.0";
    public const string DefaultFileName = "users.json";
    public const string EditNotice = "editare permisă numai administratorului global autentificat (card + PIN; administratorul principal poate folosi și contul + parola)";
    public static readonly string[] ColumnNames = ["Person", "Accounts", "Sids", "Clearance", "ClearanceValidFrom", "ClearanceValidTo", "NeedToKnow", "ZonesAllowed"];

    [JsonPropertyName("schema_version")] public string SchemaVersion { get; set; } = CurrentSchemaVersion;
    public DateTimeOffset? UpdatedUtc { get; set; }
    public List<UserRow> Rows { get; set; } = [];

    [JsonIgnore] public string Kind => "users";
    [JsonIgnore] public string Title => "Registru utilizatori";
    [JsonIgnore] public string FileName => DefaultFileName;
    [JsonIgnore] public IReadOnlyList<string> Columns => ColumnNames;
    [JsonIgnore] public bool IsDefined => Rows.Count > 0;

    public List<string[]> Cells() => Rows.Select(r => new[] { r.Person, r.Accounts, r.Sids, r.Clearance, r.ClearanceValidFrom, r.ClearanceValidTo, r.NeedToKnow, r.ZonesAllowed }).ToList();

    public void SetCells(IEnumerable<IReadOnlyList<string>> rows) =>
        Rows = rows.Select(r => Enumerable.Range(0, ColumnNames.Length).Select(i => i < r.Count ? (r[i] ?? "").Trim() : "").ToArray())
                   .Select(c => new UserRow { Person = c[0], Accounts = c[1], Sids = c[2], Clearance = c[3], ClearanceValidFrom = c[4], ClearanceValidTo = c[5], NeedToKnow = c[6], ZonesAllowed = c[7] })
                   .ToList();

    public List<(string Message, bool IsError)> ValidateRow(IReadOnlyList<string> c, IReadOnlyList<string[]> previousRows)
    {
        var m = new List<(string, bool)>();
        void Err(string s) => m.Add((s, true));
        void Warn(string s) => m.Add((s, false));
        bool Empty(string v) => string.IsNullOrWhiteSpace(v);
        if (Empty(c[0])) Err("persoana lipsește");
        var accounts = RegisterRules.SplitList(c[1]);
        var sids = RegisterRules.SplitList(c[2]);
        if (accounts.Count == 0 && sids.Count == 0) Err("cel puțin un cont (sau un SID) este obligatoriu");
        foreach (var a in accounts.Where(RegisterRules.BadAccount)) Err($"contul „{a}” trebuie scris DOMENIU\\utilizator sau utilizator");
        foreach (var s in sids.Where(s => !RegisterRules.IsSid(s))) Err($"SID-ul „{s}” nu este de forma S-1-5-21-…");
        if (Empty(c[3]))
        {
            if (!Empty(c[0])) Warn("nicio abilitare înregistrată: persoana nu are nivel de acces la informații clasificate în acest registru");
        }
        else if (SecrecyLevels.IsUnclassifiedMarking(c[3])) Err($"„{c[3]}” nu este un nivel de clasificare: nu există nivel neclasificat (decizia 21); lăsați câmpul gol dacă persoana nu are abilitare");
        else if (!SecrecyLevels.TryParse(c[3], out _)) Err($"nivelul de acces „{c[3]}” nu este cunoscut (NATO, UE sau nivel național; decizia 21)");
        DateOnly from = default, to = default;
        bool hasFrom = !Empty(c[4]), hasTo = !Empty(c[5]);
        bool okFrom = !hasFrom || RegisterRules.TryDate(c[4], out from), okTo = !hasTo || RegisterRules.TryDate(c[5], out to);
        if (!okFrom) Err($"data „{c[4]}” nu este în formatul yyyy-MM-dd (abilitare valabilă de la)");
        if (!okTo) Err($"data „{c[5]}” nu este în formatul yyyy-MM-dd (abilitare valabilă până la)");
        if (hasFrom && hasTo && okFrom && okTo && to < from) Err("sfârșitul valabilității precedă începutul");
        if (!Empty(c[3]) && previousRows.Any(p => p[0].Trim().Equals(c[0].Trim(), StringComparison.OrdinalIgnoreCase) && p[3].Trim().Equals(c[3].Trim(), StringComparison.OrdinalIgnoreCase)))
            Warn($"persoana „{c[0]}” are același nivel înregistrat de mai multe ori");
        return m;
    }

    public List<RegisterIssue> Validate()
    {
        var issues = new List<RegisterIssue>();
        var cells = Cells();
        for (int i = 0; i < cells.Count; i++)
            foreach (var (msg, err) in ValidateRow(cells[i], cells.Take(i).ToList())) issues.Add(new RegisterIssue(Title, i + 1, msg, err));
        return issues;
    }

    /// <summary>Rows whose account list or SID list names this account (DOMAIN\user, user) or SID.</summary>
    public IEnumerable<UserRow> ForAccount(string account, string sid = "") =>
        Rows.Where(r => r.AccountList.Any(a => RegisterRules.SameAccount(a, account)) ||
                        (sid.Length > 0 && r.SidList.Any(s => s.Equals(sid.Trim(), StringComparison.OrdinalIgnoreCase))));
}
