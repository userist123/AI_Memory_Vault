using System.Collections.Generic;
using System.Linq;
using System.Windows;
using LogAnalyzer.Dfir.Devices;

namespace LogAnalyzer.UI.Views
{
    /// <summary>One line of the dialog: the local time (dd.MM.yyyy HH:mm:ss, „—” when unknown) and the event.</summary>
    public sealed record DeviceHistoryLine(string TimeText, string Kind, string User, string Details)
    {
        public static DeviceHistoryLine From(DeviceHistoryEvent e) =>
            new(e.TimeUtc is { } t ? DeviceText.LocalTime(t) : "—", e.Kind, e.User, e.Details);
    }

    /// <summary>
    /// „Istoric”: what the SEALED operation audit holds about one device, in time order (first seen, connections, disconnections, registration number changes,
    /// alerts, state changes); records still waiting to be sealed are marked „în așteptare (nesigilat)”.
    /// </summary>
    public partial class DeviceHistoryDialog : Window
    {
        public DeviceHistoryDialog(string device, string type, IReadOnlyList<DeviceHistoryEvent> events, string notice)
        {
            InitializeComponent();
            TitleText.Text = $"Istoric: {device} ({type})";
            NoticeText.Text = notice;
            HistoryGrid.ItemsSource = events.Select(DeviceHistoryLine.From).ToList();
        }

        private void Close_Click(object sender, RoutedEventArgs e) => Close();
    }
}
