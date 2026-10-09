using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LogAnalyzer.Dfir.Auth;
using LogAnalyzer.UI.Services;
using Microsoft.Win32;

namespace LogAnalyzer.UI.ViewModels
{
    public sealed record AccountRow(string Account, string Person, string Role, string State, int Cards, string Mode);
    public sealed record CardRow(string Thumbprint, string ShortThumbprint, string Subject, string Serial, string Issuer, string State, string EnrolledBy);
    public sealed record DetectedCardRow(CardCertificate Card, string Subject, string Reader, string Serial);

    /// <summary>
    /// "Autentificare și conturi" (decision 33): the signed-in user, own card enrolment, and - for the global administrator - users and their cards,
    /// the trust store (CA certificates), CRLs and the authentication parameters. Every action goes through <see cref="AuthService"/>, which checks the
    /// role itself and writes the hash-chained auth audit; hiding a button here is convenience, not the gate.
    /// </summary>
    public sealed partial class AuthViewModel : ObservableObject
    {
        public AuthViewModel()
        {
            AuthApp.SessionChanged += () => System.Windows.Application.Current?.Dispatcher.Invoke(Refresh);
            Refresh();
        }

        public ObservableCollection<AccountRow> Accounts { get; } = new();
        public ObservableCollection<CardRow> Cards { get; } = new();
        public ObservableCollection<DetectedCardRow> Detected { get; } = new();
        public ObservableCollection<string> TrustAnchors { get; } = new();
        public ObservableCollection<string> Crls { get; } = new();

        [ObservableProperty] private string _currentUser = "";
        [ObservableProperty] private bool _isAdministrator;
        [ObservableProperty] private bool _isPrimaryAdmin;
        [ObservableProperty] private string _status = "";
        [ObservableProperty] private AccountRow? _selectedAccount;
        [ObservableProperty] private CardRow? _selectedCard;
        [ObservableProperty] private DetectedCardRow? _selectedDetected;
        [ObservableProperty] private string _newAccount = "";
        [ObservableProperty] private string _newPerson = "";
        [ObservableProperty] private bool _newIsAdministrator;
        [ObservableProperty] private bool _missingCrlRefuse;
        [ObservableProperty] private int _idleLockMinutes = 10;
        [ObservableProperty] private int _lockoutThreshold = 5;
        [ObservableProperty] private string _auditSummary = "";
        [ObservableProperty] private string _integrity = "";
        [ObservableProperty] private bool _integrityBroken;
        [ObservableProperty] private string _registerGap = "";

        public string AuthDirectory => AuthApp.Service.Dir;

        partial void OnSelectedAccountChanged(AccountRow? value) => LoadCards();

        private AuthSession? Session => AuthApp.Session;

