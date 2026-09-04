using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using XAU_Kinetic.Desktop.Models;

namespace XAU_Kinetic.Desktop.Services
{
    public interface ITradingEngineService
    {
        EngineStatus Status { get; }
        event EventHandler<EngineStatus>? StatusChanged;
        
        Task<bool> StartEngineAsync(string configPath, bool mockMode, CancellationToken cancellationToken = default);
        Task StopEngineAsync();
        Task TriggerEmergencyKillSwitchAsync();

        Task<AccountState> GetAccountStateAsync();
        Task<IReadOnlyList<ActivePosition>> GetActivePositionsAsync();
        Task<IReadOnlyList<AuditLogRecord>> GetAuditLogsAsync(int limit = 50);
        Task<(bool IsValid, string Message)> VerifyAuditChainIntegrityAsync();
    }
}
