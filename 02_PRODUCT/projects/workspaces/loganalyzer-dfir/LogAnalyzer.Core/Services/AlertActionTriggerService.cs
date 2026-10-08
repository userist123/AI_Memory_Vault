using System;
using System.Diagnostics;
using System.IO;

namespace LogAnalyzer.Core.Services
{
    public class ContainmentExecutionResult
    {
        public const string NotExecuted = "NOT_EXECUTED";

        public bool Success { get; set; }
        public string Status { get; set; } = NotExecuted;
        public string ActionName { get; set; } = string.Empty;
        public string OutputLog { get; set; } = string.Empty;
        /// <summary>When the operator asked for the action (not when it ran: it does not run).</summary>
        public DateTime RequestedAtUtc { get; set; }
    }

    /// <summary>
    /// Account/station containment playbook. There is no agent or script behind it, so it never executes anything and never
    /// reports success: it returns NOT_EXECUTED with the commands the operator may run by hand. Real, verified containment
    /// of one program is in the "Izolare procese suspecte" tab (Dfir.Windows/Containment).
    /// </summary>
    public class AlertActionTriggerService
    {
        public ContainmentExecutionResult ExecuteContainmentScript(string actionName, string targetEntity, bool isAirGapped)
        {
            var target = string.IsNullOrWhiteSpace(targetEntity) ? "<țintă nedefinită>" : targetEntity;
            return new ContainmentExecutionResult
            {
                Success = false,
                Status = ContainmentExecutionResult.NotExecuted,
                ActionName = actionName,
                RequestedAtUtc = DateTime.UtcNow,
                OutputLog =
                    $"[{ContainmentExecutionResult.NotExecuted}] Acțiunea '{actionName}' pentru '{target}' NU a fost executată.\n" +
                    (isAirGapped ? "Mod AirGapped: aplicația nu modifică stația din acest ecran.\n"
                                 : "Mod Network: nu există niciun agent EDR conectat care să primească comanda.\n") +
                    "Comenzi pe care operatorul le poate rula manual, după verificare:\n" +
                    $"  net user {target} /active:no\n" +
                    "  wevtutil sl Security /e:true\n" +
                    "Pentru izolarea unui program cu verificare folosiți fila „Izolare procese suspecte”.",
            };
        }
    }
}
