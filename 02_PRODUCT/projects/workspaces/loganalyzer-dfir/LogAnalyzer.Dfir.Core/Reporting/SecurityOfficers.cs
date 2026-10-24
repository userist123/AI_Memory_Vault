using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;

namespace LogAnalyzer.Dfir.Reporting;

/// <summary>Fingerprint of a security officer = SHA-256 of their qualified signature certificate (DER), shown as 16 groups of 4 hex characters.</summary>
public static class OfficerFingerprint
{
    public static string Of(X509Certificate2 cert) => Of(cert.RawData);

    public static string Of(byte[] certDer)
    {
        var hex = Convert.ToHexString(SHA256.HashData(certDer));
        return string.Join("-", Enumerable.Range(0, 16).Select(i => hex.Substring(i * 4, 4)));
    }

    /// <summary>Upper-case hex without separators; empty when the text is not 64 hex characters.</summary>
    public static string Normalize(string? fingerprint)
    {
        var s = new string((fingerprint ?? "").Where(c => !char.IsWhiteSpace(c) && c != '-' && c != ':').Select(char.ToUpperInvariant).ToArray());
        return s.Length == 64 && s.All(Uri.IsHexDigit) ? s : "";
    }

    public static bool Same(string? a, string? b) => Normalize(a) is { Length: > 0 } x && x == Normalize(b);

    /// <summary>The CN of the certificate subject (or the whole subject when there is no CN).</summary>
    public static string CommonName(X509Certificate2 cert) => cert.GetNameInfo(X509NameType.SimpleName, false) is { Length: > 0 } cn ? cn : cert.Subject;
}

/// <summary>A registered security officer: name, qualified signature certificate (DER base64) and its fingerprint, and how it was registered.</summary>
public sealed class SecurityOfficerRow
{
    public string Name { get; set; } = "";
    public string Fingerprint { get; set; } = "";
    public string Certificate { get; set; } = "";
    public DateTimeOffset RegisteredUtc { get; set; }
    public string Basis { get; set; } = "";
    /// <summary>For an approved officer: the fingerprint of the officer who signed the registration request.</summary>
    public string ApprovedBy { get; set; } = "";
    /// <summary>For an approved officer: the registration request PDF signed by <see cref="ApprovedBy"/> (base64), kept so the approval can be re-checked any time.</summary>
    public string ApprovalPdf { get; set; } = "";
    /// <summary>For an approved officer: SHA-256 of the request PDF as generated here (the unsigned part of <see cref="ApprovalPdf"/>).</summary>
    public string ApprovalRequestSha256 { get; set; } = "";
}

/// <summary>A registration request waiting for the signature of an already registered officer.</summary>
public sealed class PendingOfficerRequest
{
    public string Name { get; set; } = "";
    public string Fingerprint { get; set; } = "";
    public string Certificate { get; set; } = "";
    public string RequestPath { get; set; } = "";
    public string RequestSha256 { get; set; } = "";
    public DateTimeOffset CreatedUtc { get; set; }
}

public sealed class OfficerStoreData
{
    public List<SecurityOfficerRow> Officers { get; set; } = [];
    public List<PendingOfficerRequest> Pending { get; set; } = [];
}

/// <summary>
/// The officer list, in its own file (%PROGRAMDATA%\LogAnalyzer\officers\security_officers.json), written only by the officer flows, never by the
/// procedure-profile editor or a profile import. Every flow re-reads the file before changing it. The file proves nothing by itself: each row is trusted
/// only through <see cref="OfficerRegistry.Trusted"/>, and every change is in the sealed audit (check A13).
/// </summary>
public sealed class OfficerStore(string? path = null)
{
    public string Path { get; } = path ?? System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "LogAnalyzer", "officers", "security_officers.json");

    public static OfficerStore Shared { get; } = new();

    public string RequestsDir => System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Path)!, "requests");

    public OfficerStoreData Load()
    {
        try
        {
            var d = File.Exists(Path) ? JsonSerializer.Deserialize<OfficerStoreData>(File.ReadAllText(Path), LogAnalyzer.Dfir.IO.Json.Options) ?? new() : new();
            d.Officers ??= []; d.Pending ??= [];
            return d;
        }
        catch (JsonException) { return new(); }
    }

    public void Save(OfficerStoreData data) => LogAnalyzer.Dfir.IO.Json.Write(Path, data);
}

