using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;

namespace LogAnalyzer.UI.Views.Controls
{
    /// <summary>
    /// A page of the main window that is built the first time it is shown, then kept (owner 2026-10-11: startup built all 23 pages even when only one
    /// is opened). The page's view comes from <see cref="Page"/>; it inherits the main window's DataContext like an inline page would.
    /// The view sits in a <see cref="ScrollViewer"/> (<see cref="Host"/>) so that on a small monitor (1366x768, 1280x720) every part of the page is
    /// reachable: the view gets the size of the viewport when that is at least <see cref="MinPageWidth"/> x <see cref="MinPageHeight"/> (so a DataGrid on a
    /// star row keeps its own scrolling and virtualization), and scrolls the whole page below that.
    /// </summary>
    [ContentProperty(nameof(Page))]
    public sealed class LazyPage : ContentControl
    {
        /// <summary>The smallest page size the views are laid out for; a smaller viewport scrolls instead of squeezing the page.</summary>
        public const double MinPageWidth = 880, MinPageHeight = 560;

        public DataTemplate? Page { get; set; }

        /// <summary>The scroll host that holds the page's view (null until the page is realized).</summary>
        public ScrollViewer? Host => Content as ScrollViewer;

        /// <summary>The page's view itself (null until the page is realized).</summary>
        public FrameworkElement? View => Host?.Content as FrameworkElement;

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
            var built = Page.LoadContent();
            if (built is not FrameworkElement view) { Content = built; return; }
            var host = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                Focusable = false,
                Content = view,
            };
            host.SizeChanged += (_, _) => Fit(host, view);
            // a page that grows past the viewport (PageFill raises its MinHeight) gets the vertical scroll bar: its width comes off the page width
            System.ComponentModel.DependencyPropertyDescriptor.FromProperty(MinHeightProperty, typeof(FrameworkElement))
                .AddValueChanged(view, (_, _) => Fit(host, view));
            Content = host;
        }

        /// <summary>The size the view is given for a viewport: the viewport itself, but never below the minimum page size (the scroll bars then appear).</summary>
        public static Size PageSize(double viewportWidth, double viewportHeight) => PageSize(viewportWidth, viewportHeight, 0);

        /// <summary>As <see cref="PageSize(double, double)"/> for a view that needs at least <paramref name="viewMinHeight"/> (it then scrolls vertically).</summary>
        public static Size PageSize(double viewportWidth, double viewportHeight, double viewMinHeight)
        {
            bool vbar = viewportHeight < Math.Max(MinPageHeight, viewMinHeight);
            double w = Math.Max(viewportWidth - (vbar ? SystemParameters.VerticalScrollBarWidth : 0), MinPageWidth);
            double h = Math.Max(viewportHeight - (w > viewportWidth ? SystemParameters.HorizontalScrollBarHeight : 0), MinPageHeight);
            return new Size(w, h);
        }

        private static void Fit(ScrollViewer host, FrameworkElement view)
        {
            if (host.ActualWidth <= 0 || host.ActualHeight <= 0) return;
            var s = PageSize(host.ActualWidth, host.ActualHeight, view.MinHeight);
            view.Width = s.Width; view.Height = s.Height;
        }
    }
}
