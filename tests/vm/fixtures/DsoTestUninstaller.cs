// Instrukcja uruchomienia:
//     Nie uruchamiaj na hoście. Budowany i instalowany w VM przez Install-DsoTestProgram.ps1 (csc.exe z .NET Framework 4, C# 5):
//         %WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe /nologo /out:DsoTestUninstaller.exe DsoTestUninstaller.cs
//     Wywołanie (z wpisu Uninstall): DsoTestUninstaller.exe <tryb> <KluczUninstall> [<plik-znacznika>]
// Opis działania:
//     Deinstalator testowy programu Win32 dla testów VM usuwania aplikacji (Etap 4a, Task 7). Usuwa wyłącznie klucz
//     HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\<Klucz> (widok 64-bit), gdzie <Klucz> = DsoTest[A-Za-z0-9]+.
//     Tryby:
//       quiet          - usuwa wpis i kończy się kodem 0;
//       exit3010       - usuwa wpis i kończy się kodem 3010 (restart wymagany);
//       deferred       - jak Inno Setup: kopiuje się do %TEMP%, startuje kopię i od razu kończy się kodem 0;
//                        kopia (deferred-child) usuwa wpis po 5 s, a potem czeka (do 120 s), aż nie będzie żadnego procesu
//                        DSOptimizer, i zapisuje <plik-znacznika> z "dsoptimizer-gone=True" - dowód, że proces potomny
//                        deinstalatora przeżył zakończenie DSOptimizera (deinstalator nie siedzi w jego Job Objecie).
//     Każde uruchomienie dopisuje linię do ran.log obok exe (dowód, czy deinstalator w ogóle wystartował).
using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;
using Microsoft.Win32;

internal static class DsoTestUninstaller
{
    private const string UninstallRoot = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\";
    private const int InvalidArguments = 87;

    private static int Main(string[] args)
    {
        if (args.Length < 2 || !Regex.IsMatch(args[1], "^DsoTest[A-Za-z0-9]+$"))
        {
            return InvalidArguments;
        }

        string mode = args[0];
        string key = args[1];
        string marker = args.Length > 2 ? args[2] : null;
        Log(mode + " " + key);
        switch (mode)
        {
            case "quiet":
                DeleteKey(key);
                return 0;
            case "exit3010":
                DeleteKey(key);
                return 3010;
            case "deferred":
                StartDeferredCopy(key, marker);
                return 0;
            case "deferred-child":
                RunDeferredChild(key, marker);
                return 0;
            default:
                return InvalidArguments;
        }
    }

    private static void DeleteKey(string key)
    {
        using (RegistryKey baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
        {
            baseKey.DeleteSubKeyTree(UninstallRoot + key, false);
        }
    }

    private static void StartDeferredCopy(string key, string marker)
    {
        string self = Assembly.GetExecutingAssembly().Location;
        string copy = Path.Combine(Path.GetTempPath(), "DsoTestUninstaller-" + key + ".exe");
        File.Copy(self, copy, true);
        ProcessStartInfo info = new ProcessStartInfo(copy, "deferred-child " + key + (marker == null ? string.Empty : " " + marker));
        info.UseShellExecute = false;
        info.CreateNoWindow = true;
        using (Process.Start(info))
        {
        }
    }

    private static void RunDeferredChild(string key, string marker)
    {
        Thread.Sleep(TimeSpan.FromSeconds(5));
        DeleteKey(key);
        Log("deferred-child: wpis usuniety " + key);
        if (marker == null)
        {
            return;
        }

        Stopwatch watch = Stopwatch.StartNew();
        bool gone = false;
        while (watch.Elapsed < TimeSpan.FromSeconds(120))
        {
            if (Process.GetProcessesByName("DSOptimizer").Length == 0)
            {
                gone = true;
                break;
            }

            Thread.Sleep(500);
        }

        File.WriteAllText(marker, "dsoptimizer-gone=" + gone + " " + DateTime.UtcNow.ToString("o"));
    }

    private static void Log(string line)
    {
        try
        {
            string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ran.log");
            File.AppendAllText(path, DateTime.UtcNow.ToString("o") + " " + line + Environment.NewLine);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
