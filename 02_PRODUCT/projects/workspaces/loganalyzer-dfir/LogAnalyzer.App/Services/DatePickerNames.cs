using System.Windows.Automation;
using System.Windows.Controls;

namespace LogAnalyzer.UI.Services
{
    /// <summary>
    /// Romanian names for the parts of a date field that WPF names in English (evaluation 2026-11-05: the calendar button was announced
    /// "Show Calendar" by the screen reader and by the UI Automation tree). The field's own name, when set, is reused so each button says
    /// which date it opens.
    /// </summary>
    public static class DatePickerNames
    {
        public const string CalendarButton = "Alege data din calendar";

        public static void Apply(DatePicker picker)
        {
            picker.ApplyTemplate();
            var field = AutomationProperties.GetName(picker);
            if (picker.Template?.FindName("PART_Button", picker) is Button button)
                AutomationProperties.SetName(button, string.IsNullOrWhiteSpace(field) ? CalendarButton : $"{CalendarButton}: {field}");
            if (picker.Template?.FindName("PART_TextBox", picker) is System.Windows.Controls.Primitives.DatePickerTextBox text && string.IsNullOrWhiteSpace(AutomationProperties.GetName(text)))
                AutomationProperties.SetName(text, string.IsNullOrWhiteSpace(field) ? "Data (zz.ll.aaaa)" : field);
        }
    }
}
