// Sonda autostartu dla testów VM (Flow Autostart). Budowana w VM przez csc.exe z .NET Framework 4 (C# 5), bez sieci:
//   csc.exe /nologo /target:winexe /out:C:\dsotest\DsoAutostartProbe.exe DsoAutostartProbe.cs
// Install-DsoAutostartProbe.ps1 kopiuje exe pod nazwą sondy (C:\dsotest\<Nazwa>.exe). Przy starcie sonda dopisuje linię
// <nazwa exe>|<czas UTC ISO 8601>|<konto>|<sesja> do C:\dsotest\probe.log, zużywa ok. 1,5 s CPU, zapisuje i czyta 16 MB w %TEMP%
// (widoczne w pomiarze startu Windows), potem śpi godzinę, żeby proces był widoczny po zalogowaniu.
using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;

[assembly: AssemblyTitle("DsoAutostartProbe")]
[assembly: AssemblyCompany("DsoTest Publisher")]
[assembly: AssemblyProduct("DsoAutostartProbe")]
[assembly: AssemblyFileVersion("1.0.0.0")]

internal static class DsoAutostartProbe
{
    private const string LogPath = @"C:\dsotest\probe.log";

    private static int Main()
    {
        string name = Path.GetFileNameWithoutExtension(Process.GetCurrentProcess().MainModule.FileName);
        string line = name + "|" + DateTime.UtcNow.ToString("o") + "|" + Environment.UserName + "|" + Process.GetCurrentProcess().SessionId + Environment.NewLine;
        AppendLine(line);
        BurnCpu(1500);
        TouchDisk(name);
        Thread.Sleep(3600000);
        return 0;
    }

    // Kilka sond startuje naraz: wyłączny zapis z ponowieniem, żeby żadna linia nie zginęła ani się nie nadpisała.
    private static void AppendLine(string line)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(line);
        for (int attempt = 0; attempt < 200; attempt++)
        {
            try
            {
                using (FileStream stream = new FileStream(LogPath, FileMode.Append, FileAccess.Write, FileShare.None))
                {
                    stream.Write(bytes, 0, bytes.Length);
                    stream.Flush(true);
                }
                return;
            }
            catch (IOException)
            {
                Thread.Sleep(50);
            }
        }
    }

    private static void BurnCpu(int milliseconds)
    {
        Stopwatch watch = Stopwatch.StartNew();
        double sink = 0;
        while (watch.ElapsedMilliseconds < milliseconds)
        {
            sink += Math.Sqrt(watch.ElapsedTicks);
        }

        GC.KeepAlive(sink);
    }

    private static void TouchDisk(string name)
    {
        string path = Path.Combine(Path.GetTempPath(), name + ".bin");
        byte[] buffer = new byte[1024 * 1024];
        using (FileStream stream = File.Create(path))
        {
            for (int i = 0; i < 16; i++)
            {
                stream.Write(buffer, 0, buffer.Length);
            }

            stream.Flush(true);
        }

        using (FileStream stream = File.OpenRead(path))
        {
            while (stream.Read(buffer, 0, buffer.Length) > 0)
            {
            }
        }

        File.Delete(path);
    }
}
