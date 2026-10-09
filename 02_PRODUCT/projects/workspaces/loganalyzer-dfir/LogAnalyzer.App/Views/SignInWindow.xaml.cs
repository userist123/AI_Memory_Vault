using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using LogAnalyzer.Dfir.Auth;
using LogAnalyzer.UI.Services;

namespace LogAnalyzer.UI.Views
{
    public enum SignInWindowMode { SignIn, FirstRun, Recovery, Unlock }

    /// <summary>
    /// Sign-in before the main window (decision 33): card + PIN by default; account + password only for the primary administrator. The same window
    /// creates the primary administrator on first run, sets a new password after the documented offline recovery, and unlocks a locked session.
    /// </summary>
    public partial class SignInWindow : Window
    {
        private readonly SignInWindowMode _mode;
        private readonly AuthSession? _locked;
        private List<CardCertificate> _choices = [];

        public AuthSession? Session { get; private set; }

        public SignInWindow(SignInWindowMode mode, AuthSession? lockedSession = null)
        {
            InitializeComponent();
            _mode = mode; _locked = lockedSession;
            Configure();
            Loaded += async (_, _) => await RefreshCardsAsync();
        }

        private void Configure()
        {
            bool setup = _mode is SignInWindowMode.FirstRun or SignInWindowMode.Recovery;
            CardPanel.Visibility = setup ? Visibility.Collapsed : Visibility.Visible;
            PersonLabel.Visibility = PersonBox.Visibility = _mode == SignInWindowMode.FirstRun ? Visibility.Visible : Visibility.Collapsed;
            ConfirmLabel.Visibility = ConfirmBox.Visibility = setup ? Visibility.Visible : Visibility.Collapsed;
            AccountLabel.Visibility = AccountBox.Visibility = _mode is SignInWindowMode.SignIn or SignInWindowMode.FirstRun ? Visibility.Visible : Visibility.Collapsed;
            switch (_mode)
            {
                case SignInWindowMode.FirstRun:
                    TitleText.Text = "Prima pornire: administratorul principal";
                    IntroText.Text = "Nu există niciun cont. Creați administratorul principal cu cont și parolă (cel puțin 14 caractere, diferită de nume, nu o parolă obișnuită). " +
                                     "Parola rămâne valabilă și după ce înrolați un card; ceilalți utilizatori se creează apoi de către administrator și folosesc numai card și PIN.";
                    PasswordTitle.Text = "Creare administrator principal"; PasswordHint.Text = ""; PasswordButton.Content = "Creează administratorul principal";
                    break;
                case SignInWindowMode.Recovery:
                    TitleText.Text = "Recuperare parolă (procedura offline)";
                    IntroText.Text = "Fișierul parolei administratorului principal a fost șters offline (procedura documentată în docs/dfir/AUTHENTICATION.md). Setați o parolă nouă; evenimentul este înregistrat în auditul autentificării.";
                    PasswordTitle.Text = "Parolă nouă pentru administratorul principal"; PasswordHint.Text = ""; PasswordButton.Content = "Setează parola";
                    break;
                case SignInWindowMode.Unlock:
                    TitleText.Text = "Sesiune blocată";
                    IntroText.Text = _locked is { LockReason: "card_removed" } ? "Cardul a fost scos din slotul tastaturii. Reintroduceți cardul și apăsați „Autentificare cu card”." : "Sesiunea s-a blocat după inactivitate.";
                    if (_locked?.Mode == SignInMode.Card) PasswordPanel.Visibility = Visibility.Collapsed;
                    else { CardPanel.Visibility = Visibility.Collapsed; AccountLabel.Visibility = AccountBox.Visibility = Visibility.Collapsed; PasswordTitle.Text = "Parola administratorului principal"; PasswordHint.Text = ""; PasswordButton.Content = "Deblochează"; }
                    ExitButton.Content = "Încheie și închide aplicația";
                    break;
                default:
                    TitleText.Text = "Autentificare";
                    IntroText.Text = "Aplicația cere autentificare înaintea oricărei lucrări. Utilizatorii folosesc cardul și PIN-ul; administratorul principal poate folosi și contul cu parola.";
                    break;
            }
        }

