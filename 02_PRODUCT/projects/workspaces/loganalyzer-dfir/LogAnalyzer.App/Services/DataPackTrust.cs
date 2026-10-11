namespace LogAnalyzer.UI.Services
{
    /// <summary>
    /// Public keys that may sign offline detection data packs (docs/dfir/ANALYSIS_2026-10-11_APPLICATION_UPDATES.md). Empty until the owner runs
    /// <c>LogAnalyzer.KeyGen datapack-keygen</c> on an offline machine and pastes the printed public key here; compiled in so that nobody on a station
    /// can add a key. Two entries allow a rotation (current + next).
    /// </summary>
    public static class DataPackTrust
    {
        public static readonly string[] PublicKeys = [];

        public static void Apply() => LogAnalyzer.Dfir.Analysis.DataPack.TrustedKeys = PublicKeys;
    }
}
