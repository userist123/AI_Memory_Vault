using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using LogAnalyzer.UI.Services;
using Xunit;

namespace LogAnalyzer.App.Tests;

/// <summary>Lab VMs 2026-11-07: an English Windows drew „Yes / No / Cancel” under the application's Romanian questions.</summary>
[Collection(GlobalStateCollection.Name)]
public sealed class RomanianMessageBoxTests
{
    [Fact]
    public void A_question_box_shows_Da_Nu_Anulare_and_still_returns_the_choice()
    {
        var (seen, result) = Show(MessageBoxButton.YesNoCancel, [6, 7, 2], click: 7);
        Assert.Equal(["&Da", "&Nu", "Anulare"], seen);
        Assert.Equal(MessageBoxResult.No, result);
        Assert.Equal(["&Da", "&Nu"], Show(MessageBoxButton.YesNo, [6, 7], click: 6).Seen);
    }

    [Fact]
    public void An_OK_only_box_keeps_OK_on_its_single_button_and_OK_Cancel_gets_Anulare()
    {
        // review 2026-11-07: an OK-only box gives its single button id 2 (IDCANCEL); renaming id 2 would have shown „Anulare” on every error box
        var (seen, result) = Show(MessageBoxButton.OK, [2], click: 2);
        Assert.Equal(["OK"], seen);
        Assert.Equal(MessageBoxResult.OK, result);
        Assert.Equal(["OK", "Anulare"], Show(MessageBoxButton.OKCancel, [1, 2], click: 2).Seen);
    }

    private static (List<string> Seen, MessageBoxResult Result) Show(MessageBoxButton buttons, int[] ids, int click)
    {
        string caption = "LA-test-" + Guid.NewGuid().ToString("N");
        var seen = new List<string>();
        Task? closer = null;
        var shown = new ManualResetEventSlim(false);
        // the closer starts on the UI thread right before the box opens and keeps trying until Show returns: a modal box left open would hang the
        // shared UI thread of every test (round-2 review)
        var result = Wp18AccessibilityTests.OnSta(() =>
        {
            Assert.True(RomanianMessageBox.Install());
            closer = Task.Run(() =>
            {
                bool read = false;
                while (!shown.IsSet)
                {
                    var h = FindWindow("#32770", caption);
                    if (h != IntPtr.Zero)
                    {
                        if (!read) { Thread.Sleep(200); foreach (var id in ids) seen.Add(Text(GetDlgItem(h, id))); read = true; }
                        // WM_COMMAND with the button id ends the box like a click, without needing the box in the foreground (BM_CLICK does)
                        PostMessage(h, 0x0111 /* WM_COMMAND */, (IntPtr)click, GetDlgItem(h, click));
                        for (int j = 0; j < 30 && IsWindow(h); j++) Thread.Sleep(100);
                        foreach (var id in new[] { 2, 7, 1, 6 }) if (IsWindow(h)) PostMessage(h, 0x0111, (IntPtr)id, GetDlgItem(h, id));
                    }
                    Thread.Sleep(50);
                }
            });
            try { return MessageBox.Show("Continuați?", caption, buttons); }
            finally { shown.Set(); RomanianMessageBox.Uninstall(); }
        });
        closer?.Wait(TimeSpan.FromSeconds(15));
        return (seen, result);
    }

    private static string Text(IntPtr h)
    {
        var sb = new StringBuilder(64);
        GetWindowText(h, sb, sb.Capacity);
        return sb.ToString();
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr FindWindow(string? lpClassName, string lpWindowName);
    [DllImport("user32.dll")] private static extern IntPtr GetDlgItem(IntPtr hDlg, int id);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int max);
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr hWnd, int msg, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr hWnd);
}