        private static string Describe(CardCertificate c) => $"{c.Certificate.GetNameInfo(System.Security.Cryptography.X509Certificates.X509NameType.SimpleName, false)}  |  {c.Reader}  |  seria {c.SerialHex}";

        private async Task RefreshCardsAsync()
        {
            if (CardPanel.Visibility != Visibility.Visible) return;
            try
            {
                _choices = (await Task.Run(() => AuthApp.Cards.Enumerate())).ToList();
                CardList.ItemsSource = _choices.Count == 0
                    ? new[] { "Niciun card cu certificat găsit. Introduceți cardul în slotul tastaturii (un badge RFID doar cu UID nu este acceptat)." }
                    : _choices.Select(Describe).ToArray();
            }
            catch (CardException ex) { StatusText.Text = ex.Message; }
        }

        private async void Refresh_Click(object sender, RoutedEventArgs e) => await RefreshCardsAsync();

        private async void Card_Click(object sender, RoutedEventArgs e)
        {
            StatusText.Text = "";
            CardButton.IsEnabled = false;
            try
            {
                string? thumb = CardList.SelectedIndex >= 0 && CardList.SelectedIndex < _choices.Count ? _choices[CardList.SelectedIndex].Sha256Thumbprint : null;
                if (_mode == SignInWindowMode.Unlock)
                {
                    var u = await Task.Run(() => AuthApp.Service.Unlock(_locked!, AuthApp.Cards));
                    if (u.Ok) { DialogResult = true; return; }
                    StatusText.Text = u.Message;
                    return;
                }
                var r = await Task.Run(() => AuthApp.Service.SignInWithCard(AuthApp.Cards, thumb));
                if (r.Reason == "choose_card" && r.Choices is { Count: > 0 } ch)
                {
                    _choices = ch.ToList();
                    CardList.ItemsSource = _choices.Select(Describe).ToArray();
                    StatusText.Text = "Sunt mai multe carduri înrolate: alegeți unul din listă și apăsați din nou.";
                    return;
                }
                Finish(r);
            }
            finally { CardButton.IsEnabled = true; }
        }

        private async void Password_Click(object sender, RoutedEventArgs e)
        {
            StatusText.Text = "";
            PasswordButton.IsEnabled = false;
            try
            {
                var pw = PasswordBox.Password;
                switch (_mode)
                {
                    case SignInWindowMode.FirstRun:
                    case SignInWindowMode.Recovery:
                    {
                        if (pw != ConfirmBox.Password) { StatusText.Text = "Parolele nu coincid."; return; }
                        var r = _mode == SignInWindowMode.FirstRun
                            ? await Task.Run(() => AuthApp.Service.CreatePrimaryAdmin(AccountBox.Text, PersonBox.Text, pw))
                            : await Task.Run(() => AuthApp.Service.RecoverPrimaryPassword(pw));
                        if (!r.Ok) { StatusText.Text = r.Message; return; }
                        var account = _mode == SignInWindowMode.FirstRun ? AccountBox.Text : AuthApp.Service.PrimaryAdmin!.Account;
                        Finish(await Task.Run(() => AuthApp.Service.SignInWithPassword(account, pw)));
                        break;
                    }
                    case SignInWindowMode.Unlock:
                    {
                        var u = await Task.Run(() => AuthApp.Service.Unlock(_locked!, null, pw));
                        if (u.Ok) DialogResult = true; else StatusText.Text = u.Message;
                        break;
                    }
                    default:
                        Finish(await Task.Run(() => AuthApp.Service.SignInWithPassword(AccountBox.Text, pw)));
                        break;
                }
            }
            finally { PasswordBox.Clear(); ConfirmBox.Clear(); PasswordButton.IsEnabled = true; }
        }

        private void Finish(SignInResult r)
        {
            if (!r.Ok) { StatusText.Text = r.Message; return; }
            if (r.AllWarnings.Count > 0) MessageBox.Show(string.Join("\n\n", r.AllWarnings), "Avertismente autentificare", MessageBoxButton.OK, MessageBoxImage.Warning);
            Session = r.Session;
            DialogResult = true;
        }

        private void Exit_Click(object sender, RoutedEventArgs e) => DialogResult = false;
    }
}
