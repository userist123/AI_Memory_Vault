using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using LogAnalyzer.UI.Services;

namespace LogAnalyzer.UI.Views
{
    /// <summary>
    /// Display-only helpers for the Simplu / Expert level (2026-10-23): <c>HideInSimple="True"</c> collapses an element (or a DataGrid column) in Simplu and
    /// keeps it in Expert; <c>OnlyInSimple="True"</c> is the opposite. Nothing here changes data, commands or checks.
    /// </summary>
    public static class SimpleLevel
    {
        public static readonly DependencyProperty HideInSimpleProperty = DependencyProperty.RegisterAttached("HideInSimple", typeof(bool), typeof(SimpleLevel),
            new PropertyMetadata(false, (d, e) => Bind(d, (bool)e.NewValue, hideInSimple: true)));
        public static bool GetHideInSimple(DependencyObject d) => (bool)d.GetValue(HideInSimpleProperty);
        public static void SetHideInSimple(DependencyObject d, bool value) => d.SetValue(HideInSimpleProperty, value);

        public static readonly DependencyProperty OnlyInSimpleProperty = DependencyProperty.RegisterAttached("OnlyInSimple", typeof(bool), typeof(SimpleLevel),
            new PropertyMetadata(false, (d, e) => Bind(d, (bool)e.NewValue, hideInSimple: false)));
        public static bool GetOnlyInSimple(DependencyObject d) => (bool)d.GetValue(OnlyInSimpleProperty);
        public static void SetOnlyInSimple(DependencyObject d, bool value) => d.SetValue(OnlyInSimpleProperty, value);

        /// <summary>The visibility a level gives an element: pure, so it can be tested without a window.</summary>
        public static Visibility VisibilityFor(bool isSimple, bool hideInSimple, Visibility baseVisibility = Visibility.Visible) =>
            baseVisibility != Visibility.Visible ? baseVisibility : isSimple == hideInSimple ? Visibility.Collapsed : Visibility.Visible;

        private static void Bind(DependencyObject d, bool on, bool hideInSimple)
        {
            var prop = d switch { UIElement => UIElement.VisibilityProperty, DataGridColumn => DataGridColumn.VisibilityProperty, _ => null };
            if (prop is null || !on) return;
            // an element that is already collapsed by its own rule (e.g. the media-register switch) stays collapsed in both levels
            var baseVisibility = (Visibility)d.GetValue(prop);
            BindingOperations.SetBinding(d, prop, new Binding(nameof(LanguageLevelState.IsSimple))
            {
                Source = LanguageLevelState.Instance,
                Mode = BindingMode.OneWay,
                Converter = new LevelConverter(hideInSimple, baseVisibility),
            });
        }

        private sealed class LevelConverter(bool hideInSimple, Visibility baseVisibility) : IValueConverter
        {
            public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => VisibilityFor(value is true, hideInSimple, baseVisibility);
            public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
        }

        /// <summary>Calls <paramref name="onChange"/> whenever the level moves while the element is loaded (unsubscribes on unload, so a closed page never leaks).</summary>
        public static void WhileLoaded(FrameworkElement owner, Action<bool> onChange)
        {
            System.ComponentModel.PropertyChangedEventHandler handler = (_, e) =>
            {
                if (e.PropertyName == nameof(LanguageLevelState.IsSimple)) owner.Dispatcher.BeginInvoke(() => onChange(LanguageLevelState.Instance.IsSimple));
            };
            owner.Loaded += (_, _) => { LanguageLevelState.Instance.PropertyChanged -= handler; LanguageLevelState.Instance.PropertyChanged += handler; };
            owner.Unloaded += (_, _) => LanguageLevelState.Instance.PropertyChanged -= handler;
        }

        /// <summary>Simplu hides some tabs; if the selected one is among them, the first tab takes over so the page is never blank.</summary>
        public static void KeepFirstTabWhenSimple(FrameworkElement owner, TabControl tabs) => WhileLoaded(owner, isSimple =>
        {
            if (isSimple && tabs.SelectedItem is TabItem { Visibility: not Visibility.Visible }) tabs.SelectedIndex = 0;
        });
    }
}
