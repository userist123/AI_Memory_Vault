using LogAnalyzer.Dfir.Case;
using LogAnalyzer.Dfir.IO;

// Test helper (see the csproj). Commands:
//   append <logPath> <keyHex> <count> <tag>   register the seal key, append <count> entries {Tag, I}
//   verify <logPath> <keyHex>                 print "STATUS <status>", "MESSAGE <text>", "ENTRIES <n>"
//   sharedappend <path> <count> <tag>         SharedAppend.WriteText <count> lines "<tag>|<i>|<padding>"
// Fault injection (LA_CHAIN_FAULT) is switched on here only.
// Finding B10: every executable of the solution warns on a .NET runtime below 10.0.13 (never blocks).
if (LogAnalyzer.Dfir.Reporting.RuntimeGuard.Warning(Environment.Version) is { } runtimeWarning) Console.Error.WriteLine("[!] Atenție .NET: " + runtimeWarning + ".");
HashChain.FaultInjection = true;
try
{
    switch (args.Length > 0 ? args[0] : "")
    {
        case "append" when args.Length == 5:
        {
            var log = args[1];
            HashChain.RegisterHeadSeal(log, Convert.FromHexString(args[2]));
            var chain = new HashChain(log);
            int count = int.Parse(args[3]);
            for (int i = 1; i <= count; i++) chain.Append(new { Tag = args[4], I = i });
            return 0;
        }
        case "verify" when args.Length == 3:
        {
            var log = args[1];
            HashChain.RegisterHeadSeal(log, Convert.FromHexString(args[2]));
            var v = new HashChain(log).Verify();
            Console.WriteLine("STATUS " + v.Status);
            Console.WriteLine("MESSAGE " + v.Message);
            Console.WriteLine("ENTRIES " + v.Entries);
            return 0;
        }
        case "sharedappend" when args.Length == 4:
        {
            int count = int.Parse(args[2]);
            var pad = new string('x', 200);
            for (int i = 1; i <= count; i++) SharedAppend.WriteText(args[1], $"{args[3]}|{i}|{pad}\n");
            return 0;
        }
        default:
            Console.Error.WriteLine("usage: append <log> <keyHex> <count> <tag> | verify <log> <keyHex> | sharedappend <path> <count> <tag>");
            return 2;
    }
}
catch (Exception ex)
{
    Console.Error.WriteLine(ex.ToString());
    return 1;
}
