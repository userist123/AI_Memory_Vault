using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace LogAnalyzer.UI.Views.Controls
{
    public partial class CommandPalette : UserControl
    {
        public CommandPalette()
        {
            InitializeComponent();
            // the keyboard goes to the palette's own field when it opens (lab VM 2026-11-06: it stayed in the event search behind it)
            Backdrop.IsVisibleChanged += (_, e) =>
            {
                if (e.NewValue is true) Dispatcher.BeginInvoke(DispatcherPriority.Input, () => { PaletteSearchInput.Focus(); System.Windows.Input.Keyboard.Focus(PaletteSearchInput); });
            };
        }
    }
}
