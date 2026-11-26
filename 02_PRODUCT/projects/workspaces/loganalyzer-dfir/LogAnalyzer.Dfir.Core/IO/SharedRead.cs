using System.Text;

namespace LogAnalyzer.Dfir.IO;

/// <summary>
/// Reads a case file that another process may hold open for a moment (two app instances share the LIVE case; <see cref="SharedAppend"/> and the chained logs
/// open the file with write access). File.ReadLines / ReadAllText request share-Read and so fail with a sharing violation while a writer holds the file.
/// The read keeps share-Read (an in-place rewrite is still excluded, a reader never sees a half-rewritten file) and retries only sharing or lock
/// violations, for about 20 s like <see cref="SharedAppend"/>. Other errors (missing file, directory, access denied) are not retried. The whole read happens
/// under the open handle, so the retry covers it and no handle is left to a lazy enumerator. BOM and line splitting are those of File.ReadLines/ReadAllText.
/// </summary>
public static class SharedRead
{
    public static IEnumerable<string> Lines(string path) => Read(path, static r =>
    {
        var list = new List<string>();
        while (r.ReadLine() is { } line) list.Add(line);
        return list;
    });

    public static string AllText(string path) => Read(path, static r => r.ReadToEnd());

    private static T Read<T>(string path, Func<StreamReader, T> read)
    {
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                using var r = new StreamReader(fs, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
                return read(r);
            }
            catch (IOException ex) when (attempt < SharedAppend.MaxAttempts && SharedAppend.IsSharingViolation(ex))
            {
                Thread.Sleep(Math.Min(5 + attempt, 50));
            }
        }
    }
}
