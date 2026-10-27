using System;
using System.Collections.Generic;
using System.Linq;
using DSOptimizer.Core.Install;
using Xunit;

namespace DSOptimizer.Tests.Unit.Install;

public class WingetOutputTests
{
    [Fact]
    public void InstallVlc_Sample_OnlyTextLinesAreNotProgress()
    {
        var lines = InstallSamples.Lines("spike-net-install-vlc.bin");

        Assert.Contains(lines, l => l.Trim() == "-");
        Assert.Contains(lines, l => l.Trim() == "\\");
        Assert.Contains(lines, l => l.Contains('█') && l.Contains('▒'));
        Assert.Equal(
            new[]
            {
                "Found VLC media player [VideoLAN.VLC] Version 3.0.24",
                "This application is licensed to you by its owner.",
                "Microsoft is not responsible for, nor does it grant any licenses to, third-party packages.",
                "Downloading https://download.videolan.org/pub/videolan/vlc/3.0.24/win64/vlc-3.0.24-win64.exe",
                "Successfully verified installer hash",
                "Starting package install...",
                "Successfully installed",
            },
            lines.Where(l => !WingetOutput.IsProgressLine(l)));
    }

    [Fact]
    public void InstallVlc_Sample_PercentFromSizes()
    {
        var lines = InstallSamples.Lines("spike-net-install-vlc.bin");

        var percents = lines.Select(WingetOutput.TryParsePercent).Where(p => p is not null).ToList();

        Assert.Equal(new int?[] { 2, 31, 77, 100 }, percents);
    }

    [Fact]
    public void Install7Zip_Sample_PercentAndNoTextLost()
    {
        var lines = InstallSamples.Lines("spike-net-install-7zip.bin");

        Assert.Equal(new int?[] { 37, 100 }, lines.Select(WingetOutput.TryParsePercent).Where(p => p is not null));
        Assert.Equal("26.05", WingetOutput.FoundVersion(lines, "7zip.7zip"));
        Assert.Equal(7, lines.Count(l => !WingetOutput.IsProgressLine(l)));
    }

    [Fact]
    public void OfflineInstall_Sample_ErrorLinesAreNotProgress()
    {
        var lines = InstallSamples.Lines("spike-off-install-7zip.bin");

        Assert.Equal(
            new[]
            {
                "Failed when opening source(s); try the 'source reset' command if the problem persists.",
                "An unexpected error occurred while executing the command: ",
                "0x8a15000f : Data required by the source is missing",
            },
            lines.Where(l => !WingetOutput.IsProgressLine(l)));
        Assert.All(lines, l => Assert.Null(WingetOutput.StageOf(l)));
    }

    [Theory]
    [InlineData("   - ")]
    [InlineData("   \\ ")]
    [InlineData("|")]
    [InlineData("/")]
    [InlineData("  ██▒▒")]
    [InlineData("  45%")]
    [InlineData("100 %")]
    [InlineData("  14.0 MB / 44.1 MB")]
    [InlineData("512 KB / 1.00 MB")]
    [InlineData("")]
    public void IsProgressLine_SpinnerBarPercentSizes_True(string line)
    {
        Assert.True(WingetOutput.IsProgressLine(line));
    }

    [Theory]
    [InlineData("Successfully installed")]
    [InlineData("Downloading https://example.com/a%20b.exe")]
    [InlineData("Postęp 45% gotowe")]
    [InlineData("0x8a15000f : Data required by the source is missing")]
    [InlineData("Found 7-Zip [7zip.7zip] Version 26.05")]
    public void IsProgressLine_Text_False(string line)
    {
        Assert.False(WingetOutput.IsProgressLine(line));
    }

    [Theory]
    [InlineData("  ██████▒▒▒▒  14.0 MB / 44.1 MB", 31)]
    [InlineData("512 KB / 1.00 MB", 50)]
    [InlineData("  45%", 45)]
    [InlineData("100%", 100)]
    [InlineData("0 %", 0)]
    [InlineData("1.00 GB / 1.00 GB", 100)]
    [InlineData("512 B / 1 KB", 50)]
    [InlineData("  ██▒▒  12%", 12)]
    public void TryParsePercent_Values(string line, int expected)
    {
        Assert.Equal(expected, WingetOutput.TryParsePercent(line));
    }