/// <summary>
/// The "Ofițeri de securitate" list, by certificate fingerprint. The first officer is registered on trust and that trust rests on the fingerprint written on
/// the proces-verbal; every later officer needs a registration request PDF (generated here, naming the new certificate's fingerprint) signed by an officer
/// already trusted. A row not reachable through valid approvals from the first one is not trusted, whatever the file says.
/// </summary>
public static class OfficerRegistry
{
    public const string BasisFirst = "primul ofițer (amprentă pe procesul-verbal)", BasisApproved = "aprobat de un ofițer înregistrat";

    /// <summary>The marker the request PDF carries in its original bytes (a PDF comment), binding the approval to this certificate.</summary>
    public static string RequestMarker(string fingerprint) => "% LogAnalyzer-officer-request " + OfficerFingerprint.Normalize(fingerprint);

    /// <summary>The certificate as a (not yet added) row; refuses a certificate without non-repudiation (an authentication certificate).</summary>
    public static SecurityOfficerRow Identity(X509Certificate2 cert, string? name = null)
    {
        if (!QualifiedSignatureTrust.HasNonRepudiation(cert))
            throw new InvalidOperationException($"Certificatul „{cert.Subject}” nu are utilizarea „nerepudiere” (non-repudiation): nu este certificatul de semnătură calificată, ci probabil cel de autentificare.");
        return new() { Name = string.IsNullOrWhiteSpace(name) ? OfficerFingerprint.CommonName(cert) : name.Trim(), Certificate = Convert.ToBase64String(cert.RawData), Fingerprint = OfficerFingerprint.Of(cert) };
    }

    /// <summary>Registers the first officer; refused when any officer is already listed (the next ones need an approval).</summary>
    public static SecurityOfficerRow RegisterFirst(OfficerStoreData data, SecurityOfficerRow identity, DateTimeOffset nowUtc)
    {
        if (data.Officers.Count > 0) throw new InvalidOperationException("Există deja ofițeri de securitate înregistrați: un ofițer nou se înregistrează doar cu o cerere semnată de un ofițer existent.");
        identity.RegisteredUtc = nowUtc.ToUniversalTime(); identity.Basis = BasisFirst; identity.ApprovedBy = ""; identity.ApprovalPdf = "";
        data.Officers.Add(identity);
        return identity;
    }

