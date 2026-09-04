namespace XAU_Kinetic.Desktop.Services
{
    public interface IDialogService
    {
        void ShowInformation(string message, string caption);
        void ShowError(string message, string caption);
        bool ConfirmWarning(string message, string caption);
    }
}
