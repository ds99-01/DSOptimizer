using System;
using System.ComponentModel;
using System.Diagnostics;
using DSOptimizer.Core.Abstractions;
using DSOptimizer.Core.Errors;
using DSOptimizer.Core.Gui;

namespace DSOptimizer.Windows;

/// <summary>
/// Otwiera adres dozwolony przez <see cref="ExternalLinkPolicy"/> – stronę autora albo adres strony „Przygotowanie” z
/// <see cref="DSOptimizer.Core.Prep.PrepLinks"/> (strona producenta, wyszukiwanie, <c>ms-settings:</c>) – jako <c>explorer.exe "&lt;adres&gt;"</c> (adres zawsze w cudzysłowie)
/// z programu podniesionego, ale bez podnoszenia przeglądarki ani Ustawień: CreateProcess stałej ścieżki
/// <see cref="ShellProcessLauncher.ExplorerPath"/> (nie ShellExecute), za strażnikiem powłoki (<see cref="ShellSessionCheck.RequireOwnExplorer"/>)
/// – wtedy explorer przekazuje adres niepodniesionej powłoce konta.
/// Pomiar na VM Home i Pro (2026-10-20) potwierdził niepodniesioną przeglądarkę (Edge: token Limited, rodzic: niepodniesiony explorer /factory),
/// a 2026-10-21 niepodniesione Ustawienia (<c>ms-settings:</c>: SystemSettings.exe Limited, integralność Medium) i kliknięte linki strony „Przygotowanie” na Pro.
/// </summary>
public sealed class ShellUrlOpener : IUrlOpener
{
    private readonly Func<ProcessStartInfo, Process?> _start;
    private readonly IExplorerProcessQuery _explorers;
    private readonly int _sessionId;
    private readonly string _ownerSid;

    public ShellUrlOpener(IExplorerProcessQuery explorers)
        : this(Process.Start, explorers, ShellSessionCheck.CurrentSessionId(), ShellSessionCheck.CurrentSid())
    {
    }

    /// <summary>Testy podmieniają start procesu i zapytanie o Eksploratory.</summary>
    internal ShellUrlOpener(Func<ProcessStartInfo, Process?> start, IExplorerProcessQuery explorers, int sessionId, string ownerSid)
    {
        _start = start ?? throw new ArgumentNullException(nameof(start));
        _explorers = explorers ?? throw new ArgumentNullException(nameof(explorers));
        _sessionId = sessionId;
        _ownerSid = ownerSid ?? throw new ArgumentNullException(nameof(ownerSid));
    }

    public void Open(string url)
    {
        if (!ExternalLinkPolicy.IsAllowed(url))
        {
            throw new OperationException(OperationErrorKind.Unexpected, "Ten adres nie jest dozwolony.");
        }

        // Cudzysłów obowiązkowy: niecytowany argument z „=” (wyszukiwanie „?q=…”) explorer bierze za przełącznik i otwiera Dokumenty
        // (2026-10-26). Dozwolony adres nie zawiera cudzysłowu: lista jest stała, a Search koduje go jako %22.
        ShellSessionCheck.RequireOwnExplorer(_explorers, _sessionId, _ownerSid);

        var info = new ProcessStartInfo(ShellProcessLauncher.ExplorerPath, $"\"{url}\"")
        {
            UseShellExecute = false,
            WorkingDirectory = Environment.SystemDirectory,
            ErrorDialog = false,
        };

        try
        {
            _start(info)?.Dispose();
        }
        catch (Win32Exception ex)
        {
            throw new OperationException(OperationErrorKind.Unexpected, $"Nie można otworzyć strony: {ex.Message}", ex);
        }
    }
}
