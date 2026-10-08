using System.Windows;
using System.Windows.Controls;
using LogAnalyzer.Core.Services.Edition;
using LogAnalyzer.UI.Services;
using Microsoft.Extensions.DependencyInjection;

namespace LogAnalyzer.UI.Services
{
    /// <summary>Keys of the screens that exist only in the unclassified edition.</summary>
    public static class FeatureKeys
    {
        public const string Containment = "Containment";
        public const string DomainInvestigation = "DomainInvestigation";
        public const string AiAnalysis = "AiAnalysis";
    }

    /// <summary>
    /// Creates the view models and views of the screens whose code lives only in the unclassified edition. The classified
    /// edition registers <see cref="NoFeatureViews"/>: those screens then show "nu este disponibil în ediția clasificată".
    /// </summary>
    public interface IFeatureViewFactory
    {
        /// <summary>Null when this edition does not contain the feature.</summary>
        object? CreateViewModel(string featureKey, object? context = null);
        FrameworkElement? CreateView(string featureKey, object viewModel);
    }

    public sealed class NoFeatureViews : IFeatureViewFactory
    {
        public object? CreateViewModel(string featureKey, object? context = null) => null;
        public FrameworkElement? CreateView(string featureKey, object viewModel) => null;
    }
}

namespace LogAnalyzer.UI.Views.Controls
{
    /// <summary>
    /// Hosts a screen of the unclassified edition. Without a view model (classified edition) it shows the notice instead:
    /// the screen's code is not in this build.
    /// </summary>
    public sealed class FeaturePlaceholder : ContentControl
    {
        public static readonly DependencyProperty FeatureKeyProperty =
            DependencyProperty.Register(nameof(FeatureKey), typeof(string), typeof(FeaturePlaceholder), new PropertyMetadata("", (d, _) => ((FeaturePlaceholder)d).Rebuild()));

        public static readonly DependencyProperty ViewModelProperty =
            DependencyProperty.Register(nameof(ViewModel), typeof(object), typeof(FeaturePlaceholder), new PropertyMetadata(null, (d, _) => ((FeaturePlaceholder)d).Rebuild()));

        public static readonly DependencyProperty TitleProperty =
            DependencyProperty.Register(nameof(Title), typeof(string), typeof(FeaturePlaceholder), new PropertyMetadata("Funcție", (d, _) => ((FeaturePlaceholder)d).Rebuild()));

        public string FeatureKey { get => (string)GetValue(FeatureKeyProperty); set => SetValue(FeatureKeyProperty, value); }
        public object? ViewModel { get => GetValue(ViewModelProperty); set => SetValue(ViewModelProperty, value); }
        public string Title { get => (string)GetValue(TitleProperty); set => SetValue(TitleProperty, value); }

        public FeaturePlaceholder()
        {
            HorizontalContentAlignment = HorizontalAlignment.Stretch;
            VerticalContentAlignment = VerticalAlignment.Stretch;
            Rebuild();
        }

        private void Rebuild()
        {
            FrameworkElement? view = null;
            if (ViewModel is { } vm && FeatureKey.Length > 0)
                view = global::LogAnalyzer.UI.App.ServiceProvider?.GetService<IFeatureViewFactory>()?.CreateView(FeatureKey, vm);
            Content = view ?? Notice(Title);
        }

        public static FrameworkElement Notice(string title) => new StackPanel
        {
            Margin = new Thickness(24),
            Children =
            {
                new TextBlock { Text = title, FontSize = 18, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 6) },
                new TextBlock { Text = EditionText.Unavailable(title), TextWrapping = TextWrapping.Wrap, Opacity = 0.8 },
            },
        };
    }
}
