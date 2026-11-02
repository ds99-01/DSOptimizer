using System;

namespace DSOptimizer.Core.Autostart;

/// <summary>Miejsce, z którego Windows uruchamia pozycję przy logowaniu.</summary>
public enum AutostartSource
{
    /// <summary><c>HKU\&lt;SID&gt;\…\CurrentVersion\Run</c>.</summary>
    RunUser,

    /// <summary><c>HKLM\SOFTWARE\…\CurrentVersion\Run</c>.</summary>
    RunMachine,

    /// <summary><c>HKLM\SOFTWARE\WOW6432Node\…\CurrentVersion\Run</c>.</summary>
    Run32,

    /// <summary>Folder Autostart konta.</summary>
    FolderUser,

    /// <summary>Wspólny folder Autostart.</summary>
    FolderCommon,

    /// <summary>Zadanie startowe aplikacji ze Sklepu (<c>windows.startupTask</c>).</summary>
    StartupTask,
}

/// <summary>Zakres pozycji: konto zalogowanego użytkownika, cały komputer albo aplikacja ze Sklepu (też konto).</summary>
public enum AutostartScope
{
    User,
    Machine,
    Store,
}

/// <summary>Stan przełącznika pozycji.</summary>
public enum AutostartState
{
    Enabled,
    Disabled,
    Unknown,
}

public static class AutostartSources
{
    public static AutostartScope ScopeOf(AutostartSource source) => source switch
    {
        AutostartSource.RunUser or AutostartSource.FolderUser => AutostartScope.User,
        AutostartSource.RunMachine or AutostartSource.Run32 or AutostartSource.FolderCommon => AutostartScope.Machine,
        AutostartSource.StartupTask => AutostartScope.Store,
        _ => throw new ArgumentOutOfRangeException(nameof(source), source, null),
    };
}
