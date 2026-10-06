using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;

namespace LogAnalyzer.Core.Services.Connectivity
{
    /// <summary>
    /// Passive connectivity probe. It asks Windows what it already knows (Network List Manager, which holds the
    /// result of the OS's own connectivity check) and, as a fallback, reads local interface state.
    /// It never sends a packet: on a station under investigation, or one meant to be isolated, the analysis tool
    /// itself must not contact anything to find out whether it could.
    /// </summary>
    public sealed class WindowsConnectivityProbe : IConnectivityProbe
    {
        // NLM_CONNECTIVITY flags (netlistmgr.h).
        internal const int Ipv4Subnet = 0x10, Ipv4LocalNetwork = 0x20, Ipv4Internet = 0x40;
        internal const int Ipv6Subnet = 0x100, Ipv6LocalNetwork = 0x200, Ipv6Internet = 0x400;

        public ConnectivitySnapshot Probe()
        {
            var details = new List<string>();
            if (!OperatingSystem.IsWindows())
                return ConnectivitySnapshot.Unknown("not Windows");

            try
            {
                int flags = QueryNetworkListManager();
                details.Add($"NLM_CONNECTIVITY=0x{flags:X}");
                details.AddRange(DescribeInterfaces());
                return new ConnectivitySnapshot(MapNlmConnectivity(flags), "Windows Network List Manager", details, DateTime.UtcNow);
            }
            catch (Exception ex) when (ex is COMException or InvalidCastException or UnauthorizedAccessException or TypeLoadException)
            {
                details.Add($"NLM indisponibil: {ex.GetType().Name}");
            }

            try
            {
                var state = InferFromInterfaces(out var ifaceDetails);
                details.AddRange(ifaceDetails);
                return new ConnectivitySnapshot(state, "NetworkInterface (fallback)", details, DateTime.UtcNow);
            }
            catch (NetworkInformationException ex)
            {
                details.Add($"NetworkInterface indisponibil: {ex.Message}");
                return new ConnectivitySnapshot(ConnectivityState.Unknown, "none", details, DateTime.UtcNow);
            }
        }

        public static ConnectivityState MapNlmConnectivity(int flags)
        {
            if ((flags & (Ipv4Internet | Ipv6Internet)) != 0) return ConnectivityState.Internet;
            if ((flags & (Ipv4LocalNetwork | Ipv6LocalNetwork | Ipv4Subnet | Ipv6Subnet)) != 0) return ConnectivityState.LocalNetworkOnly;
            return ConnectivityState.NoNetwork;
        }

        /// <summary>
        /// Without NLM, interface state cannot prove Internet access, so a connected interface counts only as a
        /// local network. Detection then falls back to AirGapped, which is the safe side.
        /// </summary>
        private static ConnectivityState InferFromInterfaces(out List<string> details)
        {
            details = DescribeInterfaces().ToList();
            bool anyUp = NetworkInterface.GetAllNetworkInterfaces().Any(IsRealConnectedInterface);
            return anyUp ? ConnectivityState.LocalNetworkOnly : ConnectivityState.NoNetwork;
        }

        private static IEnumerable<string> DescribeInterfaces()
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces().Where(IsRealConnectedInterface))
            {
                bool gateway = nic.GetIPProperties().GatewayAddresses.Any(g => !g.Address.Equals(System.Net.IPAddress.Any));
                yield return $"{nic.Name} ({nic.NetworkInterfaceType}, up{(gateway ? ", gateway" : "")})";
            }
        }

        // Up, not loopback/tunnel, and holding a routable unicast address (filter-driver pseudo interfaces and
        // adapters with only an APIPA/link-local address do not count as a connected network).
        private static bool IsRealConnectedInterface(NetworkInterface nic)
        {
            if (nic.OperationalStatus != OperationalStatus.Up ||
                nic.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
                return false;
            try
            {
                return nic.GetIPProperties().UnicastAddresses.Any(a => IsRoutable(a.Address));
            }
            catch (NetworkInformationException)
            {
                return false;
            }
        }

        private static bool IsRoutable(System.Net.IPAddress ip)
        {
            if (System.Net.IPAddress.IsLoopback(ip) || ip.IsIPv6LinkLocal) return false;
            var b = ip.GetAddressBytes();
            return !(b.Length == 4 && b[0] == 169 && b[1] == 254);
        }

        [System.Runtime.Versioning.SupportedOSPlatform("windows")]
        private static int QueryNetworkListManager()
        {
            var type = Type.GetTypeFromCLSID(new Guid("DCB00C01-570F-4A9B-8D69-199FDBA5723B"), throwOnError: true)!;
            object instance = Activator.CreateInstance(type)!;
            try
            {
                return ((INetworkListManager)instance).GetConnectivity();
            }
            finally
            {
                Marshal.FinalReleaseComObject(instance);
            }
        }

        [ComImport]
        [Guid("DCB00000-570F-4A9B-8D69-199FDBA5723B")]
        [InterfaceType(ComInterfaceType.InterfaceIsDual)]
        private interface INetworkListManager
        {
            // Methods in vtable order; only GetConnectivity is called.
            [return: MarshalAs(UnmanagedType.Interface)] object GetNetworks(int flags);
            [return: MarshalAs(UnmanagedType.Interface)] object GetNetwork(Guid networkId);
            [return: MarshalAs(UnmanagedType.Interface)] object GetNetworkConnections();
            [return: MarshalAs(UnmanagedType.Interface)] object GetNetworkConnection(Guid networkConnectionId);
            bool IsConnectedToInternet { [return: MarshalAs(UnmanagedType.VariantBool)] get; }
            bool IsConnected { [return: MarshalAs(UnmanagedType.VariantBool)] get; }
            int GetConnectivity();
        }
    }
}
