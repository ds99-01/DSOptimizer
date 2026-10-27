using System;
using System.Globalization;
using System.IO;
using DSOptimizer.Tests.Windows.Support;
using DSOptimizer.Windows;
using Xunit;

namespace DSOptimizer.Tests.Windows;

[Trait("Category", "Windows")]
public class FileLogTests
{
    private static string FileName(FixedClock clock)
        => string.Format(CultureInfo.InvariantCulture, "dsoptimizer-{0:yyyyMMdd}.log", clock.UtcNow.ToLocalTime());

    private static InvalidOperationException ThrownException()
    {
        try
        {
            throw new InvalidOperationException("boom");
        }
        catch (InvalidOperationException ex)
        {
            return ex;
        }
    }

    [Fact]
    public void Info_WritesFormattedLineToPrimaryAndMirror()
    {
        using var temp = new TempDirectory();
        var clock = new FixedClock();
        var log = new FileLog(temp.AppRoot, temp.Sub("mirror"), clock);

        log.Info("Test", "wiadomość ąę");

        foreach (var directory in new[] { temp.AppRoot.LogsDirectory, temp.Sub("mirror") })
        {
            var text = File.ReadAllText(Path.Combine(directory, FileName(clock)));
            Assert.Contains("[INFO] [Test] wiadomość ąę", text, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Info_AsFirstComponent_CreatesTrustedParentDirectory()
    {
        using var temp = new TempDirectory();
        var log = new FileLog(temp.AppRoot, null, new FixedClock());

        log.Info("Test", "pierwszy");

        Assert.True(SecureDirectory.IsTrustedDirectory(temp.Sub("app")));
        Assert.True(SecureDirectory.IsTrustedDirectory(temp.AppRoot.LogsDirectory));
    }

    [Fact]
    public void Fail_AppendsExceptionDetails()
    {
        using var temp = new TempDirectory();
        var clock = new FixedClock();
        var log = new FileLog(temp.AppRoot, null, clock);

        log.Fail("Test", "coś padło", new InvalidOperationException("boom"));

        var text = File.ReadAllText(Path.Combine(temp.AppRoot.LogsDirectory, FileName(clock)));
        Assert.Contains("[ERROR] [Test] coś padło", text, StringComparison.Ordinal);
        Assert.Contains("InvalidOperationException", text, StringComparison.Ordinal);
        Assert.Contains("boom", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Fail_WritesStackTraceOfThrownException()
    {
        using var temp = new TempDirectory();
        var clock = new FixedClock();
        var log = new FileLog(temp.AppRoot, null, clock);

        log.Fail("Test", "coś padło", ThrownException());

        var text = File.ReadAllText(Path.Combine(temp.AppRoot.LogsDirectory, FileName(clock)));
        Assert.Contains(nameof(ThrownException), text, StringComparison.Ordinal);
    }

    [Fact]
    public void BrokenMirror_IsDisabledButPrimaryKeepsWorking()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(temp.Sub("mirror"), "plik blokujący katalog");
        var clock = new FixedClock();
        var log = new FileLog(temp.AppRoot, temp.Sub("mirror"), clock);

        log.Warn("Test", "pierwsza");
        log.Warn("Test", "druga");

        var lines = File.ReadAllLines(Path.Combine(temp.AppRoot.LogsDirectory, FileName(clock)));
        Assert.Equal(2, lines.Length);
    }

    [Fact]
    public void Log_WhenRootIsAFile_NeverThrows()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(temp.Sub("app"), "plik blokujący korzeń");
        var log = new FileLog(temp.AppRoot, null, new FixedClock());

        var exception = Record.Exception(() =>
        {
            log.Info("Test", "info");
            log.Warn("Test", "warn");
            log.Fail("Test", "fail", new InvalidOperationException("boom"));
        });

        Assert.Null(exception);
    }

    [Fact]
    public void Info_WhenPrimaryCannotBeSecured_DoesNotWriteIntoUntrustedDirectory()
    {
        using var temp = new TempDirectory();
        SecureDirectory.Ensure(temp.Sub("app"));
        var primary = temp.AppRoot.LogsDirectory;
        Directory.CreateDirectory(primary);
        ForeignAcl.Apply(primary);
        var clock = new FixedClock();

        // Otwarty plik w środku blokuje odłożenie katalogu, więc EnsureLeaf kończy się błędem (fail-safe).
        using (new FileStream(Path.Combine(primary, "blokada.txt"), FileMode.Create, FileAccess.Write, FileShare.None))
        {
            Assert.ThrowsAny<IOException>(() => temp.AppRoot.EnsureLeaf(primary));
            new FileLog(temp.AppRoot, null, clock).Info("Test", "nie może trafić do niezaufanego katalogu");
        }

        Assert.False(File.Exists(Path.Combine(primary, FileName(clock))));
    }
}
