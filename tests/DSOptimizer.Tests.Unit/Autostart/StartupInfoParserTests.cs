using System;
using System.Xml;
using DSOptimizer.Core.Autostart;
using Xunit;

namespace DSOptimizer.Tests.Unit.Autostart;

public class StartupInfoParserTests
{
    [Fact]
    public void Parse_SchemaFromMeasurement()
    {
        const string Xml = "\uFEFF<?xml version=\"1.0\" encoding=\"UTF-16\"?>\r\n"
            + "<StartupData IntervalStartMs=\"3460\" IntervalEndMs=\"93460\">\r\n"
            + "  <Process Name=\"C:\\dsotest\\DsoRunUser.exe\" PID=\"8216\" StartedInTraceSec=\"12.809\">\r\n"
            + "    <StartTime>2026/11/02:08:43:45.0360860</StartTime>\r\n"
            + "    <CommandLine><![CDATA[\"C:\\dsotest\\DsoRunUser.exe\" ]]></CommandLine>\r\n"
            + "    <DiskUsage Units=\"bytes\">3002368</DiskUsage>\r\n"
            + "    <CpuUsage Units=\"us\">1534474</CpuUsage>\r\n"
            + "    <ParentPID>5012</ParentPID>\r\n"
            + "    <ParentStartTime>2026/11/02:08:43:30.1000000</ParentStartTime>\r\n"
            + "    <ParentName>explorer.exe</ParentName>\r\n"
            + "  </Process>\r\n"
            + "  <ReadAheadAnalysisTime>0</ReadAheadAnalysisTime>\r\n"
            + "  <RurLegacyResourceAttribution>0</RurLegacyResourceAttribution>\r\n"
            + "</StartupData>\r\n";

        var process = Assert.Single(StartupInfoParser.Parse(Xml));

        Assert.Equal(
            new StartupProcess(
                @"C:\dsotest\DsoRunUser.exe",
                "\"C:\\dsotest\\DsoRunUser.exe\" ",
                1534474,
                3002368,
                "explorer.exe",
                new DateTime(2026, 11, 2, 8, 43, 45).AddTicks(360860)),
            process);
    }

    [Fact]
    public void Parse_SeveralProcesses_InFileOrder()
    {
        var processes = StartupInfoParser.Parse(StartupInfoXml.Of(
            new XmlProcess(@"C:\Windows\System32\SecurityHealthSystray.exe", "explorer.exe", 19_000, 550_912),
            new XmlProcess(@"C:\Program Files\WindowsApps\Microsoft.WindowsTerminal_1.24.11911.0_x64__8wekyb3d8bbwe\OpenConsole.exe", "WindowsTerminal.exe", 1, 2)));

        Assert.Equal(2, processes.Count);
        Assert.Equal(@"C:\Windows\System32\SecurityHealthSystray.exe", processes[0].Name);
        Assert.Equal("WindowsTerminal.exe", processes[1].ParentName);
    }

    [Fact]
    public void Parse_DoctypeRejected()
    {
        const string Xml = "<?xml version=\"1.0\"?><!DOCTYPE StartupData [<!ENTITY x \"C:\\a.exe\">]>"
            + "<StartupData><Process Name=\"&x;\"><CpuUsage>1</CpuUsage></Process></StartupData>";

        Assert.Throws<XmlException>(() => StartupInfoParser.Parse(Xml));
    }

    [Fact]
    public void Parse_MissingCpu_Null()
    {
        const string Xml = "<StartupData><Process Name=\"C:\\a.exe\"><DiskUsage Units=\"bytes\">10</DiskUsage>"
            + "<ParentName>explorer.exe</ParentName></Process></StartupData>";

        var process = Assert.Single(StartupInfoParser.Parse(Xml));

        Assert.Equal(new StartupProcess(@"C:\a.exe", null, null, 10, "explorer.exe", null), process);
    }

    [Fact]
    public void Parse_UnreadableNumbersAndTime_Null()
    {
        const string Xml = "<StartupData><Process Name=\"C:\\a.exe\"><StartTime>wczoraj</StartTime><DiskUsage>-</DiskUsage>"
            + "<CpuUsage>1.5</CpuUsage></Process></StartupData>";

        var process = Assert.Single(StartupInfoParser.Parse(Xml));

        Assert.Null(process.StartTime);
        Assert.Null(process.DiskBytes);
        Assert.Null(process.CpuMicroseconds);
    }

    [Fact]
    public void Parse_ProcessWithoutName_Skipped()
    {
        const string Xml = "<StartupData><Process><CpuUsage>1</CpuUsage></Process><Process Name=\" \"/><Process Name=\"C:\\b.exe\"/></StartupData>";

        Assert.Equal(@"C:\b.exe", Assert.Single(StartupInfoParser.Parse(Xml)).Name);
    }

    [Fact]
    public void Parse_NoProcesses_Empty()
        => Assert.Empty(StartupInfoParser.Parse("<StartupData IntervalStartMs=\"1\" IntervalEndMs=\"2\"/>"));

    [Theory]
    [InlineData("")]
    [InlineData("to nie jest XML")]
    [InlineData("<StartupData><Process Name=\"C:\\a.exe\">")]
    [InlineData("<Other><Process Name=\"C:\\a.exe\"/></Other>")]
    public void Parse_NotStartupData_XmlException(string xml)
        => Assert.Throws<XmlException>(() => StartupInfoParser.Parse(xml));

    [Fact]
    public void Parse_Null_Throws()
        => Assert.Throws<ArgumentNullException>(() => StartupInfoParser.Parse(null!));
}
