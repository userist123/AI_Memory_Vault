namespace LogAnalyzer.Core.Interfaces
{
    /// <summary>Outcome of an action that changes the station: success is claimed only when the effect was checked afterwards.</summary>
    public class DefenseActionResult
    {
        public const string Verified = "VERIFIED";
        public const string NotVerified = "NOT_VERIFIED";
        public const string Failed = "FAILED";
        public const string Rejected = "REJECTED";
        /// <summary>The signed-in account may not change the station (operator role); nothing was attempted.</summary>
        public const string NotAuthorized = "NOT_AUTHORIZED";
        /// <summary>The capability is not part of this edition (nothing was attempted).</summary>
        public const string Unavailable = "UNAVAILABLE";

        /// <summary>True only when the change was applied AND its effect was checked afterwards.</summary>
        public bool Success { get; set; }
        public string Status { get; set; } = Failed;
        public string Message { get; set; } = string.Empty;
        /// <summary>Commands run, their exit codes and output, in order.</summary>
        public string ExecutionDetails { get; set; } = string.Empty;
        /// <summary>The independent re-check (shared response contract); null only for a result that was never attempted.</summary>
        public LogAnalyzer.Dfir.Response.ResponseOutcome? Verification { get; set; }

        /// <summary>
        /// Writes the outcome and its re-check evidence to the case audit. A failed write is kept on <see cref="Verification"/> and turns
        /// <see cref="Success"/> off: an unaudited response is never shown as a success.
        /// </summary>
        public DefenseActionResult RecordTo(System.Action<string, string>? caseAudit)
        {
            Verification ??= LogAnalyzer.Dfir.Response.ResponseOutcome.NotAttempted("defense", "apply",
                Status switch
                {
                    Rejected => LogAnalyzer.Dfir.Response.ResponseStatus.Rejected,
                    NotAuthorized => LogAnalyzer.Dfir.Response.ResponseStatus.NotAuthorized,
                    Unavailable => LogAnalyzer.Dfir.Response.ResponseStatus.Unavailable,
                    Verified => LogAnalyzer.Dfir.Response.ResponseStatus.NotVerified,   // a VERIFIED claim without re-check evidence is not accepted
                    NotVerified => LogAnalyzer.Dfir.Response.ResponseStatus.NotVerified,
                    _ => LogAnalyzer.Dfir.Response.ResponseStatus.Failed,
                }, Message);
            if (Status == Verified && Verification.Status != LogAnalyzer.Dfir.Response.ResponseStatus.Verified) Status = NotVerified;
            LogAnalyzer.Dfir.Response.ResponseVerifier.Record(Verification, caseAudit);
            if (Verification.AuditError is not null) Message += " ATENȚIE: " + Verification.AuditError + ".";
            Success = Status == Verified && Verification.IsSuccess;
            return this;
        }
    }

    /// <summary>Station-wide firewall actions started by the operator. Implemented in LogAnalyzer.Response (unclassified edition only).</summary>
    public interface IHostDefense
    {
        DefenseActionResult IsolateHostFromNetwork();
        DefenseActionResult RestoreNetworkAccess();
        DefenseActionResult BlockMaliciousIoC(string iocTarget);
    }
}
