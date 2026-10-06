using System.Runtime.InteropServices;
using System.Text;

namespace LogAnalyzer.Dfir.Windows.Native;

/// <summary>
/// Minimal read-only ESE (Extensible Storage Engine) reader over esent.dll, sufficient for SRUM.
/// Always open a WORKING COPY: attaching a database writes to its directory (checkpoint/logs).
/// </summary>
internal sealed class EseReadOnlyDatabase : IDisposable
{
    private const int JetParamSystemPath = 0, JetParamTempPath = 1, JetParamLogFilePath = 2, JetParamRecovery = 34, JetParamDatabasePageSize = 64;
    private const uint JetBitDbReadOnly = 0x1, JetBitTableReadOnly = 0x4;
    private const int JetMoveFirst = unchecked((int)0x80000000), JetMoveNext = 1;
    private const int JetWrnColumnNull = 1004, JetErrNoCurrentRecord = -1603;

    [StructLayout(LayoutKind.Sequential)]
    private struct JET_COLUMNDEF
    {
        public uint cbStruct, columnid, coltyp; public ushort wCountry, langid, cp, wCollate; public uint cbMax, grbit;
    }

    [DllImport("esent.dll", CharSet = CharSet.Unicode)] private static extern int JetSetSystemParameterW(IntPtr pinstance, IntPtr sesid, uint paramid, IntPtr lParam, string? szParam);
    [DllImport("esent.dll", CharSet = CharSet.Unicode)] private static extern int JetCreateInstanceW(out IntPtr instance, string name);
    [DllImport("esent.dll")] private static extern int JetInit(ref IntPtr instance);
    [DllImport("esent.dll")] private static extern int JetTerm(IntPtr instance);
    [DllImport("esent.dll", CharSet = CharSet.Unicode)] private static extern int JetBeginSessionW(IntPtr instance, out IntPtr sesid, string? user, string? pwd);
    [DllImport("esent.dll")] private static extern int JetEndSession(IntPtr sesid, uint grbit);
    [DllImport("esent.dll", CharSet = CharSet.Unicode)] private static extern int JetAttachDatabase2W(IntPtr sesid, string file, uint cpgDatabaseSizeMax, uint grbit);
    [DllImport("esent.dll", CharSet = CharSet.Unicode)] private static extern int JetDetachDatabaseW(IntPtr sesid, string file);
    [DllImport("esent.dll", CharSet = CharSet.Unicode)] private static extern int JetOpenDatabaseW(IntPtr sesid, string file, string? connect, out uint dbid, uint grbit);
    [DllImport("esent.dll")] private static extern int JetCloseDatabase(IntPtr sesid, uint dbid, uint grbit);
    [DllImport("esent.dll", CharSet = CharSet.Unicode)] private static extern int JetOpenTableW(IntPtr sesid, uint dbid, string table, IntPtr parameters, uint cbParameters, uint grbit, out IntPtr tableid);
    [DllImport("esent.dll")] private static extern int JetCloseTable(IntPtr sesid, IntPtr tableid);
    [DllImport("esent.dll", CharSet = CharSet.Unicode)] private static extern int JetGetTableColumnInfoW(IntPtr sesid, IntPtr tableid, string column, ref JET_COLUMNDEF def, uint cbMax, uint infoLevel);
    [DllImport("esent.dll")] private static extern int JetMove(IntPtr sesid, IntPtr tableid, int cRow, uint grbit);
    [DllImport("esent.dll")] private static extern int JetRetrieveColumn(IntPtr sesid, IntPtr tableid, uint columnid, byte[]? data, uint cbData, out uint cbActual, uint grbit, IntPtr retinfo);

    private IntPtr _instance, _session;
    private readonly uint _dbid;
    private readonly string _path;

