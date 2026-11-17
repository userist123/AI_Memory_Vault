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

    /// <summary>
    /// Chained append: under one exclusive-for-writers handle reads the last non-empty line (null when the file is empty), asks <paramref name="text"/> for the
    /// text to append (UTF-8, no BOM) and writes it at the end. Read and append are atomic against other writers, so a chain (previous hash, counter) cannot fork.
    /// </summary>
    public static void WriteAfterLastLine(string path, Func<string?, string> text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        for (int attempt = 0; ; attempt++)
        {
            FileStream fs;
            try { fs = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.Read); }
            catch (IOException ex) when (attempt < MaxAttempts && IsSharingViolation(ex)) { Thread.Sleep(Math.Min(5 + attempt, 50)); continue; }
            using (fs)
            {
                var bytes = System.Text.Encoding.UTF8.GetBytes(text(LastNonEmptyLine(fs)));
                fs.Seek(0, SeekOrigin.End);
                fs.Write(bytes);
                fs.Flush();
            }
            return;
        }
    }

    /// <summary>
    /// Rewrites a whole existing file under one handle that excludes other writers (readers allowed): reads it, asks <paramref name="transform"/> for the
    /// new text (null = leave the file unchanged) and writes it in place. Read and rewrite are atomic against <see cref="WriteAfterLastLine"/> and
    /// <see cref="Write"/> from another process, so a line appended meanwhile is never dropped (a temp file + move would replace it). The new text is
    /// written first to <c>path + ".tmp"</c>, removed after the rewrite, so a crash in the middle of the rewrite leaves the full new content beside the file.
    /// </summary>
    public static void RewriteUnderHandle(string path, Func<string, string?> transform)
    {
        for (int attempt = 0; ; attempt++)
        {
            FileStream fs;
            try { fs = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.Read); }
            catch (IOException ex) when (attempt < MaxAttempts && IsSharingViolation(ex)) { Thread.Sleep(Math.Min(5 + attempt, 50)); continue; }
            using (fs)
            {
                var old = new byte[fs.Length];
                fs.ReadExactly(old);
                // a leading UTF-8 BOM is skipped, as File.ReadLines does for the readers of the same file
                int skip = old.Length >= 3 && old[0] == 0xEF && old[1] == 0xBB && old[2] == 0xBF ? 3 : 0;
                var next = transform(System.Text.Encoding.UTF8.GetString(old, skip, old.Length - skip));
                if (next is null) return;
                var bytes = System.Text.Encoding.UTF8.GetBytes(next);
                var backup = path + ".tmp";
                File.WriteAllBytes(backup, bytes);
                fs.Seek(0, SeekOrigin.Begin);
                fs.Write(bytes);
                fs.SetLength(bytes.Length);
                fs.Flush(true);
                // best effort: the file is already rewritten; a scanner holding the backup must not turn a done change into an error (callers audit after this)
                try { File.Delete(backup); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            }
            return;
        }
    }

    internal static string? LastNonEmptyLine(FileStream fs)
    {
        long end = fs.Length;
        if (end == 0) return null;
        var buffer = new List<byte>();
        var chunk = new byte[4096];
        long pos = end;
        while (pos > 0)
        {
            int n = (int)Math.Min(chunk.Length, pos);
            pos -= n;
            fs.Seek(pos, SeekOrigin.Begin);
            fs.ReadExactly(chunk, 0, n);
            buffer.InsertRange(0, chunk.Take(n));
            var text = System.Text.Encoding.UTF8.GetString(buffer.ToArray()).TrimEnd('\r', '\n', ' ', '\t');
            int nl = text.LastIndexOf('\n');
            if (nl >= 0) return text[(nl + 1)..].TrimEnd('\r');
            if (pos == 0) return text.Length > 0 ? text : null;
        }
        return null;
    }

    /// <summary>Appends one UTF-8 text (no BOM), as File.AppendAllText did.</summary>
    public static void WriteText(string path, string text) => Write(path, _ => System.Text.Encoding.UTF8.GetBytes(text));

    // ERROR_SHARING_VIOLATION (32) / ERROR_LOCK_VIOLATION (33); anything else (disk full, access denied, missing folder) fails at once.
    internal static bool IsSharingViolation(IOException ex) => (ex.HResult & 0xFFFF) is 32 or 33 || !OperatingSystem.IsWindows();
}