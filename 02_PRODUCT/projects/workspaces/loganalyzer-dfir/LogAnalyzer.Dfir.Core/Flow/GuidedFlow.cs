namespace LogAnalyzer.Dfir.Flow;

/// <summary>One step of a guided flow: what the user is asked, and the check that must pass before "Înainte".</summary>
public sealed record FlowStep(string Key, string Title, string Question, Func<string?>? Validate = null);

/// <summary>
/// A guided flow of at most a few steps (WP18 S4): "Înapoi" never loses what was entered, "Oprește" keeps the flow where it was so it can be
/// resumed, and "Înainte" is refused with a sentence while the step's check fails. The flow owns no data of its own: the view model keeps the
/// answers and gives each step its validation.
/// </summary>
public sealed class GuidedFlow
{
    private readonly List<FlowStep> _steps;

    public GuidedFlow(string title, IEnumerable<FlowStep> steps)
    {
        Title = title;
        _steps = steps.ToList();
        if (_steps.Count == 0) throw new ArgumentException("A guided flow needs at least one step.", nameof(steps));
    }

    public string Title { get; }
    public IReadOnlyList<FlowStep> Steps => _steps;
    public int StepIndex { get; private set; }
    public FlowStep Current => _steps[StepIndex];
    public bool Stopped { get; private set; }
    public bool Completed { get; private set; }
    /// <summary>The reason the last "Înainte" was refused; empty when it was accepted.</summary>
    public string LastError { get; private set; } = "";

    public bool CanGoBack => !Completed && StepIndex > 0;
    public bool IsLastStep => StepIndex == _steps.Count - 1;
    public string ProgressText => Completed ? "Gata" : $"Pasul {StepIndex + 1} din {_steps.Count}";

    /// <summary>Goes to the next step, or completes the flow on the last step. Returns false (and sets <see cref="LastError"/>) when the step's check fails.</summary>
    public bool Next()
    {
        if (Completed) return false;
        Stopped = false;
        var error = Current.Validate?.Invoke();
        if (!string.IsNullOrWhiteSpace(error)) { LastError = error; return false; }
        LastError = "";
        if (IsLastStep) { Completed = true; return true; }
        StepIndex++;
        return true;
    }

    public bool Back()
    {
        if (!CanGoBack) return false;
        Stopped = false; LastError = "";
        StepIndex--;
        return true;
    }

    /// <summary>Stops without losing anything: the answers stay in the view model, the step stays where it was.</summary>
    public void Stop() { Stopped = true; LastError = ""; }
    public void Resume() { Stopped = false; }

    /// <summary>Back to the first step; the answers are the view model's to keep or clear.</summary>
    public void Restart() { StepIndex = 0; Stopped = false; Completed = false; LastError = ""; }
}
