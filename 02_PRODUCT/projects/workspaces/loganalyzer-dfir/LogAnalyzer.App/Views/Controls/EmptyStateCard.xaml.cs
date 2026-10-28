using System.Windows;
using System.Windows.Controls;

namespace LogAnalyzer.UI.Views.Controls
{
    /// <summary>The "Ce este / de ce contează pentru control / primul pas" panel of a page with nothing in it yet.</summary>
    public partial class EmptyStateCard : UserControl
    {
        public static readonly DependencyProperty WhatProperty = DependencyProperty.Register(nameof(What), typeof(string), typeof(EmptyStateCard), new PropertyMetadata(""));
        public static readonly DependencyProperty WhyProperty = DependencyProperty.Register(nameof(Why), typeof(string), typeof(EmptyStateCard), new PropertyMetadata(""));
        public static readonly DependencyProperty StepProperty = DependencyProperty.Register(nameof(Step), typeof(string), typeof(EmptyStateCard), new PropertyMetadata(""));
        public static readonly DependencyProperty ActionProperty = DependencyProperty.Register(nameof(Action), typeof(object), typeof(EmptyStateCard), new PropertyMetadata(null));

        public string What { get => (string)GetValue(WhatProperty); set => SetValue(WhatProperty, value); }
        public string Why { get => (string)GetValue(WhyProperty); set => SetValue(WhyProperty, value); }
        public string Step { get => (string)GetValue(StepProperty); set => SetValue(StepProperty, value); }
        /// <summary>The button of the first step.</summary>
        public object? Action { get => GetValue(ActionProperty); set => SetValue(ActionProperty, value); }

        public EmptyStateCard() { InitializeComponent(); }
    }
}
