using System;
using System.Windows;
using LogAnalyzer.Dfir.Case;
using LogAnalyzer.Dfir.Model;

namespace LogAnalyzer.UI.Views
{
    /// <summary>
    /// Owner decision 28: the scope of the station's LIVE case, entered by the operator before the first LIVE use (or right after an emergency
    /// containment that started on the provisional scope). The dialog only returns a complete scope; the missing fields are listed and it stays open.
    /// </summary>
    public partial class ScopeDialog : Window
    {
        private readonly ScopeForm _form;

        /// <summary>The scope the operator confirmed; null if the dialog was closed without confirming.</summary>
        public CaseScope? Result { get; private set; }

        public ScopeDialog(CaseScope? current, bool afterEmergency = false)
        {
            InitializeComponent();
            _form = ScopeForm.FromScope(current);
            if (afterEmergency)
                Intro.Text = "Izolarea a pornit pe scopul provizoriu. Completați acum scopul cazului: de ce, pentru ce perioadă, pe ce sisteme, cine aprobă. Până la confirmare, rapoartele și exporturile poartă mențiunea „scop provizoriu, neconfirmat”.";
            LegalBox.ItemsSource = ScopeForm.LegalBases; NetworkBox.ItemsSource = ScopeForm.NetworkCategories; ClassBox.ItemsSource = ScopeForm.ClassificationLevels;
            PurposeBox.Text = _form.Purpose; ApproverBox.Text = _form.Approver; SystemsBox.Text = _form.Systems; NotesBox.Text = _form.Notes;
            FromPicker.SelectedDate = _form.PeriodFrom; ToPicker.SelectedDate = _form.PeriodTo;
            LegalBox.SelectedIndex = _form.LegalBasisIndex; NetworkBox.SelectedIndex = _form.NetworkIndex; ClassBox.SelectedIndex = _form.ClassificationIndex;
        }

        private void Confirm_Click(object sender, RoutedEventArgs e)
        {
            _form.Purpose = PurposeBox.Text; _form.Approver = ApproverBox.Text; _form.Systems = SystemsBox.Text; _form.Notes = NotesBox.Text;
            _form.PeriodFrom = FromPicker.SelectedDate; _form.PeriodTo = ToPicker.SelectedDate;
            _form.LegalBasisIndex = LegalBox.SelectedIndex; _form.NetworkIndex = NetworkBox.SelectedIndex; _form.ClassificationIndex = ClassBox.SelectedIndex;
            if (!_form.TryBuild(out var scope, out var missing))
            {
                ErrorText.Text = "Câmpuri lipsă sau inconsistente: " + string.Join(", ", missing);
                return;
            }
            Result = scope;
            DialogResult = true;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

        /// <summary>Shows the dialog on the UI thread and returns the confirmed scope, or null.</summary>
        public static CaseScope? Ask(CaseScope? current, bool afterEmergency)
        {
            CaseScope? Show() => new ScopeDialog(current, afterEmergency) { Owner = Application.Current?.MainWindow } is var d && d.ShowDialog() == true ? d.Result : null;
            var app = Application.Current;
            return app is null ? null : app.Dispatcher.CheckAccess() ? Show() : app.Dispatcher.Invoke(Show);
        }
    }
}