    public EseReadOnlyDatabase(string workingCopyPath, int pageSize = 4096)
    {
        _path = Path.GetFullPath(workingCopyPath);
        var dir = Path.GetDirectoryName(_path)! + Path.DirectorySeparatorChar;
        JetSetSystemParameterW(IntPtr.Zero, IntPtr.Zero, JetParamDatabasePageSize, (IntPtr)pageSize, null);
        Check(JetCreateInstanceW(out _instance, "ladfir-" + Guid.NewGuid().ToString("N")[..8]), "JetCreateInstance");
        var pi = Marshal.AllocHGlobal(IntPtr.Size);
        try
        {
            Marshal.WriteIntPtr(pi, _instance);
            foreach (var p in new[] { JetParamSystemPath, JetParamTempPath, JetParamLogFilePath }) JetSetSystemParameterW(pi, IntPtr.Zero, (uint)p, IntPtr.Zero, dir);
            JetSetSystemParameterW(pi, IntPtr.Zero, JetParamRecovery, IntPtr.Zero, "Off");
        }
        finally { Marshal.FreeHGlobal(pi); }
        Check(JetInit(ref _instance), "JetInit");
        Check(JetBeginSessionW(_instance, out _session, null, null), "JetBeginSession");
        Check(JetAttachDatabase2W(_session, _path, 0, JetBitDbReadOnly), "JetAttachDatabase2");
        Check(JetOpenDatabaseW(_session, _path, null, out _dbid, JetBitDbReadOnly), "JetOpenDatabase");
    }

    public sealed class Table : IDisposable
    {
        private readonly EseReadOnlyDatabase _db; private readonly IntPtr _t;
        private readonly Dictionary<string, uint> _cols = new(StringComparer.OrdinalIgnoreCase);
        internal Table(EseReadOnlyDatabase db, IntPtr t) { _db = db; _t = t; }

        public uint Column(string name)
        {
            if (_cols.TryGetValue(name, out var id)) return id;
            var def = new JET_COLUMNDEF { cbStruct = (uint)Marshal.SizeOf<JET_COLUMNDEF>() };
            Check(JetGetTableColumnInfoW(_db._session, _t, name, ref def, def.cbStruct, 0), "JetGetTableColumnInfo " + name);
            return _cols[name] = def.columnid;
        }

        public IEnumerable<Table> Rows()
        {
            int rc = JetMove(_db._session, _t, JetMoveFirst, 0);
            while (rc >= 0)
            {
                yield return this;
                rc = JetMove(_db._session, _t, JetMoveNext, 0);
            }
            if (rc != JetErrNoCurrentRecord) Check(rc, "JetMove");
        }

        public byte[]? Bytes(uint column)
        {
            var buf = new byte[256];
            int rc = JetRetrieveColumn(_db._session, _t, column, buf, (uint)buf.Length, out uint act, 0, IntPtr.Zero);
            if (rc == JetWrnColumnNull) return null;
            if (rc < 0) Check(rc, "JetRetrieveColumn");
            if (act > buf.Length)
            {
                buf = new byte[act];
                Check(JetRetrieveColumn(_db._session, _t, column, buf, act, out act, 0, IntPtr.Zero), "JetRetrieveColumn(long)");
            }
            return buf[..(int)act];
        }

        public int? Int32(uint c) => Bytes(c) is { Length: >= 4 } b ? BitConverter.ToInt32(b) : null;
        public long? Int64(uint c) => Bytes(c) is { Length: >= 8 } b ? BitConverter.ToInt64(b) : null;
        public byte? Byte(uint c) => Bytes(c) is { Length: >= 1 } b ? b[0] : null;
        public double? Double(uint c) => Bytes(c) is { Length: >= 8 } b ? BitConverter.ToDouble(b) : null;
        public string? Utf16(uint c) => Bytes(c) is { } b ? Encoding.Unicode.GetString(b).TrimEnd('\0') : null;

        public void Dispose() => JetCloseTable(_db._session, _t);
    }

    public Table OpenTable(string name)
    {
        Check(JetOpenTableW(_session, _dbid, name, IntPtr.Zero, 0, JetBitTableReadOnly, out var t), "JetOpenTable " + name);
        return new Table(this, t);
    }

    private static void Check(int rc, string what)
    {
        if (rc < 0) throw new InvalidOperationException($"{what} failed (JET error {rc}).");
    }

    public void Dispose()
    {
        if (_session != IntPtr.Zero)
        {
            JetCloseDatabase(_session, _dbid, 0);
            JetDetachDatabaseW(_session, _path);
            JetEndSession(_session, 0);
            _session = IntPtr.Zero;
        }
        if (_instance != IntPtr.Zero) { JetTerm(_instance); _instance = IntPtr.Zero; }
    }
}
