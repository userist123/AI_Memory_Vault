using System;
using System.Collections;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using LogAnalyzer.UI.ViewModels;

namespace LogAnalyzer.UI.Views
{
    public partial class ProcessContainmentView : UserControl
    {
        private bool _loaded;

        public ProcessContainmentView()
        {
            InitializeComponent();
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            if (_loaded || DataContext is not ContainmentViewModel vm) return;
            _loaded = true;
            vm.LoadCommand.Execute(null);
        }
    }
}
