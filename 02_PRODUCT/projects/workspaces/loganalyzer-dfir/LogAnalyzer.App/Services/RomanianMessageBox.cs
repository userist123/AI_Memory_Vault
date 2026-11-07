using System;
using System.Runtime.InteropServices;
using System.Text;

namespace LogAnalyzer.UI.Services
{
    /// <summary>
    /// The buttons of the standard message box are drawn by Windows in the language of Windows: an English Windows showed „Yes / No / Cancel” under a
    /// Romanian question (lab VMs 2026-11-07). A thread hook renames them „Da / Nu / Anulare” as each message box opens, so every
    /// <c>MessageBox.Show</c> of the application is covered without changing its calls. Only message boxes are touched: they are the dialogs that carry
    /// the message text control (id 0xFFFF); file dialogs and the application's own windows are left alone.
    /// </summary>
    internal static class RomanianMessageBox
    {
        private const int WH_CBT = 5, HCBT_ACTIVATE = 5, MessageTextId = 0xFFFF;

        /// <summary>Button ids of the Win32 message box (IDYES, IDNO, IDCANCEL) and the Romanian captions.</summary>
        internal static readonly (int Id, string Text)[] Captions = [(6, "&Da"), (7, "&Nu"), (2, "Anulare")];

        private delegate IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam);
        [ThreadStatic] private static IntPtr _hook;
        [ThreadStatic] private static HookProc? _proc;   // kept alive while the hook is installed

        /// <summary>Installs the hook on the calling (UI) thread; a second call on the same thread does nothing.</summary>
        public static bool Install()
        {
            if (_hook != IntPtr.Zero) return true;
            _proc = Hook;
            _hook = SetWindowsHookEx(WH_CBT, _proc, IntPtr.Zero, GetCurrentThreadId());
            return _hook != IntPtr.Zero;
        }

        /// <summary>Removes the hook of the calling thread (tests).</summary>
        public static void Uninstall()
        {
            if (_hook == IntPtr.Zero) return;
            UnhookWindowsHookEx(_hook);
            _hook = IntPtr.Zero; _proc = null;
        }

        private static IntPtr Hook(int code, IntPtr wParam, IntPtr lParam)
        {
            if (code == HCBT_ACTIVATE && IsMessageBox(wParam))
            {
                // an OK-only box gives its single „OK” button id 2 (IDCANCEL): id 2 is „Anulare” only beside another button (OK, Yes or No)
                bool cancelIsSecond = GetDlgItem(wParam, 1) != IntPtr.Zero || GetDlgItem(wParam, 6) != IntPtr.Zero || GetDlgItem(wParam, 7) != IntPtr.Zero;
                foreach (var (id, text) in Captions)
                    if ((id != 2 || cancelIsSecond) && GetDlgItem(wParam, id) != IntPtr.Zero) SetDlgItemText(wParam, id, text);
            }
            return CallNextHookEx(_hook, code, wParam, lParam);
        }

        private static bool IsMessageBox(IntPtr window)
        {
            var name = new StringBuilder(32);
            return GetClassName(window, name, name.Capacity) > 0 && name.ToString() == "#32770" && GetDlgItem(window, MessageTextId) != IntPtr.Zero;
        }

        [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SetWindowsHookEx(int idHook, HookProc lpfn, IntPtr hMod, uint dwThreadId);
        [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(IntPtr hhk);
        [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")] private static extern IntPtr GetDlgItem(IntPtr hDlg, int nIDDlgItem);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool SetDlgItemText(IntPtr hDlg, int nIDDlgItem, string lpString);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);
        [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    }
}
