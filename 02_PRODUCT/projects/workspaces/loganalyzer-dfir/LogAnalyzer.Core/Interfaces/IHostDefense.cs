namespace LogAnalyzer.Core.Interfaces
{
    /// <summary>Outcome of an action that changes the station: success is claimed only when the effect was checked afterwards.</summary>
    public class DefenseActionResult
    {
        public const string Verified = "VERIFIED";
        public const string NotVerified = "NOT_VERIFIED";
        public const string Failed = "FAILED";
        public const string Rejected = "REJECTED";
        /// <summary>The capability is not part of this edition (nothing was attempted).</summary>
        public const string Unavailable = "UNAVAILABLE";

        /// <summary>True only when the change was applied AND its effect was checked afterwards.</summary>
        public bool Success { get; set; }
        public string Status { get; set; } = Failed;
        public string Message { get; set; } = string.Empty;
        /// <summary>Commands run, their exit codes and output, in order.</summary>
        public string ExecutionDetails { get; set; } = string.Empty;
    }

    /// <summary>Station-wide firewall actions started by the operator. Implemented in LogAnalyzer.Response (unclassified edition only).</summary>
    public interface IHostDefense
    {
        DefenseActionResult IsolateHostFromNetwork();
        DefenseActionResult RestoreNetworkAccess();
        DefenseActionResult BlockMaliciousIoC(string iocTarget);
    }
}
