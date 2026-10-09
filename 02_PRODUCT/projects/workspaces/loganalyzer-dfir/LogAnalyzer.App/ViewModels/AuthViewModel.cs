using System;
using LogAnalyzer.Dfir.Language;
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
    public sealed record AccountRow(string Account, string Person, string Role, string State, int Cards, string Mode, bool IsAdministrator = false, bool IsDisabled = false);
    public sealed record CardRow(string Thumbprint, string ShortThumbprint, string Subject, string Serial, string Issuer, string State, string EnrolledBy, bool IsDisabled = false);
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
            Loc.LanguageChanged += (_, _) => RefreshKeepingSelection();   // WP6b: row labels and status lines are built again in the new language
            Refresh();
        }

        private void RefreshKeepingSelection()
        {
            void Do()
            {
                var account = SelectedAccount?.Account;
                Refresh();
                if (account is not null) SelectedAccount = Accounts.FirstOrDefault(a => a.Account == account);
            }
            if (System.Windows.Application.Current?.Dispatcher is { } d && !d.CheckAccess()) d.Invoke(Do); else Do();
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
            CurrentUser = s is null ? Loc.T("auth.vm.no_session") : $"{s.Account} - {(s.Role == AuthRole.Administrator ? Loc.T("auth.vm.role_admin") : Loc.T("auth.vm.role_operator"))}{(s.IsPrimaryAdmin ? Loc.T("auth.vm.primary") : "")} - " +
                          (s.Mode == SignInMode.Card ? Loc.Format("auth.vm.mode_card", s.CardThumbprint?[..Math.Min(12, s.CardThumbprint.Length)], s.CardReader) : Loc.T("auth.vm.mode_password")) + Loc.Format("auth.vm.since", s.SignedInUtc.ToLocalTime());
            IsAdministrator = s?.IsAdministrator == true;
            IsPrimaryAdmin = s?.IsPrimaryAdmin == true;
            var svc = AuthApp.Service;
            Accounts.Clear();
            foreach (var a in svc.Accounts())
                Accounts.Add(new AccountRow(a.Account, a.Person, a.Role == AuthRole.Administrator ? Loc.T("auth.vm.row_admin") : Loc.T("auth.vm.role_operator"), a.Disabled ? Loc.T("auth.vm.state_disabled") : Loc.T("auth.vm.state_active"), a.Cards.Count,
                    a.IsPrimaryAdmin ? Loc.T("auth.vm.mode_primary") : Loc.T("auth.vm.mode_card_only"), a.Role == AuthRole.Administrator, a.Disabled));
            LoadCards();
            TrustAnchors.Clear();
            foreach (var c in svc.Trust.LoadAnchors()) TrustAnchors.Add(Loc.Format("auth.vm.anchor", c.Subject, c.NotAfter, Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(c.RawData))[..16]));
            Crls.Clear();
            foreach (var c in svc.Trust.LoadCrls()) Crls.Add(Loc.Format("auth.vm.crl", c.ThisUpdate, c.NextUpdate is { } n ? n.ToString("yyyy-MM-dd HH:mm") + "Z" : "-", c.RevokedSerials.Count));
            var p = svc.Policy;
            MissingCrlRefuse = p.MissingCrl == MissingCrlPolicy.Refuse; IdleLockMinutes = p.IdleLockMinutes; LockoutThreshold = p.LockoutThreshold;
            var v = svc.VerifyAudit();
            AuditSummary = Loc.Format("auth.vm.audit", v.Message);
            IntegrityBroken = !svc.AccountsIntegrityOk;
            Integrity = IntegrityBroken ? Loc.T("auth.vm.integrity_broken") : Loc.T("auth.vm.integrity_ok");
            var users = Dfir.Registers.RegisterStore.Load<Dfir.Registers.UsersRegister>().Register;
            var missing = users is null ? svc.Accounts().Select(a => a.Account).ToList() : svc.AccountsMissingFromRegister(users).ToList();
            RegisterGap = missing.Count == 0 ? "" : Loc.Format("auth.vm.register_gap", string.Join(", ", missing));
        }

        private void LoadCards()
        {
            Cards.Clear();
            if (SelectedAccount is null) return;
            var acc = AuthApp.Service.Find(SelectedAccount.Account);
            if (acc is null) return;
            foreach (var c in acc.Cards)
                Cards.Add(new CardRow(c.Sha256Thumbprint, c.Sha256Thumbprint[..Math.Min(16, c.Sha256Thumbprint.Length)], c.Subject, c.Serial, c.Issuer, c.Disabled ? Loc.T("auth.vm.state_disabled") : Loc.T("auth.vm.state_active"), c.EnrolledBy, c.Disabled));
        }

        private bool Report(AuthResult r, string ok)
        {
            Status = r.Ok ? ok + (string.IsNullOrEmpty(r.Message) ? "" : " " + r.Message) : Loc.Format("auth.vm.refused", Loc.Reason("auth.reason", r.Reason, r.Message));
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
                Status = found.Count == 0 ? Loc.T("auth.vm.no_cards") : Loc.Format("auth.vm.certs_on_card", found.Count);
            }
            catch (CardException ex) { Status = ex.Message; }
        }

        /// <summary>Enrols the selected detected card for the signed-in user (a replacement or an additional card). The card signs a challenge (PIN prompt).</summary>
        [RelayCommand]
        private async Task EnrollForMeAsync()
        {
            if (Session is not { } s || SelectedDetected is not { } d) { Status = Loc.T("auth.vm.choose_card"); return; }
            Status = Loc.T("auth.vm.enter_pin");
            Report(await Task.Run(() => AuthApp.Service.EnrollCard(s, s.Account, d.Card, AuthApp.Cards)), Loc.T("auth.vm.enrolled_me"));
        }

        [RelayCommand]
        private async Task EnrollForSelectedAsync()
        {
            if (Session is not { } s || SelectedAccount is not { } a || SelectedDetected is not { } d) { Status = Loc.T("auth.vm.choose_account_card"); return; }
            Status = Loc.T("auth.vm.user_enters_pin");
            Report(await Task.Run(() => AuthApp.Service.EnrollCard(s, a.Account, d.Card, AuthApp.Cards)), Loc.Format("auth.vm.enrolled_for", a.Account));
        }

        [RelayCommand]
        private void CreateAccount()
        {
            if (Session is not { } s) return;
            if (Report(AuthApp.Service.CreateAccount(s, NewAccount, NewPerson, NewIsAdministrator ? AuthRole.Administrator : AuthRole.Operator), Loc.Format("auth.vm.account_created", NewAccount)))
            { NewAccount = ""; NewPerson = ""; NewIsAdministrator = false; }
        }

        [RelayCommand]
        private void ToggleRole()
        {
            if (Session is not { } s || SelectedAccount is not { } a) return;
            Report(AuthApp.Service.SetRole(s, a.Account, a.IsAdministrator ? AuthRole.Operator : AuthRole.Administrator), Loc.T("auth.vm.role_changed"));
        }

        [RelayCommand]
        private void ToggleAccountDisabled()
        {
            if (Session is not { } s || SelectedAccount is not { } a) return;
            Report(AuthApp.Service.SetAccountDisabled(s, a.Account, !a.IsDisabled), !a.IsDisabled ? Loc.T("auth.vm.account_disabled") : Loc.T("auth.vm.account_enabled"));
        }

        [RelayCommand]
        private void ToggleCardDisabled()
        {
            if (Session is not { } s || SelectedAccount is not { } a || SelectedCard is not { } c) return;
            Report(AuthApp.Service.SetCardDisabled(s, a.Account, c.Thumbprint, !c.IsDisabled), !c.IsDisabled ? Loc.T("auth.vm.card_disabled") : Loc.T("auth.vm.card_enabled"));
        }

        [RelayCommand]
        private void ImportCa()
        {
            if (Session is not { } s) return;
            var dlg = new OpenFileDialog { Title = Loc.T("auth.vm.dialog_ca"), Filter = Loc.T("auth.vm.filter_certs") };
            if (dlg.ShowDialog() == true) Report(AuthApp.Service.ImportTrustCertificate(s, File.ReadAllBytes(dlg.FileName), dlg.FileName), Loc.T("auth.vm.ca_imported"));
        }

        [RelayCommand]
        private void ImportCrl()
        {
            if (Session is not { } s) return;
            var dlg = new OpenFileDialog { Title = Loc.T("auth.vm.dialog_crl"), Filter = Loc.T("auth.vm.filter_crl") };
            if (dlg.ShowDialog() == true) Report(AuthApp.Service.ImportCrl(s, File.ReadAllBytes(dlg.FileName), dlg.FileName), Loc.T("auth.vm.crl_imported"));
        }

        [RelayCommand]
        private void SavePolicy()
        {
            if (Session is not { } s) return;
            Report(AuthApp.Service.SetPolicy(s, new AuthPolicy
            {
                MissingCrl = MissingCrlRefuse ? MissingCrlPolicy.Refuse : MissingCrlPolicy.Warn, IdleLockMinutes = IdleLockMinutes, LockoutThreshold = LockoutThreshold,
            }), Loc.T("auth.vm.policy_saved"));
        }

        [RelayCommand]
        private void AcceptIntegrity()
        {
            if (Session is not { } s) return;
            Report(AuthApp.Service.AcceptCurrentAccountsState(s), Loc.T("auth.vm.integrity_accepted"));
        }

        /// <summary>Called by the view with the three password boxes (primary administrator only).</summary>
        public void ChangePassword(string current, string next, string confirm)
        {
            if (Session is not { } s) return;
            if (next != confirm) { Status = Loc.T("auth.vm.passwords_differ"); return; }
            Report(AuthApp.Service.ChangePassword(s, current, next), Loc.T("auth.vm.password_changed"));
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
