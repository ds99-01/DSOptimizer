using System;
using System.ComponentModel;
using System.IO;
using DSOptimizer.Core.Abstractions;
using DSOptimizer.Core.Errors;
using DSOptimizer.Core.Localization;
using DSOptimizer.Core.Model;

namespace DSOptimizer.Windows;

/// <summary>
/// Uruchamia pozycje <c>launch</c> poza Job Objectem skryptów, więc okno panelu przeżywa koniec Execute. Wszystko przez CreateProcess
/// bez dziedziczenia uchwytów (<c>bInheritHandles=FALSE</c>, <see cref="NativeProcessStarter"/>), więc CLI przechwytujące stdout nie czeka
/// na zamknięcie okna panelu; ShellExecute nie istnieje. Plik jest rozwiązywany wyłącznie do System32 (<c>powershell.exe</c> do
/// <see cref="PowerShellScriptRunner.PowerShellPath"/>, <c>shell:::{GUID}</c> do <see cref="ExplorerPath"/> = <c>%SystemRoot%\explorer.exe</c>),
/// nigdy względem katalogu bieżącego (pendrive) ani PATH; <c>.msc</c>/<c>.cpl</c> przez <c>mmc.exe</c>/<c>control.exe</c> z System32 – patrz
/// <see cref="LaunchTarget.ToCommand"/>. Przed startem <c>explorer.exe</c> działa strażnik powłoki (<see cref="ShellSessionGuard"/>):
/// bez Eksploratora tego samego konta w bieżącej sesji nic nie startuje. Katalog roboczy procesu to System32. Bez czekania na proces i okno.
/// </summary>
public sealed class ShellProcessLauncher : IProcessLauncher
{
    private readonly Action<string, string, string> _start;
    private readonly IExplorerProcessQuery _explorers;
    private readonly int _sessionId;
    private readonly string _ownerSid;

    public ShellProcessLauncher()
        : this(new WindowsExplorerProcessQuery())
    {
    }

    public ShellProcessLauncher(IExplorerProcessQuery explorers)
        : this(NativeProcessStarter.Start, explorers, ShellSessionCheck.CurrentSessionId(), ShellSessionCheck.CurrentSid())
    {
    }

    /// <summary>Testy podmieniają start procesu (plik, argumenty, katalog roboczy) i zapytanie o Eksploratory, żeby sprawdzić je bez uruchamiania czegokolwiek.</summary>
    internal ShellProcessLauncher(Action<string, string, string> start, IExplorerProcessQuery explorers, int sessionId, string ownerSid)
    {
        _start = start ?? throw new ArgumentNullException(nameof(start));
        _explorers = explorers ?? throw new ArgumentNullException(nameof(explorers));
        _sessionId = sessionId;
        _ownerSid = ownerSid ?? throw new ArgumentNullException(nameof(ownerSid));
    }

    /// <summary>Stała ścieżka <c>%SystemRoot%\explorer.exe</c> (nie System32).</summary>
    public static string ExplorerPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");

    public void Launch(LaunchSpec launch)
    {
        if (launch is null)
        {
            throw new ArgumentNullException(nameof(launch));
        }

        var systemDirectory = Environment.SystemDirectory;
        var command = LaunchTarget.ToCommand(launch, systemDirectory, PowerShellScriptRunner.PowerShellPath, ExplorerPath, File.Exists);
        if (command.RequiresUserShell)
        {
            ShellSessionCheck.RequireOwnExplorer(_explorers, _sessionId, _ownerSid);
        }

        try
        {
            // Uchwyty procesu nie są potrzebne (nie czekamy); explorer.exe oddający otwarcie powłoce kończy się sam.
            _start(command.FileName, command.Arguments, systemDirectory);
        }
        catch (Win32Exception ex)
        {
            throw new OperationException(OperationErrorKind.Unexpected, L.F("Nie można uruchomić {0}: {1}", launch.File, ex.Message), ex);
        }
    }
}
