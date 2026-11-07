using System;
using System.Collections;
using System.Globalization;
using System.Linq;
using System.Windows.Data;

namespace LogAnalyzer.UI.Views
{
    /// <summary>Visible when the bound value is false (the hint shown before a selection is made).</summary>
    public sealed class InverseBoolToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
            value is true ? System.Windows.Visibility.Collapsed : System.Windows.Visibility.Visible;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
    }

    /// <summary>Shows a list of strings in one grid cell.</summary>
    public sealed class JoinConverter : IValueConverter
    {
        public static readonly JoinConverter Instance = new();

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
            value is IEnumerable e and not string ? string.Join("; ", e.Cast<object>()) : value?.ToString() ?? "";

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
    }

    /// <summary>Romanian UI label of a standard state (UX contract §7): Observat, Corelat, Susținut de dovezi, ...</summary>
    public sealed class StandardStateLabelConverter : IValueConverter
    {
        public static readonly StandardStateLabelConverter Instance = new();

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
            value is LogAnalyzer.Dfir.Model.StandardState s ? LogAnalyzer.Dfir.Analysis.StateLabels.Romanian(s) : "";

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
    }

    /// <summary>Romanian state label of a classification (Observat, Corelat, Deducție, Nedemonstrat): the same mapping as the finding state and the graph view.</summary>
    public sealed class ClassificationWordingConverter : IValueConverter
    {
        public static readonly ClassificationWordingConverter Instance = new();

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
            value is LogAnalyzer.Dfir.Model.Classification c ? LogAnalyzer.Dfir.Analysis.StateLabels.ForClassification(c) : LogAnalyzer.Dfir.Analysis.StateLabels.Romanian(LogAnalyzer.Dfir.Model.StandardState.Unknown);

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
    }

    /// <summary>Severity as icon + Romanian word („▲ Ridicată”), so it is never only a colour or an English enum name.</summary>
    /// <summary>
    /// The plain sentence of a finding (owner 2026-11-06: who, what, with what, when; no codes). A case saved before the sentence existed gets it told now,
    /// by account (the users register is applied when the case is analysed).
    /// </summary>
    public sealed class PlainStoryConverter : IValueConverter
    {
        public static readonly PlainStoryConverter Instance = new();

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
            value is LogAnalyzer.Dfir.Model.Finding f ? LogAnalyzer.Dfir.Language.PlainStory.Current(f) : "";

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
    }

    /// <summary>Closure checklist result in words („îndeplinit”, „atenție”, „blochează”).</summary>
    public sealed class ChecklistStatusTextConverter : IValueConverter
    {
        public static readonly ChecklistStatusTextConverter Instance = new();

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value switch
        {
            LogAnalyzer.Dfir.Case.ChecklistStatus.Pass => "✓ îndeplinit",
            LogAnalyzer.Dfir.Case.ChecklistStatus.Warn => "⚠ atenție",
            LogAnalyzer.Dfir.Case.ChecklistStatus.Fail => "✗ blochează",
            _ => value?.ToString() ?? "",
        };

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
    }

    public sealed class TaskStatusTextConverter : IValueConverter
    {
        public static readonly TaskStatusTextConverter Instance = new();

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value switch
        {
            LogAnalyzer.Dfir.Case.CaseTaskStatus.Open => "deschisă",
            LogAnalyzer.Dfir.Case.CaseTaskStatus.Done => "făcută",
            LogAnalyzer.Dfir.Case.CaseTaskStatus.Cancelled => "anulată",
            _ => value?.ToString() ?? "",
        };

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
    }

    public sealed class SeverityTextConverter : IValueConverter    {
        public static readonly SeverityTextConverter Instance = new();

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
            value is LogAnalyzer.Dfir.Model.Severity s ? Text(s)
            // the older engines write the severity as English text ("High", "Critical"…): evaluation 2026-11-05 found it shown untranslated
            : value is string t && Enum.TryParse<LogAnalyzer.Dfir.Model.Severity>(t.Trim().StartsWith("Info", StringComparison.OrdinalIgnoreCase) ? "Info" : t.Trim(), true, out var parsed) ? Text(parsed)
            : value?.ToString() ?? "";

        private static string Text(LogAnalyzer.Dfir.Model.Severity s) => LogAnalyzer.Dfir.Presentation.SeverityLabels.Icon(s) + " " + LogAnalyzer.Dfir.Presentation.SeverityLabels.Romanian(s);

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
    }

    /// <summary>A category or tactic written in English by an older engine („Removable Media”, „Credential Access”), shown in Romanian (evaluation 2026-11-05).</summary>
    public sealed class CategoryTextConverter : IValueConverter
    {
        public static readonly CategoryTextConverter Instance = new();
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value is string s ? LogAnalyzer.Dfir.Language.RoLabels.Category(s.Trim()) : value?.ToString() ?? "";
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
    }

    /// <summary>The one-line „Ce face" of a policy setting, for the tooltip of a plan row.</summary>
    public sealed class PolicySettingOneLineConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
            value is LogAnalyzer.Dfir.Policy.SettingRef s ? LogAnalyzer.Dfir.Windows.Policy.PolicySettingCatalogue.OneLine(s) : "";

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
    }
}
