using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using DSOptimizer.Core;
using DSOptimizer.Core.Abstractions;
using DSOptimizer.Core.Errors;
using DSOptimizer.Core.Model;
using DSOptimizer.Core.Prep;
using DSOptimizer.Windows;
using Xunit;

namespace DSOptimizer.Tests.Windows;

/// <summary>
/// Otwieranie strony autora bez uruchamiania czegokolwiek: start procesu podmieniony, sprawdzamy ProcessStartInfo
/// (<c>%SystemRoot%\explorer.exe https://github.com/ds99-01</c>) i to, że odmowy nie startują nic.
/// </summary>
[Trait("Category", "Windows")]
public class ShellUrlOpenerTests
{
    private const int Session = 1;
    private const string Me = "S-1-5-21-1-2-3-1000";

    private readonly List<ProcessStartInfo> _started = new();
    private readonly FakeExplorerQuery _explorers = new();

    public ShellUrlOpenerTests()
    {
        _explorers.Processes.Add(new ExplorerProcessInfo(Session, Me, @"C:\Windows\Explorer.EXE"));
    }

    private ShellUrlOpener Opener(Exception? failure = null)
        => new(
            info =>
            {
                _started.Add(info);
                if (failure is not null)
                {
                    throw failure;
                }

                return null;
            },
            _explorers,
            Session,
            Me);

    private sealed class FakeExplorerQuery : IExplorerProcessQuery
    {
        public List<ExplorerProcessInfo> Processes { get; } = new();

        public Exception? Failure { get; set; }

        public int Calls { get; private set; }

        public IReadOnlyList<ExplorerProcessInfo> GetExplorerProcesses()
        {
            Calls++;
            if (Failure is not null)
            {
                throw Failure;
            }

            return Processes;
        }
    }

    [Fact]
    public void Open_AllowedUrl_WithOwnExplorer_StartsExplorer()
    {
        Opener().Open(ProductInfo.AuthorUrl);

        var info = Assert.Single(_started);
        Assert.Equal(ShellProcessLauncher.ExplorerPath, info.FileName);
        Assert.Equal("\"https://github.com/ds99-01\"", info.Arguments);
        Assert.False(info.UseShellExecute);
        Assert.False(info.ErrorDialog);
        Assert.Equal(Environment.SystemDirectory, info.WorkingDirectory);
    }

    [Fact]
    public void Open_PrepVendorUrl_WithOwnExplorer_StartsExplorerWithExactArgument()
    {
        const string url = "https://www.nvidia.com/pl-pl/drivers/";

        Opener().Open(url);

        var info = Assert.Single(_started);
        Assert.Equal(ShellProcessLauncher.ExplorerPath, info.FileName);
        Assert.Equal($"\"{url}\"", info.Arguments);
    }

    [Fact]
    public void Open_SearchUrl_StartsExplorerWithEncodedArgument()
    {
        var url = PrepLinks.Search(@"PCI\VEN_8086&DEV_A0E8");

        Opener().Open(url);

        Assert.Equal($"\"{url}\"", Assert.Single(_started).Arguments);
    }

    // explorer.exe bierze niecytowany argument z „=” za przełącznik i otwiera Dokumenty zamiast przeglądarki
    // (odtworzone 2026-10-26 na 24H2: „…/search?q=support” → okno „Dokumenty”, ten sam adres w cudzysłowie → przeglądarka).
    [Fact]
    public void Open_SupportSearchUrl_QuotesArgumentContainingEqualsSign()
    {
        var url = PrepLinks.Search("ASUSTeK COMPUTER INC. ROG STRIX X670E-F GAMING WIFI support");

        Opener().Open(url);

        var arguments = Assert.Single(_started).Arguments;
        Assert.Contains("?q=", arguments);
        Assert.StartsWith("\"https://www.google.com/search?q=", arguments);
        Assert.EndsWith("support\"", arguments);
    }

    [Fact]
    public void Open_SettingsUri_FollowsSettingsOpenable()
    {
        if (PrepLinks.SettingsOpenable)
        {
            Opener().Open(PrepLinks.WindowsUpdate);
            Assert.Equal($"\"{PrepLinks.WindowsUpdate}\"", Assert.Single(_started).Arguments);
        }
        else
        {
            Assert.Throws<OperationException>(() => Opener().Open(PrepLinks.WindowsUpdate));
            Assert.Empty(_started);
        }

        Assert.Throws<OperationException>(() => Opener().Open("ms-settings:display"));
        Assert.Equal(PrepLinks.SettingsOpenable ? 1 : 0, _started.Count);
    }

    [Fact]
    public void Open_OtherUrl_RefusesWithoutStarting()
    {
        var ex = Assert.Throws<OperationException>(() => Opener().Open("https://example.com"));

        Assert.Equal("Ten adres nie jest dozwolony.", ex.Message);
        Assert.Empty(_started);
        Assert.Equal(0, _explorers.Calls);
    }

    [Fact]
    public void Open_NoOwnExplorer_RefusesWithoutStarting()
    {
        _explorers.Processes.Clear();

        var ex = Assert.Throws<OperationException>(() => Opener().Open(ProductInfo.AuthorUrl));

        Assert.Equal(ShellSessionGuard.RefusalMessage, ex.Message);
        Assert.Empty(_started);
    }

    [Fact]
    public void Open_ExplorerQueryFails_RefusesWithoutStarting()
    {
        _explorers.Failure = new InvalidOperationException("brak dostępu");

        var ex = Assert.Throws<OperationException>(() => Opener().Open(ProductInfo.AuthorUrl));

        Assert.Contains("brak dostępu", ex.Message, StringComparison.Ordinal);
        Assert.Empty(_started);
    }

    [Fact]
    public void Open_Win32Error_BecomesOperationException()
    {
        var ex = Assert.Throws<OperationException>(() => Opener(new Win32Exception("odmowa")).Open(ProductInfo.AuthorUrl));

        Assert.Equal("Nie można otworzyć strony: odmowa", ex.Message);
        Assert.IsType<Win32Exception>(ex.InnerException);
    }
}
