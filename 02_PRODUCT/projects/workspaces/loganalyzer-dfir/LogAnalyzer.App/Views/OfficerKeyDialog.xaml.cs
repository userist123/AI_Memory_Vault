using System.Windows;

namespace LogAnalyzer.UI.Views
{
    /// <summary>
    /// Asks for the passphrase of a security officer key (and, when registering, the officer's name). The passphrase is read once by the caller and the
    /// box is cleared when the dialog closes; nothing is stored.
    /// </summary>
    public partial class OfficerKeyDialog : Window
    {
        private readonly bool _askName;

        public string OfficerName { get; private set; } = "";

        private OfficerKeyDialog(string heading, string explanation, bool askName)
        {
            InitializeComponent();
            Heading.Text = heading;
            Explanation.Text = explanation;
            _askName = askName;
            NamePanel.Visibility = askName ? Visibility.Visible : Visibility.Collapsed;
            Loaded += (_, _) => { if (askName) NameBox.Focus(); else Pass.Focus(); };
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            if (_askName && string.IsNullOrWhiteSpace(NameBox.Text)) { ErrorText.Text = "Scrieți numele ofițerului de securitate."; return; }
            if (Pass.SecurePassword.Length == 0) { ErrorText.Text = "Scrieți parola cheii."; return; }
            OfficerName = NameBox.Text.Trim();
            DialogResult = true;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e) { Pass.Clear(); DialogResult = false; }

        /// <summary>Shows the dialog; returns the passphrase (null when cancelled) and, with <paramref name="askName"/>, the name typed.</summary>
        public static string? Ask(string heading, string explanation, bool askName, out string name)
        {
            var d = new OfficerKeyDialog(heading, explanation, askName) { Owner = Application.Current?.MainWindow };
            name = "";
            try
            {
                if (d.ShowDialog() != true) return null;
                name = d.OfficerName;
                return d.Pass.Password;
            }
            finally { d.Pass.Clear(); }
        }
    }
}
