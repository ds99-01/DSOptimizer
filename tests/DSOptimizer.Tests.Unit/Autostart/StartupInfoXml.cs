using System.Text;

namespace DSOptimizer.Tests.Unit.Autostart;

/// <summary>Proces w pliku pomiaru Windows (schemat z pomiaru na VM 26H2).</summary>
internal sealed record XmlProcess(string Name, string ParentName, long Cpu, long Disk, string StartTime = "2026/11/02:08:43:45.0360860", string? CommandLine = null);

/// <summary>Treść pliku <c>StartupInfo</c> w schemacie zmierzonym na VM (<c>StartupData</c> → <c>Process</c>).</summary>
internal static class StartupInfoXml
{
    public static string Of(params XmlProcess[] processes)
    {
        var builder = new StringBuilder();
        builder.Append("<?xml version=\"1.0\" encoding=\"UTF-16\"?>\r\n<StartupData IntervalStartMs=\"3460\" IntervalEndMs=\"93460\">\r\n");
        var pid = 1000;
        foreach (var process in processes)
        {
            pid += 4;
            builder.Append("  <Process Name=\"").Append(process.Name).Append("\" PID=\"").Append(pid).Append("\" StartedInTraceSec=\"12.809\">\r\n")
                .Append("    <StartTime>").Append(process.StartTime).Append("</StartTime>\r\n")
                .Append("    <CommandLine><![CDATA[").Append(process.CommandLine ?? "\"" + process.Name + "\" ").Append("]]></CommandLine>\r\n")
                .Append("    <DiskUsage Units=\"bytes\">").Append(process.Disk).Append("</DiskUsage>\r\n")
                .Append("    <CpuUsage Units=\"us\">").Append(process.Cpu).Append("</CpuUsage>\r\n")
                .Append("    <ParentPID>4321</ParentPID>\r\n")
                .Append("    <ParentStartTime>2026/11/02:08:43:30.0000000</ParentStartTime>\r\n")
                .Append("    <ParentName>").Append(process.ParentName).Append("</ParentName>\r\n")
                .Append("  </Process>\r\n");
        }

        builder.Append("  <ReadAheadAnalysisTime>0</ReadAheadAnalysisTime>\r\n  <RurLegacyResourceAttribution>0</RurLegacyResourceAttribution>\r\n</StartupData>\r\n");
        return builder.ToString();
    }
}
