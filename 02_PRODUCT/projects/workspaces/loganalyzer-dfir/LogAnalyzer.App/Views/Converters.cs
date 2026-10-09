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
            value is LogAnalyzer.Dfir.Model.StandardState s ? LogAnalyzer.Dfir.Analysis.StateLabels.Romanian(s) : "";

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
    }

    /// <summary>Spec §26 wording of a classification: OBSERVED, CORRELATED, INFERRED, UNPROVEN (the graph explorer uses the same words).</summary>
    public sealed class ClassificationWordingConverter : IValueConverter
    {
        public static readonly ClassificationWordingConverter Instance = new();

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
            value is LogAnalyzer.Dfir.Model.Classification c ? LogAnalyzer.Dfir.Graph.GraphExplorer.Wording(c) : "UNKNOWN";

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
    }
}
