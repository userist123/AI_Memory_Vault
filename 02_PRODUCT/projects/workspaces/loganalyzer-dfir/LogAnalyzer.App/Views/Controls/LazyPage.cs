using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;

namespace LogAnalyzer.UI.Views.Controls
{
    /// <summary>
    /// A page of the main window that is built the first time it is shown, then kept (owner 2026-10-11: startup built all 23 pages even when only one
    /// is opened). The page's view comes from <see cref="Page"/>; it inherits the main window's DataContext like an inline page would.
    /// </summary>
    [ContentProperty(nameof(Page))]
    public sealed class LazyPage : ContentControl
    {
        public DataTemplate? Page { get; set; }

        public LazyPage()
        {
            Focusable = false;
            IsVisibleChanged += (_, e) => { if (e.NewValue is true) Realize(); };
            Loaded += (_, _) => { if (IsVisible) Realize(); };
        }

        /// <summary>True once the page's view exists (tests and the startup log use it).</summary>
        public bool IsRealized => Content is not null;

        public void Realize()
        {
            if (Content is not null || Page is null) return;
            if (!Page.IsSealed) Page.Seal();      // XAML seals templates itself; one built in code must be sealed before use
            Content = Page.LoadContent();
        }
    }
}
