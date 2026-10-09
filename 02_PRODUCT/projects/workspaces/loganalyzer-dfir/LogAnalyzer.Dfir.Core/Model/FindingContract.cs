using System.Text.Json;
using System.Text.Json.Serialization;

namespace LogAnalyzer.Dfir.Model;

/// <summary>JSON/spec name of a contract enum: SNAKE_UPPER ("NOT_ASSESSED"). Reading accepts the spec name, the C# name or the legacy number.</summary>
public sealed class SpecEnumConverter<T> : JsonConverter<T> where T : struct, Enum
{
    public static string Name(T v) => ContractNames.Snake(v.ToString());

    public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Number && reader.TryGetInt32(out var n) && Enum.IsDefined(typeof(T), n)) return (T)(object)n;
        var s = reader.GetString() ?? "";
        foreach (var v in Enum.GetValues<T>())
            if (string.Equals(Name(v), s, StringComparison.OrdinalIgnoreCase) || string.Equals(v.ToString(), s, StringComparison.OrdinalIgnoreCase)) return v;
        throw new JsonException($"Valoare necunoscută pentru {typeof(T).Name}: '{s}'.");
    }

    public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options) => writer.WriteStringValue(Name(value));
}

public static class ContractNames
{
    public static string Snake(string pascal)
    {
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < pascal.Length; i++)
        {
            if (i > 0 && char.IsUpper(pascal[i])) sb.Append('_');
            sb.Append(char.ToUpperInvariant(pascal[i]));
        }
        return sb.ToString();
    }

    public static string ToSpec(this SemanticType t) => Snake(t.ToString());
    public static string ToSpec(this StandardState s) => Snake(s.ToString());
    public static string ToSpec(this OperationState s) => Snake(s.ToString());
    public static string ToSpec(this ParserHealth s) => Snake(s.ToString());
    public static string ToSpec(this SourceAvailability s) => Snake(s.ToString());
}

/// <summary>What a statement is about (program requirements §5). Presence is never execution; correlation is never causation.</summary>
[JsonConverter(typeof(SpecEnumConverter<SemanticType>))]
public enum SemanticType { Observation, Presence, Execution, Configuration, Correlation, Inference, Attribution }

/// <summary>The ten standard states (program requirements §20, UX contract §7). Added alongside <see cref="Classification"/>, never replacing it.</summary>
[JsonConverter(typeof(SpecEnumConverter<StandardState>))]
public enum StandardState { Observed, Correlated, Supported, Verified, Inferred, Unproven, Contradicted, Rejected, Unknown, NotAssessed }

/// <summary>State of an operation (UX contract §20). "Analysis completed" is not "finding verified".</summary>
[JsonConverter(typeof(SpecEnumConverter<OperationState>))]
public enum OperationState { NotStarted, Running, Completed, Partial, Failed, Cancelled, Blocked }

/// <summary>Health of one source/parser (lessons learned §5). Vocabulary only; coverage wiring comes with the case/Home model.</summary>
[JsonConverter(typeof(SpecEnumConverter<ParserHealth>))]
public enum ParserHealth { Available, Collected, Parsed, Partial, Unsupported, Invalid, Error, Blocked, NotPresent, NotEnabled }

/// <summary>Availability of a source in a case (lessons learned §98). "Source absent" never means "event did not happen".</summary>
[JsonConverter(typeof(SpecEnumConverter<SourceAvailability>))]
public enum SourceAvailability { Available, NotAvailable, NotEnabled, NotApplicable, NotCollected }

/// <summary>Result of independent verification. Until the verifier exists (stage-2 WP4) every finding is NOT_ASSESSED, with the reason.</summary>
public sealed record FindingVerification(StandardState State, string Reason, string Verifier = "", DateTimeOffset? AssessedUtc = null)
{
    public const string NoVerifierReason = "Nicio verificare independentă nu a rulat: modulul de verificare nu există încă; starea nu e un verdict.";
    public static FindingVerification NotAssessed(string? reason = null) => new(StandardState.NotAssessed, reason ?? NoVerifierReason);
}

/// <summary>Where a finding came from: rule, producer, evidence (with hashes) and the parsers that produced the rows it rests on.</summary>
public sealed record FindingProvenance(
    string RuleId, string RuleVersion, string Producer, string ApplicationVersion,
    IReadOnlyList<string> EvidenceIds, IReadOnlyList<string> EvidenceSha256, IReadOnlyList<string> Parsers, string ContractVersion);

/// <summary>One step in how a finding got its present form. <see cref="TimeUtc"/> is null for steps that are pure functions of the data.</summary>
public sealed record FindingAuditEntry(string Step, string Actor, string Detail, DateTimeOffset? TimeUtc = null);
