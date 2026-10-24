using System.Windows;
using System.Windows.Controls;
using LogAnalyzer.UI.ViewModels;

namespace LogAnalyzer.UI.Views
{
    public partial class AuthView : UserControl
    {
        public AuthView() => InitializeComponent();

        private void ChangePassword_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is not AuthViewModel vm) return;
            vm.ChangePassword(CurrentPw.Password, NewPw.Password, ConfirmPw.Password);
            CurrentPw.Clear(); NewPw.Clear(); ConfirmPw.Clear();
        }
    }
}
