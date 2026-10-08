#if CLASSIFIED_LOCAL_AI
using System.Windows;
using LogAnalyzer.UI.ViewModels;
using LogAnalyzer.UI.Views;

namespace LogAnalyzer.UI.Services
{
    /// <summary>Only with -p:ClassifiedIncludeLocalAi=true (owner approval required): the AI screen, nothing else.</summary>
    internal sealed class ClassifiedLocalAiViews : IFeatureViewFactory
    {
        public object? CreateViewModel(string featureKey, object? context = null) =>
            featureKey == FeatureKeys.AiAnalysis && context is InvestigationViewModel inv ? new AiAnalysisViewModel(inv) : null;

        public FrameworkElement? CreateView(string featureKey, object viewModel) =>
            featureKey == FeatureKeys.AiAnalysis ? new AiAnalysisView { DataContext = viewModel } : null;
    }
}
#endif
