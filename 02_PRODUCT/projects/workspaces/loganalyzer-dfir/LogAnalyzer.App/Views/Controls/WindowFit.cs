using System;
using System.Windows;

namespace LogAnalyzer.UI.Views.Controls
{
    /// <summary>
    /// Keeps a window inside the screen's work area (the screen minus the taskbar): on a 1366x768 or 1280x720 monitor a window declared 760 or 900 tall
    /// would otherwise open partly off screen with its buttons unreachable. Set <c>ctrl:WindowFit.ToWorkArea="True"</c> on the Window.
    /// </summary>
    public static class WindowFit
    {
        public static readonly DependencyProperty ToWorkAreaProperty =
            DependencyProperty.RegisterAttached("ToWorkArea", typeof(bool), typeof(WindowFit), new PropertyMetadata(false, OnChanged));

        public static bool GetToWorkArea(DependencyObject d) => (bool)d.GetValue(ToWorkAreaProperty);
        public static void SetToWorkArea(DependencyObject d, bool v) => d.SetValue(ToWorkAreaProperty, v);

        private static void OnChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not Window w || e.NewValue is not true) return;
            if (w.IsInitialized) Apply(w, SystemParameters.WorkArea.Size);
            else w.Initialized += (_, _) => Apply(w, SystemParameters.WorkArea.Size);   // after InitializeComponent: Height/Width/Min* are all set
        }

        /// <summary>Caps the window (and its minimum size) to <paramref name="area"/>; a window already smaller is left alone.</summary>
        public static void Apply(Window w, Size area)
        {
            if (area.Width <= 0 || area.Height <= 0) return;
            w.MaxWidth = Math.Min(w.MaxWidth, area.Width);
            w.MaxHeight = Math.Min(w.MaxHeight, area.Height);
            w.MinWidth = Math.Min(w.MinWidth, area.Width);
            w.MinHeight = Math.Min(w.MinHeight, area.Height);
            if (!double.IsNaN(w.Width)) w.Width = Math.Min(w.Width, area.Width);
            if (!double.IsNaN(w.Height)) w.Height = Math.Min(w.Height, area.Height);
        }
    }
}
