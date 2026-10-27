using System;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using DSOptimizer.Core.Abstractions;
using DSOptimizer.Windows.Hardware;

namespace DSOptimizer.Windows;

/// <summary>
/// Czy komputer należy do domeny: <c>SELECT PartOfDomain FROM Win32_ComputerSystem</c> (tylko odczyt CIM).
/// Błąd odczytu albo brak wartości = <c>null</c> (wywołujący traktuje to jako "nie wiadomo", nie jako "poza domeną").
/// </summary>
public sealed class WindowsDomainInfo : IDomainInfo
{
    public bool? IsDomainJoined()
    {
        try
        {
            return CimReader.Query(
                    CimReader.Cimv2,
                    "SELECT PartOfDomain FROM Win32_ComputerSystem",
                    static computer => CimReader.Bool(computer, "PartOfDomain"))
                .FirstOrDefault();
        }
        catch (Exception ex) when (ex is ManagementException or COMException or UnauthorizedAccessException or InvalidOperationException)
        {
            return null;
        }
    }
}
