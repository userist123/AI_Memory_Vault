using System;
using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using LogAnalyzer.Dfir.Windows.Audit;

namespace LogAnalyzer.UI.Views
{
    /// <summary>
    /// Usability 2026-10-16 (owner: „fiecare termen explicat acolo unde apare”): tooltips that look the term up in <see cref="TermCatalog"/>.
    /// <list type="bullet">
    /// <item><c>local:TermTip.Term="A11"</c> — a fixed term: the element gets the explanation as tooltip and as screen-reader help text.</item>
    /// <item><c>local:TermTip.Auto="True"</c> on a TextBlock (also in a DataGrid column's ElementStyle) — the tooltip follows the text shown: a check id, a result
    /// (CONFORM, DE VERIFICAT…), a severity word or a technical word. Text that is not in the catalogue gets no tooltip.</item>
    /// </list>
    /// </summary>
    public static class TermTip
    {
        public static readonly DependencyProperty TermProperty =
            DependencyProperty.RegisterAttached("Term", typeof(string), typeof(TermTip), new PropertyMetadata(null, OnTermChanged));
        public static string? GetTerm(DependencyObject d) => (string?)d.GetValue(TermProperty);
        public static void SetTerm(DependencyObject d, string? value) => d.SetValue(TermProperty, value);

        private static void OnTermChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not FrameworkElement fe) return;
            var tip = TermCatalog.Tooltip(e.NewValue as string);
            if (tip.Length == 0) return;
            fe.ToolTip = tip;
            ToolTipService.SetShowOnDisabled(fe, true);
            if (string.IsNullOrEmpty(AutomationProperties.GetHelpText(fe))) AutomationProperties.SetHelpText(fe, tip);
        }

        public static readonly DependencyProperty AutoProperty =
            DependencyProperty.RegisterAttached("Auto", typeof(bool), typeof(TermTip), new PropertyMetadata(false, OnAutoChanged));
        public static bool GetAuto(DependencyObject d) => (bool)d.GetValue(AutoProperty);
        public static void SetAuto(DependencyObject d, bool value) => d.SetValue(AutoProperty, value);

        private static void OnAutoChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not TextBlock tb || e.NewValue is not true) return;
            // a binding on the element's own text: no event handler to leak, recomputed whenever the cell is reused for another row
            BindingOperations.SetBinding(tb, FrameworkElement.ToolTipProperty,
                new Binding(nameof(TextBlock.Text)) { RelativeSource = RelativeSource.Self, Converter = TermTooltipConverter.Instance });
            ToolTipService.SetShowOnDisabled(tb, true);
        }
    }

    /// <summary>Text (or enum value) → its catalogue explanation; null (no tooltip) when the text is not an explained term.</summary>
    public sealed class TermTooltipConverter : IValueConverter
    {
        public static TermTooltipConverter Instance { get; } = new();
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            var tip = TermCatalog.Tooltip(value?.ToString());
            return tip.Length == 0 ? null : tip;
        }
        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
    }

    /// <summary>
    /// The small „?” next to a section heading: hover shows the explanation of <see cref="Term"/>, a click opens the glossary at that entry.
    /// </summary>
    public sealed class HelpButton : Button
    {
        public static readonly DependencyProperty TermProperty =
            DependencyProperty.Register(nameof(Term), typeof(string), typeof(HelpButton), new PropertyMetadata("", (d, e) => ((HelpButton)d).Apply()));
        public string Term { get => (string)GetValue(TermProperty); set => SetValue(TermProperty, value); }

        public HelpButton()
        {
            Content = "?";
            MinWidth = 22; Padding = new Thickness(6, 0, 6, 0); Margin = new Thickness(6, 0, 0, 0);
            VerticalAlignment = VerticalAlignment.Center;
            Focusable = true;
            Click += (_, _) => OpenGlossary(Term);
        }

        private void Apply()
        {
            var t = TermCatalog.Find(Term);
            ToolTip = t is null ? "Deschide glosarul „Ce înseamnă?”" : TermCatalog.Tooltip(Term) + " Clic: deschide glosarul.";
            AutomationProperties.SetName(this, t is null ? "Ce înseamnă?" : $"Ce înseamnă {t.Technical}?");
            AutomationProperties.SetHelpText(this, TermCatalog.Tooltip(Term));
        }

        /// <summary>Opens the glossary filtered on the term (owned by the main window).</summary>
        public static void OpenGlossary(string? term)
        {
            var w = new GlossaryWindow(TermCatalog.Find(term)?.Technical ?? term);
            if (Application.Current?.MainWindow is { IsVisible: true } owner && owner != w) w.Owner = owner;
            w.ShowDialog();
        }
    }
}
