using System.Windows;
using XAU_Kinetic.Desktop.ViewModels;

namespace XAU_Kinetic.Desktop
{
    public partial class MainWindow : Window
    {
        public MainWindow(MainViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;
        }
    }
}
