using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;

namespace LogAnalyzer.UI.Services
{
    /// <summary>
    /// The accessible name of a grid row. WPF names a row after <c>item.ToString()</c>, so a row whose item does not override it was announced by a screen
    /// reader as its class name (lab 2026-11-10: "System.Data.DataRowView" in the users register, "LogAnalyzer.Dfir.Windows.Containment.ProcessIncident" in
    /// process containment). Such a row is named after its first columns ("Persoana: Administrator; Conturi: Administrator"). A row that already has a name,
    /// or whose item has a real text, is left alone.
    /// </summary>
    public static class GridRowNames
    {
        public const int MaxColumns = 4;
        public const int MaxValueChars = 80;

        private static readonly DependencyProperty HookedProperty =
            DependencyProperty.RegisterAttached("Hooked", typeof(bool), typeof(GridRowNames), new PropertyMetadata(false));

        private static readonly DependencyProperty WrittenProperty =
            DependencyProperty.RegisterAttached("Written", typeof(string), typeof(GridRowNames), new PropertyMetadata(""));

        private static bool _registered;

        /// <summary>
        /// Names every grid row of the application. The hook is SizeChanged, raised for each row at its first layout: a class handler for Loaded is never
        /// called, because WPF raises Loaded only where an instance handler exists (found by the test on 2026-11-10 after the first version did nothing on the VM).
        /// </summary>
        public static void Register()
        {
            if (_registered) return;
            _registered = true;
            EventManager.RegisterClassHandler(typeof(DataGridRow), FrameworkElement.SizeChangedEvent, new SizeChangedEventHandler((s, _) => Apply((DataGridRow)s)));
        }

        /// <summary>Names the row now and again whenever the (recycled) row gets another item.</summary>
        public static void Apply(DataGridRow row)
        {
            if ((bool)row.GetValue(HookedProperty)) return;   // SizeChanged comes again at every resize: the row is named once, then on every new item
            row.SetValue(HookedProperty, true);
            row.DataContextChanged += (s, _) => Name((DataGridRow)s);
            Name(row);
        }

        private static void Name(DataGridRow row)
        {
            if (row.Item is not { } item || item == CollectionView.NewItemPlaceholder) return;
            // a name set in XAML (a binding or a literal) wins; only a name this class wrote is replaced when the row is recycled
            var current = AutomationProperties.GetName(row);
            if (current.Length > 0 && current != (string)row.GetValue(WrittenProperty)) return;
            if (ItemsControl.ItemsControlFromItemContainer(row) is not DataGrid grid) return;
            var cols = grid.Columns.OrderBy(c => c.DisplayIndex)
                .Select(c => (Header: c.Header?.ToString() ?? "", Path: (c as DataGridBoundColumn)?.Binding is Binding b ? b.Path?.Path ?? "" : c.SortMemberPath ?? ""));
            if (NameFor(item, cols) is not { } name)
            {
                // the recycled row now holds an item with its own text: the name written for the previous item must not stay
                if (current.Length > 0) { row.ClearValue(AutomationProperties.NameProperty); row.SetValue(WrittenProperty, ""); }
                return;
            }
            row.SetCurrentValue(AutomationProperties.NameProperty, name);
            row.SetValue(WrittenProperty, name);
        }

        /// <summary>
        /// The name for a row, or null when the item's own text is meaningful (it is not just the class name). Built from the first <see cref="MaxColumns"/>
        /// columns that have a value: "Header: value; Header: value".
        /// </summary>
        public static string? NameFor(object item, IEnumerable<(string Header, string Path)> columns)
        {
            var text = item.ToString() ?? "";
            var type = item.GetType();
            // a record prints "TypeName { A = 1, B = System.Collections.Generic.List`1[...] }" (lab: the domain checks grid): that is not a text for a person either
            bool recordDump = text.StartsWith(type.Name + " {", System.StringComparison.Ordinal);
            if (text.Length > 0 && text != type.FullName && text != type.Name && !recordDump && item is not System.Data.DataRowView) return null;
            var parts = new List<string>();
            foreach (var (header, path) in columns)
            {
                if (parts.Count >= MaxColumns) break;
                var value = Value(item, path);
                if (value.Length == 0) continue;
                if (value.Length > MaxValueChars) value = value[..MaxValueChars] + "…";
                parts.Add(header.Length > 0 ? $"{header}: {value}" : value);
            }
            return parts.Count > 0 ? string.Join("; ", parts) : "rând de tabel";
        }

        /// <summary>One simple property (or DataRowView column) of the item as text; "" when the path is not a plain name or the value is missing.</summary>
        private static string Value(object item, string path)
        {
            if (path.Length == 0) return "";
            path = path.Trim('[', ']');
            object? v = null;
            try
            {
                if (item is System.Data.DataRowView row) v = row.Row.Table.Columns.Contains(path) ? row[path] : null;
                else if (path.All(ch => char.IsLetterOrDigit(ch) || ch == '_')) v = item.GetType().GetProperty(path)?.GetValue(item);
            }
            catch (System.Exception ex) when (ex is System.Reflection.TargetInvocationException or System.ArgumentException or System.Data.DataException) { return ""; }
            return v is null or System.DBNull ? "" : (System.Convert.ToString(v, CultureInfo.CurrentCulture) ?? "").Trim();
        }
    }
}
