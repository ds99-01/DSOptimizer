using System;
using System.Collections.Generic;
using System.Globalization;
using System.Management;
using DSOptimizer.Core.Abstractions;

namespace DSOptimizer.Windows;

/// <summary>Wywołanie root\default:SystemRestore.CreateRestorePoint oraz odczyt listy punktów. Weryfikacja na VM 26H2 w Etapie 1b.</summary>
public sealed class WmiRestorePointApi : IRestorePointApi
{
    private const uint ModifySettings = 12;
    private const uint BeginSystemChange = 100;

    public uint CreateRestorePoint(string description)
    {
        if (description is null)
        {
            throw new ArgumentNullException(nameof(description));
        }

        using var restore = new ManagementClass(new ManagementScope(@"\\.\root\default"), new ManagementPath("SystemRestore"), null);
        using var input = restore.GetMethodParameters("CreateRestorePoint");
        input["Description"] = description;
        input["RestorePointType"] = ModifySettings;
        input["EventType"] = BeginSystemChange;
        using var result = restore.InvokeMethod("CreateRestorePoint", input, null);
        return Convert.ToUInt32(result["ReturnValue"], CultureInfo.InvariantCulture);
    }

    /// <summary>Odczyt przez WMI (root\default:SystemRestore). Brak punktów lub wyłączona ochrona = pusta lista, bez wyjątku.</summary>
    public IReadOnlyList<RestorePointInfo> GetRestorePoints()
    {
        var scope = new ManagementScope(@"\\.\root\default");
        using var searcher = new ManagementObjectSearcher(scope, new ObjectQuery("SELECT SequenceNumber, Description FROM SystemRestore"));
        using var collection = searcher.Get();
        var points = new List<RestorePointInfo>();
        foreach (ManagementBaseObject item in collection)
        {
            using (item)
            {
                points.Add(new RestorePointInfo(
                    Convert.ToUInt32(item["SequenceNumber"], CultureInfo.InvariantCulture),
                    Convert.ToString(item["Description"], CultureInfo.InvariantCulture) ?? string.Empty));
            }
        }

        return points;
    }
}