    [Theory]
    [InlineData("150%")]
    [InlineData("2.0 MB / 1.0 MB")]
    [InlineData("1 MB / 0 MB")]
    [InlineData("   - ")]
    [InlineData("Downloading https://example.com/a50%.exe")]
    [InlineData("Successfully installed")]
    public void TryParsePercent_NoneOrOutOfRange_Null(string line)
    {
        Assert.Null(WingetOutput.TryParsePercent(line));
    }

    [Fact]
    public void StageOf_SampleMarkers()
    {
        var lines = InstallSamples.Lines("spike-net-install-vlc.bin");

        Assert.Equal(
            new InstallStage?[] { InstallStage.Downloading, InstallStage.Installing },
            lines.Select(WingetOutput.StageOf).Where(s => s is not null));
        Assert.Equal(InstallStage.Downloading, WingetOutput.StageOf("Downloading https://download.videolan.org/x.exe"));
        Assert.Equal(InstallStage.Installing, WingetOutput.StageOf("Starting package install..."));
        Assert.Null(WingetOutput.StageOf("Found VLC media player [VideoLAN.VLC] Version 3.0.24"));
        Assert.Null(WingetOutput.StageOf("Downloading"));
    }

    [Fact]
    public void FoundVersion_MatchesIdCaseInsensitive()
    {
        var lines = InstallSamples.Lines("spike-net-install-vlc.bin");

        Assert.Equal("3.0.24", WingetOutput.FoundVersion(lines, "VideoLAN.VLC"));
        Assert.Equal("3.0.24", WingetOutput.FoundVersion(lines, "videolan.vlc"));
        Assert.Null(WingetOutput.FoundVersion(lines, "VideoLAN.VLC.Nightly"));
        Assert.Null(WingetOutput.FoundVersion(InstallSamples.Lines("spike-net-install-7zip-again.bin"), "7zip.7zip"));
    }

    [Fact]
    public void FoundVersion_NameWithBrackets()
    {
        Assert.Equal("1.2", WingetOutput.FoundVersion(new[] { "Found App [beta] [Vendor.App] Version 1.2" }, "Vendor.App"));
    }

    [Fact]
    public void ParseVersion_Sample()
    {
        Assert.Equal("v1.11.510", WingetOutput.ParseVersion(InstallSamples.Lines("spike-net-version.bin")));
        Assert.Null(WingetOutput.ParseVersion(InstallSamples.Lines("spike-net-install-spotify.bin")));
        Assert.Null(WingetOutput.ParseVersion(Array.Empty<string>()));
    }

    [Fact]
    public void NullArguments_Throw()
    {
        Assert.Throws<ArgumentNullException>(() => WingetOutput.IsProgressLine(null!));
        Assert.Throws<ArgumentNullException>(() => WingetOutput.TryParsePercent(null!));
        Assert.Throws<ArgumentNullException>(() => WingetOutput.StageOf(null!));
        Assert.Throws<ArgumentNullException>(() => WingetOutput.FoundVersion(null!, "a"));
        Assert.Throws<ArgumentNullException>(() => WingetOutput.FoundVersion(Array.Empty<string>(), null!));
        Assert.Throws<ArgumentNullException>(() => WingetOutput.ParseVersion(null!));
    }

    // ---- Próbki e4c-* z pomiaru VM Etapu 4c (Home: winget v1.11.510, Pro: v1.29.380 po aktualizacji ze Sklepu) ----

    /// <summary>Procenty paska podzielone na pobrania: nowe pobranie zaczyna się od linii <c>Downloading</c>.</summary>
    private static List<List<int>> PercentsPerDownload(IEnumerable<string> lines)
    {
        var downloads = new List<List<int>>();
        foreach (var line in lines)
        {
            if (WingetOutput.StageOf(line) == InstallStage.Downloading)
            {
                downloads.Add(new List<int>());
                continue;
            }

            var percent = WingetOutput.TryParsePercent(line);
            if (percent is not null)
            {
                Assert.NotEmpty(downloads);
                downloads[downloads.Count - 1].Add(percent.Value);
            }
        }

        return downloads;
    }

    [Theory]
    [InlineData("e4c-install-success-vlc-home.bin", 1)]
    [InlineData("e4c-install-success-libreoffice-home.bin", 2)]
    [InlineData("e4c-install-locale-firefox-pl-home.bin", 1)]
    public void E4c_InstallV111_PercentsRiseMonotonicallyWithinEachDownloadUpTo100(string sample, int downloadCount)
    {
        // LibreOffice ma dwa pobrania (zależność VC++ x64, potem MSI) – pasek startuje od nowa przy każdym „Downloading”.
        var downloads = PercentsPerDownload(InstallSamples.Lines(sample));

        Assert.Equal(downloadCount, downloads.Count);
        Assert.All(downloads, percents =>
        {
            Assert.NotEmpty(percents);
            Assert.Equal(percents.OrderBy(p => p), percents);
            Assert.Equal(100, percents[percents.Count - 1]);
        });
    }

