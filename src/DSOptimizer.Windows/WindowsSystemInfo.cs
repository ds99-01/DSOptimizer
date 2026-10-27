using System;
using System.Globalization;
using DSOptimizer.Core.Abstractions;

namespace DSOptimizer.Windows;

/// <summary>Build/UBR/edycja z HKLM\...\CurrentVersion (Environment.OSVersion kłamie bez manifestu).</summary>
public sealed class WindowsSystemInfo : ISystemInfo
{
    private const string CurrentVersionPath = @"HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion";

    public WindowsSystemInfo(IRegistry registry)
    {
        if (registry is null)
        {
            throw new ArgumentNullException(nameof(registry));
        }

        Build = int.TryParse(ReadString(registry, "CurrentBuild"), NumberStyles.None, CultureInfo.InvariantCulture, out var build) ? build : 0;
        Revision = registry.Read(CurrentVersionPath, "UBR") is { Status: ReadStatus.Found, Value.Data: uint ubr } ? (int)ubr : 0;
        Edition = ReadString(registry, "EditionID") ?? "unknown";
        MachineName = Environment.MachineName;
    }

    public int Build { get; }

    public int Revision { get; }

    public string Edition { get; }

    public string MachineName { get; }

    private static string? ReadString(IRegistry registry, string name)
        => registry.Read(CurrentVersionPath, name) is { Status: ReadStatus.Found, Value.Data: string text } ? text : null;
}
