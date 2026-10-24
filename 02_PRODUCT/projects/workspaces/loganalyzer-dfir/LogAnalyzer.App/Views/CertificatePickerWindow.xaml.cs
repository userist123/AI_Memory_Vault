using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Windows;
using LogAnalyzer.Dfir.Reporting;

namespace LogAnalyzer.UI.Views
{
    /// <summary>Lets the officer pick one of several qualified signing certificates on the card: name, issuer, validity and fingerprint are shown.</summary>
    public partial class CertificatePickerWindow : Window
    {
        private readonly IReadOnlyList<X509Certificate2> _certs;

        private CertificatePickerWindow(string heading, IReadOnlyList<X509Certificate2> certs)
        {
            InitializeComponent();
            Heading.Text = heading;
            _certs = certs;
            List.ItemsSource = certs.Select(c => $"{OfficerFingerprint.CommonName(c)} — emis de {c.GetNameInfo(X509NameType.SimpleName, true)}, valabil {c.NotBefore:yyyy-MM-dd} – {c.NotAfter:yyyy-MM-dd}, amprenta {OfficerFingerprint.Of(c)}").ToList();
            List.SelectedIndex = 0;
        }

        private void Ok_Click(object sender, RoutedEventArgs e) { if (List.SelectedIndex >= 0) DialogResult = true; }
        private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

        /// <summary>The single certificate, or the one picked when there are several; null when cancelled or none.</summary>
        public static X509Certificate2? Pick(string heading, IReadOnlyList<X509Certificate2> certs)
        {
            if (certs.Count <= 1) return certs.FirstOrDefault();
            var w = new CertificatePickerWindow(heading, certs) { Owner = Application.Current?.MainWindow };
            return w.ShowDialog() == true ? certs[w.List.SelectedIndex] : null;
        }
    }
}
