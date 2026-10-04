using System.Windows;
using LogAnalyzer.Core.Interfaces;
using LogAnalyzer.Core.Services;
using LogAnalyzer.UI.ViewModels;

namespace LogAnalyzer.UI
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();

            IEventParser eventParser = new EventParserService();
            IAnalysisEngine analysisEngine = new AnalysisEngineService();
            IRegistryParser registryParser = new RegistryParserService();
            IIocExtractionService iocExtractor = new IocExtractionService();
            ILogIntegrityService integrityService = new LogIntegrityService();
            IIncidentReportGenerator reportGenerator = new IncidentReportGenerator();

            DataContext = new MainViewModel(
                eventParser, analysisEngine, registryParser,
                iocExtractor, integrityService, reportGenerator);
        }
    }
}