    [Fact]
    public void E4c_LibreOffice_FoundVersionOfPackageNotDependency()
    {
        var lines = InstallSamples.Lines("e4c-install-success-libreoffice-home.bin");

        Assert.Equal("26.8.1.1", WingetOutput.FoundVersion(lines, "TheDocumentFoundation.LibreOffice"));
        // Linia zależności „(1/1) Found … [Microsoft.VCRedist.2015+.x64] …” nie jest linią pakietu (research, decyzja 6).
        Assert.Null(WingetOutput.FoundVersion(lines, "Microsoft.VCRedist.2015+.x64"));
        Assert.Equal(
            new InstallStage?[] { InstallStage.Downloading, InstallStage.Installing, InstallStage.Downloading, InstallStage.Installing },
            lines.Select(WingetOutput.StageOf).Where(s => s is not null));
    }

    [Fact]
    public void E4c_FirefoxWithLocale_FoundOldVersion()
    {
        // Ruling R14: z --locale pl-PL winget wybrał 129.0.2 zamiast 157.0.1 – dlatego polecenie jest bez --locale.
        Assert.Equal("129.0.2", WingetOutput.FoundVersion(InstallSamples.Lines("e4c-install-locale-firefox-pl-home.bin"), "Mozilla.Firefox"));
    }

    [Fact]
    public void E4c_MsiVlcV129_NoProgressBarButStagesAndText()
    {
        // v1.29 przy przekierowanym wyjściu nie rysuje spinnera ani paska: etap jest, procentu nie ma (stan normalny).
        var lines = InstallSamples.Lines("e4c-install-success-msi-vlc-pro.bin");

        Assert.DoesNotContain(lines, l => WingetOutput.TryParsePercent(l) is not null);
        Assert.Equal(
            new InstallStage?[] { InstallStage.Downloading, InstallStage.Installing },
            lines.Select(WingetOutput.StageOf).Where(s => s is not null));
        Assert.Equal("3.0.24", WingetOutput.FoundVersion(lines, "VideoLAN.VLC"));
        Assert.Equal(
            new[]
            {
                "Found VLC media player [VideoLAN.VLC] Version 3.0.24",
                "This application is licensed to you by its owner.",
                "Microsoft is not responsible for, nor does it grant any licenses to, third-party packages.",
                "Downloading https://download.videolan.org/pub/videolan/vlc/3.0.24/win64/vlc-3.0.24-win64.msi",
                "Successfully verified installer hash",
                "Starting package install...",
                "Successfully installed",
            },
            lines.Where(l => !WingetOutput.IsProgressLine(l)));
    }

    [Theory]
    [InlineData("e4c-install-locale-noapplicable-home.bin", "No applicable installer found; see logs for more details.")]
    [InlineData("e4c-install-already-noupgrade-home.bin", "A package version is already installed. Installation cancelled.")]
    [InlineData("e4c-install-already-teams-pro.bin", "A package version is already installed. Installation cancelled.")]
    [InlineData("e4c-install-offline-cached-home.bin", "0x80072ee7 : unknown error")]
    [InlineData("e4c-offline-fresh-install-home.bin", "0x8a15000f : Data required by the source is missing")]
    public void E4c_FailedInstall_MessageIsTextNotProgress(string sample, string lastText)
    {
        var lines = InstallSamples.Lines(sample);
        var text = lines.Where(l => !WingetOutput.IsProgressLine(l)).ToList();

        Assert.Equal(lastText, text[text.Count - 1]);
        Assert.All(lines, l => Assert.Null(WingetOutput.StageOf(l)));
        Assert.DoesNotContain(lines, l => WingetOutput.TryParsePercent(l) is not null);
    }

    [Theory]
    [InlineData("e4c-version-home.bin", "v1.11.510")]
    [InlineData("e4c-version-1-29-pro.bin", "v1.29.380")]
    public void E4c_Version_Samples(string sample, string expected)
    {
        Assert.Equal(expected, WingetOutput.ParseVersion(InstallSamples.Lines(sample)));
    }
}
