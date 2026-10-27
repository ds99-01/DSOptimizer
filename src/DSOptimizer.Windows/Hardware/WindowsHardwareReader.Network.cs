using System.Collections.Generic;
using System.Linq;
using DSOptimizer.Core.Hardware;

namespace DSOptimizer.Windows.Hardware;

public sealed partial class WindowsHardwareReader
{
    private const uint MediumNative80211 = 9;
    private const uint MediumWirelessLan = 1;
    private const uint Medium8023 = 14;
    private const uint MediaConnected = 1;

    /// <summary>Tylko karty fizyczne: <c>HardwareInterface</c> i nie <c>Virtual</c> (WireGuard, vEthernet odpadają).</summary>
    public IReadOnlyList<NetworkAdapterInfo> ReadNetwork()
        => CimReader.Query(
                CimReader.StandardCimv2,
                "SELECT InterfaceDescription, HardwareInterface, Virtual, MediaConnectState, Speed, NdisPhysicalMedium FROM MSFT_NetAdapter",
                static a => (
                    Physical: CimReader.Bool(a, "HardwareInterface") == true && CimReader.Bool(a, "Virtual") == false,
                    Adapter: new NetworkAdapterInfo(
                        CimReader.Text(a, "InterfaceDescription"),
                        NetworkKindOf(CimReader.UInt(a, "NdisPhysicalMedium")),
                        CimReader.UInt(a, "MediaConnectState") == MediaConnected,
                        CimReader.ULong(a, "Speed"))))
            .Where(a => a.Physical)
            .Select(a => a.Adapter)
            .ToList();

    /// <summary><c>NdisPhysicalMedium</c>: 14 (802.3) → Ethernet; 9 (Native 802.11) i 1 (Wireless LAN) → Wi-Fi; reszta → inna.</summary>
    internal static NetworkKind NetworkKindOf(uint? ndisPhysicalMedium) => ndisPhysicalMedium switch
    {
        Medium8023 => NetworkKind.Ethernet,
        MediumNative80211 or MediumWirelessLan => NetworkKind.WiFi,
        _ => NetworkKind.Other,
    };
}
