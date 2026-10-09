namespace LogAnalyzer.Dfir.Analysis;

/// <summary>
/// The organisation's working hours (local clock, from the procedure profile - WP15). <c>null</c> everywhere it is accepted
/// means "not defined yet". Even when defined, activity outside it is reported as a comparison with the profile, never as an
/// automatic penalty: shift work, on-call and maintenance are normal.
/// </summary>
public sealed record WorkingHours(TimeOnly Start, TimeOnly End)
{
    /// <summary>True when the time of day is inside the range (a range that wraps midnight, e.g. 22:00-06:00, is supported).</summary>
    public bool Contains(TimeOnly t) => Start <= End ? t >= Start && t <= End : t >= Start || t <= End;

    public string Describe() => $"{Start:HH\\:mm}-{End:HH\\:mm}";
}
