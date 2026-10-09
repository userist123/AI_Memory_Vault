using System;
using System.Collections;
using System.Globalization;
using System.Linq;
using System.Windows.Data;

namespace LogAnalyzer.UI.Views
{
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
            value is LogAnalyzer.Dfir.Model.StandardState s ? LogAnalyzer.Dfir.Analysis.StateLabels.Label(s) : "";

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
    }

    /// <summary>Romanian state label of a classification (Observat, Corelat, Deducție, Nedemonstrat): the same mapping as the finding state and the graph view.</summary>
    public sealed class ClassificationWordingConverter : IValueConverter
    {
        public static readonly ClassificationWordingConverter Instance = new();

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
            value is LogAnalyzer.Dfir.Model.Classification c ? LogAnalyzer.Dfir.Analysis.StateLabels.ForClassification(c) : LogAnalyzer.Dfir.Analysis.StateLabels.Label(LogAnalyzer.Dfir.Model.StandardState.Unknown);

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
    }

    /// <summary>Severity as icon + Romanian word („▲ Ridicată”), so it is never only a colour or an English enum name.</summary>
    public sealed class SeverityTextConverter : IValueConverter
    {
        public static readonly SeverityTextConverter Instance = new();

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
            value is LogAnalyzer.Dfir.Model.Severity s ? LogAnalyzer.Dfir.Presentation.SeverityLabels.Icon(s) + " " + LogAnalyzer.Dfir.Presentation.SeverityLabels.Text(s) : "";

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
    }
}
