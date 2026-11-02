using System;

namespace DSOptimizer.Core.Autostart;

/// <summary>
/// Miejsca autostartu jednego konta: gałęzie rejestru przez SID (nigdy HKCU) i foldery Autostart w postaci nierozwiniętej
/// (rozwija je <see cref="UserEnvironment"/> zmiennymi konta).
/// </summary>
public sealed record AutostartLocations(string UserRoot, string MachineRoot, string UserClassesRoot, string UserStartupFolder, string CommonStartupFolder)
{
    private const string UserCurrentVersion = @"\Software\Microsoft\Windows\CurrentVersion";
    private const string MachineCurrentVersion = @"\SOFTWARE\Microsoft\Windows\CurrentVersion";

    public string VolatileEnvironmentKey => UserRoot + @"\Volatile Environment";

    public string UserEnvironmentKey => UserRoot + @"\Environment";

    public static AutostartLocations ForUser(string sid)
    {
        if (string.IsNullOrEmpty(sid) || sid.IndexOf('\\') >= 0)
        {
            throw new ArgumentException("Nieprawidłowy SID: " + sid, nameof(sid));
        }

        return new AutostartLocations(
            @"HKU\" + sid,
            "HKLM",
            @"HKU\" + sid + "_Classes",
            @"%APPDATA%\Microsoft\Windows\Start Menu\Programs\Startup",
            @"%ProgramData%\Microsoft\Windows\Start Menu\Programs\StartUp");
    }

    /// <summary>Klucz z wpisami Run danego źródła; foldery i Sklep = <see cref="ArgumentException"/>.</summary>
    public string RunKey(AutostartSource source) => source switch
    {
        AutostartSource.RunUser => UserRoot + UserCurrentVersion + @"\Run",
        AutostartSource.RunMachine => MachineRoot + MachineCurrentVersion + @"\Run",
        AutostartSource.Run32 => MachineRoot + @"\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Run",
        _ => throw new ArgumentException("Źródło nie ma klucza Run: " + source, nameof(source)),
    };

    /// <summary>Klucz <c>StartupApproved</c> z przełącznikiem źródła; <see cref="AutostartSource.StartupTask"/> = <see cref="ArgumentException"/>.</summary>
    public string StartupApprovedKey(AutostartSource source) => source switch
    {
        AutostartSource.RunUser => UserRoot + UserCurrentVersion + @"\Explorer\StartupApproved\Run",
        AutostartSource.FolderUser => UserRoot + UserCurrentVersion + @"\Explorer\StartupApproved\StartupFolder",
        AutostartSource.RunMachine => MachineRoot + MachineCurrentVersion + @"\Explorer\StartupApproved\Run",
        AutostartSource.Run32 => MachineRoot + MachineCurrentVersion + @"\Explorer\StartupApproved\Run32",
        AutostartSource.FolderCommon => MachineRoot + MachineCurrentVersion + @"\Explorer\StartupApproved\StartupFolder",
        _ => throw new ArgumentException("Źródło nie ma przełącznika StartupApproved: " + source, nameof(source)),
    };

    /// <summary>Klucz stanu zadania startowego aplikacji ze Sklepu (części bez <c>\</c>, niepuste).</summary>
    public string StartupTaskKey(string family, string taskId)
    {
        RequireSegment(family, nameof(family));
        RequireSegment(taskId, nameof(taskId));
        return UserClassesRoot + @"\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppModel\SystemAppData\" + family + @"\" + taskId;
    }

    private static void RequireSegment(string value, string name)
    {
        if (string.IsNullOrEmpty(value) || value.IndexOf('\\') >= 0)
        {
            throw new ArgumentException("Nieprawidłowa część klucza zadania startowego: " + value, name);
        }
    }
}
