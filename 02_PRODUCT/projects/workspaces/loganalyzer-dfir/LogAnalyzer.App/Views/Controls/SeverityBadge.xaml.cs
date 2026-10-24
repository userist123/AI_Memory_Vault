using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace LogAnalyzer.UI.Views.Controls
{
    public partial class SeverityBadge : UserControl
    {
        public static readonly DependencyProperty SeverityProperty =
            DependencyProperty.Register(nameof(Severity), typeof(string), typeof(SeverityBadge),
                new PropertyMetadata("INFO", OnSeverityChanged));

        public string Severity
        {
            get => (string)GetValue(SeverityProperty);
            set => SetValue(SeverityProperty, value);
        }

        public SeverityBadge()
        {
            InitializeComponent();
            UpdateBadge(Severity);
        }

        private static void OnSeverityChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is SeverityBadge badge)
            {
                badge.UpdateBadge(e.NewValue as string);
            }
        }

        private void UpdateBadge(string? severity)
        {
            var s = (severity ?? "INFO").ToUpperInvariant();
            // The badge always says the severity in words and with an icon shape, so it is readable without colour (U18).
            if (System.Enum.TryParse<LogAnalyzer.Dfir.Model.Severity>(s, true, out var level))
                BadgeText.Text = LogAnalyzer.Dfir.Presentation.SeverityLabels.Icon(level) + " " + LogAnalyzer.Dfir.Presentation.SeverityLabels.Romanian(level);
            else
                BadgeText.Text = s;
            System.Windows.Automation.AutomationProperties.SetName(this, "Severitate: " + BadgeText.Text);
            if (s.Contains("CRIT"))
            {
                BadgeBorder.Background = (Brush)FindResource("BadgeCriticalBgBrush");
                BadgeBorder.BorderBrush = Application.Current.TryFindResource("CriticalBrush") as Brush;
                BadgeText.Foreground = Application.Current.TryFindResource("CriticalBrush") as Brush;
            }
            else if (s.Contains("HIGH") || s.Contains("AVERT"))
            {
                BadgeBorder.Background = (Brush)FindResource("BadgeHighBgBrush");
                BadgeBorder.BorderBrush = Application.Current.TryFindResource("HighBrush") as Brush;
                BadgeText.Foreground = Application.Current.TryFindResource("HighBrush") as Brush;
            }
            else if (s.Contains("MED"))
            {
                BadgeBorder.Background = (Brush)FindResource("BadgeMediumBgBrush");
                BadgeBorder.BorderBrush = Application.Current.TryFindResource("MediumBrush") as Brush;
                BadgeText.Foreground = Application.Current.TryFindResource("MediumBrush") as Brush;
            }
            else
            {
                BadgeBorder.Background = (Brush)FindResource("BadgeLowBgBrush");
                BadgeBorder.BorderBrush = Application.Current.TryFindResource("PrimaryAccentBrush") as Brush;
                BadgeText.Foreground = Application.Current.TryFindResource("PrimaryAccentBrush") as Brush;
            }
        }
    }
}