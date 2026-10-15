using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace LogAnalyzer.UI.Views.Controls
{
    /// <summary>
    /// Keeps the star-row part of a page (a TabControl or a DataGrid that fills the rest of the page) from collapsing when the parts above it are tall
    /// (owner screenshot 2026-10-15: after a station control the result section took the whole page and the checks list was one text line high).
    /// Set <c>ctrl:PageFill.MinHeight="360"</c> on the element in the star row. When the element gets less than that, the page view (the view hosted by
    /// <see cref="LazyPage"/>) gets a <see cref="FrameworkElement.MinHeight"/> large enough for the element to have it, and the page scrolls; the element
    /// keeps a finite height, so a DataGrid inside keeps its own scrolling and row virtualization. Outside a scroll host (a dialog window) nothing changes.
    /// The element itself must not carry the same MinHeight: the page must grow, not the element overflow its row.
    /// </summary>
    public static class PageFill
    {
        public static readonly DependencyProperty MinHeightProperty = DependencyProperty.RegisterAttached(
            "MinHeight", typeof(double), typeof(PageFill), new PropertyMetadata(0.0, OnMinHeightChanged));

        public static double GetMinHeight(DependencyObject d) => (double)d.GetValue(MinHeightProperty);
        public static void SetMinHeight(DependencyObject d, double value) => d.SetValue(MinHeightProperty, value);

        // the handler subscribed for an element, so it can be removed when the minimum is cleared
        private static readonly DependencyProperty HandlerProperty = DependencyProperty.RegisterAttached("Handler", typeof(EventHandler), typeof(PageFill));

        private static void OnMinHeightChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not FrameworkElement fill) return;
            if (fill.GetValue(HandlerProperty) is EventHandler old) { fill.LayoutUpdated -= old; fill.ClearValue(HandlerProperty); }
            if ((double)e.NewValue <= 0) return;
            // after every layout pass, not on SizeChanged: an element that is 0 px high from the start never raises SizeChanged
            EventHandler handler = (_, _) => Update(fill);
            fill.SetValue(HandlerProperty, handler);
            fill.LayoutUpdated += handler;
        }

        /// <summary>The page view that holds <paramref name="element"/>: the nearest ancestor that is the content of a ScrollViewer (null outside a scroll host).</summary>
        internal static FrameworkElement? PageViewOf(DependencyObject element)
        {
            for (DependencyObject? d = element; d is not null; d = VisualTreeHelper.GetParent(d))
                if (d is FrameworkElement fe && fe.Parent is ScrollViewer sv && ReferenceEquals(sv.Content, fe) && sv.TemplatedParent is null) return fe;
            return null;
        }

        /// <summary>The page height that gives the fill element its minimum: the page minus what the element has now plus its minimum.</summary>
        public static double RequiredPageHeight(double pageHeight, double fillHeight, double fillMinHeight) =>
            Math.Max(0, pageHeight - fillHeight + fillMinHeight);

        private static void Update(FrameworkElement fill)
        {
            var view = PageViewOf(fill);
            if (view is null || ReferenceEquals(view, fill)) return;
            if (!fill.IsVisible) { if (view.ReadLocalValue(FrameworkElement.MinHeightProperty) != DependencyProperty.UnsetValue) view.ClearValue(FrameworkElement.MinHeightProperty); return; }
            if (view.ActualHeight <= 0) return;
            double need = RequiredPageHeight(view.ActualHeight, fill.ActualHeight, GetMinHeight(fill));
            // the page grows when the fill is short and shrinks back (never below the size LazyPage gives it) when the parts above get smaller
            if (Math.Abs(need - view.MinHeight) > 0.5 && (need > view.ActualHeight + 0.5 || need < view.MinHeight - 0.5))
                view.MinHeight = need;
        }
    }
}
