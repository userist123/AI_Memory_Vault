using System;

namespace XAU_Kinetic.Desktop.Models
{
    public record AccountState(
        long Login,
        double Balance,
        double Equity,
        double Margin,
        double FreeMargin,
        double Profit,
        string Currency
    );

    public record ActivePosition(
        long Ticket,
        string Symbol,
        string Type,
        double Volume,
        double OpenPrice,
        double StopLoss,
        double TakeProfit,
        double Profit,
        DateTime Timestamp
    );

    public record AuditLogRecord(
        long Id,
        string EventId,
        DateTime Timestamp,
        string EventType,
        string PayloadJson,
        string PrevHash,
        string CurrentHash
    );

    public record RiskMetrics(
        double DailyDrawdownPercentage,
        double MaxDailyDrawdownThreshold,
        double TotalSymbolExposureLots,
        double MaxSymbolExposureThreshold,
        bool IsCircuitBroken,
        string CircuitReason
    );

    public enum EngineStatus
    {
        Stopped,
        Initializing,
        Running,
        CircuitBroken,
        Error
    }
}
