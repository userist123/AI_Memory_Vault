namespace LogAnalyzer.Dfir.IO;

/// <summary>
/// Appends to a file that several processes may write at once (two app instances on one station share the LIVE case; lab 2026-11-16: the second
/// failed with "used by another process" on LogAnalyzer_Audit.log). Each write opens the file alone for writing (readers allowed), so lines from two
/// processes never interleave; a sharing or lock violation is retried for about 20 s (400 attempts, growing pause), like the chained logs (<c>HashChain.OpenExclusive</c>).
/// Order of lines between processes is the order the file was obtained, not the order the events happened; the chained JSONL stays the reference.
/// </summary>
public static class SharedAppend
{
    internal const int MaxAttempts = 400;

    /// <summary>Appends the bytes <paramref name="content"/> returns; its argument says whether the file is empty (decided under the same handle).</summary>
    public static void Write(string path, Func<bool, byte[]> content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        for (int attempt = 0; ; attempt++)
        {
            FileStream fs;
            try { fs = new FileStream(path, FileMode.OpenOrCreate, FileAccess.Write, FileShare.Read); }
            catch (IOException ex) when (attempt < MaxAttempts && IsSharingViolation(ex)) { Thread.Sleep(Math.Min(5 + attempt, 50)); continue; }
            using (fs)
            {
                var bytes = content(fs.Length == 0);
                fs.Seek(0, SeekOrigin.End);
                fs.Write(bytes);
                fs.Flush();
            }
            return;
        }
    }

    /// <summary>Appends one UTF-8 text (no BOM), as File.AppendAllText did.</summary>
    public static void WriteText(string path, string text) => Write(path, _ => System.Text.Encoding.UTF8.GetBytes(text));

    // ERROR_SHARING_VIOLATION (32) / ERROR_LOCK_VIOLATION (33); anything else (disk full, access denied, missing folder) fails at once.
    internal static bool IsSharingViolation(IOException ex) => (ex.HResult & 0xFFFF) is 32 or 33 || !OperatingSystem.IsWindows();
}