        private void Refresh()
        {
            var s = Session;
            CurrentUser = s is null ? "(nicio sesiune)" : $"{s.Account} - {(s.Role == AuthRole.Administrator ? "administrator global" : "operator")}{(s.IsPrimaryAdmin ? " (principal)" : "")} - " +
                          (s.Mode == SignInMode.Card ? $"card {s.CardThumbprint?[..Math.Min(12, s.CardThumbprint.Length)]} în {s.CardReader}" : "cont + parolă") + $" - din {s.SignedInUtc.ToLocalTime():HH:mm:ss}";
            IsAdministrator = s?.IsAdministrator == true;
            IsPrimaryAdmin = s?.IsPrimaryAdmin == true;
            var svc = AuthApp.Service;
            Accounts.Clear();
            foreach (var a in svc.Accounts())
                Accounts.Add(new AccountRow(a.Account, a.Person, a.Role == AuthRole.Administrator ? "administrator" : "operator", a.Disabled ? "dezactivat" : "activ", a.Cards.Count,
                    a.IsPrimaryAdmin ? "card sau cont + parolă (principal)" : "numai card + PIN"));
            LoadCards();
            TrustAnchors.Clear();
            foreach (var c in svc.Trust.LoadAnchors()) TrustAnchors.Add($"{c.Subject}  (valabil până la {c.NotAfter:yyyy-MM-dd}, SHA-256 {Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(c.RawData))[..16]})");
            Crls.Clear();
            foreach (var c in svc.Trust.LoadCrls()) Crls.Add($"thisUpdate {c.ThisUpdate:yyyy-MM-dd HH:mm}Z, nextUpdate {(c.NextUpdate is { } n ? n.ToString("yyyy-MM-dd HH:mm") + "Z" : "-")}, {c.RevokedSerials.Count} certificate revocate");
            var p = svc.Policy;
            MissingCrlRefuse = p.MissingCrl == MissingCrlPolicy.Refuse; IdleLockMinutes = p.IdleLockMinutes; LockoutThreshold = p.LockoutThreshold;
            var v = svc.VerifyAudit();
            AuditSummary = $"Audit autentificare: {v.Message}";
            IntegrityBroken = !svc.AccountsIntegrityOk;
            Integrity = IntegrityBroken ? "accounts.json a fost modificat în afara aplicației: autentificarea cu card este refuzată până când administratorul principal acceptă starea curentă." : "accounts.json corespunde ultimei modificări auditate.";
            var users = Dfir.Registers.RegisterStore.Load<Dfir.Registers.UsersRegister>().Register;
            var missing = users is null ? svc.Accounts().Select(a => a.Account).ToList() : svc.AccountsMissingFromRegister(users).ToList();
            RegisterGap = missing.Count == 0 ? "" : "Conturi fără rând în Registrul utilizatori (abilitările se introduc acolo): " + string.Join(", ", missing);
        }

        private void LoadCards()
        {
            Cards.Clear();
            if (SelectedAccount is null) return;
            var acc = AuthApp.Service.Find(SelectedAccount.Account);
            if (acc is null) return;
            foreach (var c in acc.Cards)
                Cards.Add(new CardRow(c.Sha256Thumbprint, c.Sha256Thumbprint[..Math.Min(16, c.Sha256Thumbprint.Length)], c.Subject, c.Serial, c.Issuer, c.Disabled ? "dezactivat" : "activ", c.EnrolledBy));
        }

        private bool Report(AuthResult r, string ok)
        {
            Status = r.Ok ? ok + (string.IsNullOrEmpty(r.Message) ? "" : " " + r.Message) : "Refuzat: " + r.Message;
            Refresh();
            return r.Ok;
        }

        [RelayCommand]
        private async Task DetectCardsAsync()
        {
            try
            {
                var found = await Task.Run(() => AuthApp.Cards.Enumerate());
                Detected.Clear();
                foreach (var c in found) Detected.Add(new DetectedCardRow(c, c.Certificate.Subject, c.Reader, c.SerialHex));
                Status = found.Count == 0 ? "Niciun card cu certificat și cheie privată în cititor (un badge RFID doar cu UID nu este acceptat)." : $"{found.Count} certificate pe card.";
            }
            catch (CardException ex) { Status = ex.Message; }
        }

        /// <summary>Enrols the selected detected card for the signed-in user (a replacement or an additional card). The card signs a challenge (PIN prompt).</summary>
        [RelayCommand]
        private async Task EnrollForMeAsync()
        {
            if (Session is not { } s || SelectedDetected is not { } d) { Status = "Alegeți un card detectat."; return; }
            Status = "Introduceți PIN-ul în fereastra Windows / SafeNet…";
            Report(await Task.Run(() => AuthApp.Service.EnrollCard(s, s.Account, d.Card, AuthApp.Cards)), "Card înrolat pentru contul dumneavoastră.");
        }

        [RelayCommand]
        private async Task EnrollForSelectedAsync()
        {
            if (Session is not { } s || SelectedAccount is not { } a || SelectedDetected is not { } d) { Status = "Alegeți un cont și un card detectat."; return; }
            Status = "Utilizatorul introduce PIN-ul în fereastra Windows / SafeNet…";
            Report(await Task.Run(() => AuthApp.Service.EnrollCard(s, a.Account, d.Card, AuthApp.Cards)), $"Card înrolat pentru {a.Account}.");
        }

