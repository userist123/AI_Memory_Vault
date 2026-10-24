using LogAnalyzer.Dfir.Policy;

namespace LogAnalyzer.Dfir.Windows.Policy;

public enum PolicyStepKind { Open, Register, Validate, DryRun, Approve, WaitForApprover, ComputePlan, Apply, Assess, Nothing }

/// <summary>
/// The one thing to do next on the policy screen, in words, so the operator does not have to know the lifecycle (owner 2026-10-11: „să spună
/// omului: ca să faci aia trebuie să faci aia"). <see cref="Button"/> is empty when the operator cannot do the step from this account.
/// </summary>
public sealed record PolicyNextStep(PolicyStepKind Kind, string Text, string Button)
{
    public bool CanAct => Button.Length > 0;

    public static PolicyNextStep For(bool policyOpen, PolicyRecord? record, string currentOperator, bool approvalReasonGiven, bool planComputed, int planToChange, bool canApply)
    {
        if (!policyOpen)
            return new(PolicyStepKind.Open, "Pasul 1: deschideți politica. Alegeți fișierul primit (de exemplu un backup GPO arhivat .zip) sau folderul backup-ului GPO.", "Deschide politica…");
        if (record is null)
            return new(PolicyStepKind.Register, "Pasul următor: înregistrați politica în biblioteca aplicației. Se păstrează amprenta ei (SHA-256), ca nimeni să nu o poată schimba pe ascuns.", "Înregistrează politica");
        switch (record.Status)
        {
            case PolicyStatus.Draft:
                return new(PolicyStepKind.Validate, "Pasul următor: validați politica. Aplicația verifică dacă fiecare regulă este completă și poate fi citită.", "Validează");
            case PolicyStatus.Validated:
                return new(PolicyStepKind.DryRun, "Pasul următor: rulare de probă. Aplicația citește fiecare setare de pe stație, fără să schimbe nimic.", "Rulare de probă");
            case PolicyStatus.Tested:
                if (Same(currentOperator, record.RegisteredBy) || Same(currentOperator, record.Author))
                    return new(PolicyStepKind.WaitForApprover,
                        $"Pasul următor: aprobarea. Ea trebuie făcută de altă persoană, din alt cont Windows decât {record.RegisteredBy}, care a înregistrat politica (nimeni nu-și aprobă propria politică). " +
                        "Aprobatorul se autentifică în Windows cu contul lui, deschide aceeași politică și apasă „Aprobă”.", "");
                return approvalReasonGiven
                    ? new(PolicyStepKind.Approve, "Pasul următor: aprobați politica. Justificarea scrisă se păstrează în istoric.", "Aprobă")
                    : new(PolicyStepKind.Approve, "Pasul următor: scrieți în câmpul „Justificare aprobare” de ce aprobați politica (de exemplu numărul documentului care o cere), apoi apăsați „Aprobă”.", "");
            case PolicyStatus.Approved:
                if (!planComputed)
                    return new(PolicyStepKind.ComputePlan, "Pasul următor: calculați planul. Vedeți exact ce setări s-ar schimba pe stație, înainte de orice modificare.", "Calculează planul");
                if (planToChange == 0)
                    return new(PolicyStepKind.Assess, "Stația respectă deja politica: nu este nimic de schimbat. Puteți face raportul de conformitate.", "Verifică conformitatea");
                return canApply
                    ? new(PolicyStepKind.Apply, $"Pasul următor: aplicați planul ({planToChange} setări). Vi se va cere confirmarea; fiecare setare se recitește după scriere.", "Aplică planul…")
                    : new(PolicyStepKind.Assess, "Această ediție nu modifică stația. Puteți doar verifica conformitatea față de politică.", "Verifică conformitatea");
            case PolicyStatus.Deployed:
            case PolicyStatus.Verified:
                return new(PolicyStepKind.Assess, "Politica a fost aplicată. Verificați periodic conformitatea stației față de ea.", "Verifică conformitatea");
            default:
                return new(PolicyStepKind.Open, "Politica este retrasă; nu se mai folosește. Deschideți versiunea nouă.", "Deschide politica…");
        }
    }

    private static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