    public static PendingOfficerRequest AddRequest(OfficerStoreData data, SecurityOfficerRow identity, string requestPath, DateTimeOffset nowUtc)
    {
        if (data.Officers.Any(r => OfficerFingerprint.Same(r.Fingerprint, identity.Fingerprint))) throw new InvalidOperationException($"Ofițerul cu amprenta {identity.Fingerprint} este deja înregistrat.");
        data.Pending.RemoveAll(p => OfficerFingerprint.Same(p.Fingerprint, identity.Fingerprint));
        var p = new PendingOfficerRequest
        {
            Name = identity.Name, Fingerprint = identity.Fingerprint, Certificate = identity.Certificate, RequestPath = requestPath,
            RequestSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(requestPath))), CreatedUtc = nowUtc.ToUniversalTime(),
        };
        data.Pending.Add(p);
        return p;
    }

    /// <summary>
    /// Completes a pending request with its PDF signed by a trusted officer: the signature must verify, its original bytes must be the request generated
    /// here (SHA-256) and carry the new certificate's marker, and the signer must be a trusted officer. Returns the new row.
    /// </summary>
    public static SecurityOfficerRow CompleteRequest(OfficerStoreData data, byte[] signedRequestPdf, ICmsDetachedVerifier cms, DateTimeOffset nowUtc)
    {
        var pdf = PdfSignatureCheck.Check(signedRequestPdf, cms);
        if (pdf.Problem is { } why) throw new InvalidOperationException("Cererea semnată nu este valabilă: " + why);
        var pending = data.Pending.FirstOrDefault(p => pdf.OriginalLengthFor(p.RequestSha256) is not null)
            ?? throw new InvalidOperationException("Fișierul semnat nu este o cerere de înregistrare generată pe această stație și aflată în așteptare.");
        if (ApprovalProblem(pdf, pending.Fingerprint, pending.RequestSha256) is { } bad) throw new InvalidOperationException("Cererea semnată nu este valabilă: " + bad);
        var signerFp = OfficerFingerprint.Of(pdf.Signer!);
        if (!Trusted(data.Officers, cms, out _).Any(t => OfficerFingerprint.Same(t.Fingerprint, signerFp)))
            throw new InvalidOperationException($"Cererea este semnată cu certificatul cu amprenta {signerFp}, care nu aparține unui ofițer de securitate înregistrat.");
        var row = new SecurityOfficerRow
        {
            Name = pending.Name, Fingerprint = pending.Fingerprint, Certificate = pending.Certificate, RegisteredUtc = nowUtc.ToUniversalTime(), Basis = BasisApproved,
            ApprovedBy = signerFp, ApprovalPdf = Convert.ToBase64String(signedRequestPdf), ApprovalRequestSha256 = pending.RequestSha256,
        };
        data.Officers.Add(row);
        data.Pending.Remove(pending);
        return row;
    }

    /// <summary>Why a signed request is not the approval of <paramref name="fingerprint"/>: its original part must be the generated request, carry the marker and be followed only by the signature.</summary>
    private static string? ApprovalProblem(PdfSignatureCheck pdf, string fingerprint, string requestSha256)
    {
        if (pdf.Problem is { } why) return why;
        if (pdf.OriginalLengthFor(requestSha256) is not { } len) return "partea semnată nu este cererea generată";
        if (!pdf.PrefixContains(len, RequestMarker(fingerprint))) return "cererea semnată nu este cea pentru acest certificat";
        return PdfUpdateInspector.Check(pdf.Bytes, len);
    }

    /// <summary>
    /// The rows that are trusted: the first row (registered as first) and every row whose approval PDF verifies, names its certificate and is signed by a row
    /// trusted before it (the approval PDF is re-verified every time with <paramref name="cms"/>).
    /// </summary>
    public static IReadOnlyList<SecurityOfficerRow> Trusted(IReadOnlyList<SecurityOfficerRow>? rows, ICmsDetachedVerifier cms, out List<string> problems)
    {
        problems = [];
        var trusted = new List<SecurityOfficerRow>();
        rows ??= [];
        for (int i = 0; i < rows.Count; i++)
        {
            var r = rows[i];
            string? realFp = null;
            try { realFp = OfficerFingerprint.Of(Convert.FromBase64String(r.Certificate ?? "")); } catch (FormatException) { }
            if (realFp is null || !OfficerFingerprint.Same(realFp, r.Fingerprint)) { problems.Add($"„{r.Name}”: amprenta scrisă nu corespunde certificatului."); continue; }
            if (i == 0 && r.Basis == OfficerRegistry.BasisFirst) { trusted.Add(r); continue; }
            bool ok = r.Basis == BasisApproved && trusted.Any(t => OfficerFingerprint.Same(t.Fingerprint, r.ApprovedBy));
            if (ok)
            {
                // a planted or damaged approval file makes only this row untrusted; reading the list never fails because of it
                try
                {
                    var pdf = PdfSignatureCheck.Check(Convert.FromBase64String(r.ApprovalPdf ?? ""), cms);
                    ok = pdf.Signer is not null && ApprovalProblem(pdf, r.Fingerprint, r.ApprovalRequestSha256 ?? "") is null
                         && OfficerFingerprint.Same(OfficerFingerprint.Of(pdf.Signer), r.ApprovedBy);
                }
                catch (Exception ex) when (ex is FormatException or ArgumentException or InvalidOperationException or NotSupportedException or CryptographicException) { ok = false; }
            }
            if (ok) trusted.Add(r);
            else problems.Add($"„{r.Name}” (amprenta {r.Fingerprint}) nu are o aprobare validă a unui ofițer înregistrat: nu este de încredere.");
        }
        return trusted;
    }
}