        [RelayCommand]
        private void CreateAccount()
        {
            if (Session is not { } s) return;
            if (Report(AuthApp.Service.CreateAccount(s, NewAccount, NewPerson, NewIsAdministrator ? AuthRole.Administrator : AuthRole.Operator), $"Cont creat: {NewAccount} (numai card + PIN)."))
            { NewAccount = ""; NewPerson = ""; NewIsAdministrator = false; }
        }

        [RelayCommand]
        private void ToggleRole()
        {
            if (Session is not { } s || SelectedAccount is not { } a) return;
            Report(AuthApp.Service.SetRole(s, a.Account, a.Role == "administrator" ? AuthRole.Operator : AuthRole.Administrator), "Rol schimbat.");
        }

        [RelayCommand]
        private void ToggleAccountDisabled()
        {
            if (Session is not { } s || SelectedAccount is not { } a) return;
            Report(AuthApp.Service.SetAccountDisabled(s, a.Account, a.State == "activ"), a.State == "activ" ? "Cont dezactivat (imediat)." : "Cont reactivat.");
        }

        [RelayCommand]
        private void ToggleCardDisabled()
        {
            if (Session is not { } s || SelectedAccount is not { } a || SelectedCard is not { } c) return;
            Report(AuthApp.Service.SetCardDisabled(s, a.Account, c.Thumbprint, c.State == "activ"), c.State == "activ" ? "Card dezactivat (imediat)." : "Card reactivat.");
        }

        [RelayCommand]
        private void ImportCa()
        {
            if (Session is not { } s) return;
            var dlg = new OpenFileDialog { Title = "Certificat CA al organizației (DER sau PEM)", Filter = "Certificate (*.cer;*.crt;*.pem;*.der)|*.cer;*.crt;*.pem;*.der|Toate fișierele|*.*" };
            if (dlg.ShowDialog() == true) Report(AuthApp.Service.ImportTrustCertificate(s, File.ReadAllBytes(dlg.FileName), dlg.FileName), "Certificat CA importat.");
        }

        [RelayCommand]
        private void ImportCrl()
        {
            if (Session is not { } s) return;
            var dlg = new OpenFileDialog { Title = "Listă de revocare (CRL, DER sau PEM) obținută offline", Filter = "CRL (*.crl;*.pem)|*.crl;*.pem|Toate fișierele|*.*" };
            if (dlg.ShowDialog() == true) Report(AuthApp.Service.ImportCrl(s, File.ReadAllBytes(dlg.FileName), dlg.FileName), "CRL importat.");
        }

        [RelayCommand]
        private void SavePolicy()
        {
            if (Session is not { } s) return;
            Report(AuthApp.Service.SetPolicy(s, new AuthPolicy
            {
                MissingCrl = MissingCrlRefuse ? MissingCrlPolicy.Refuse : MissingCrlPolicy.Warn, IdleLockMinutes = IdleLockMinutes, LockoutThreshold = LockoutThreshold,
            }), "Parametri salvați.");
        }

        [RelayCommand]
        private void AcceptIntegrity()
        {
            if (Session is not { } s) return;
            Report(AuthApp.Service.AcceptCurrentAccountsState(s), "Starea curentă a conturilor a fost acceptată și auditată.");
        }

        /// <summary>Called by the view with the three password boxes (primary administrator only).</summary>
        public void ChangePassword(string current, string next, string confirm)
        {
            if (Session is not { } s) return;
            if (next != confirm) { Status = "Parolele noi nu coincid."; return; }
            Report(AuthApp.Service.ChangePassword(s, current, next), "Parola a fost schimbată.");
        }

        [RelayCommand]
        private void SignOut()
        {
            if (Session is { } s) AuthApp.Service.SignOut(s);
            AuthApp.SetSession(null);
            System.Windows.Application.Current.Shutdown();
        }
    }
}
