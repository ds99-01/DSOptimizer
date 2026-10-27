using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using DSOptimizer.Tests.Windows.Support;
using Newtonsoft.Json.Linq;
using Xunit;

namespace DSOptimizer.Tests.Windows;

/// <summary>
/// Wariant jednego exe (Etap 5, Costura): zbudowany plik wydania uruchomiony sam w pustym folderze z <c>--list-install</c>
/// musi zwrócić kod 0 i niepustą listę – dowód, że osadzone <c>apps.json</c>, Newtonsoft.Json, Core i Windows ładują się
/// z jednego exe, bez bibliotek obok. Ścieżkę pliku podaje <see cref="PathVariable"/> (np. wynik
/// <c>tools\release\Build-Release.ps1 -Variant SingleExe</c>); bez niej test jest pomijany z powodem.
/// <c>--list-install</c> czyta tylko wbudowaną listę i listę zainstalowanych programów; jedyny proces, który mógłby
/// uruchomić, to <c>winget --version</c>, a bezpiecznik <c>DSO_BLOCK_REAL_WINGET=1</c> (przekazany jawnie procesowi) blokuje go
/// przed startem. Okna GUI ten test nie otwiera.
/// </summary>
[Trait("Category", "Windows")]
public class SingleExeResourcesTests
{
    public const string PathVariable = "DSO_SINGLE_EXE_PATH";

    private static readonly TimeSpan ExitTimeout = TimeSpan.FromMinutes(2);

    [SingleExeFact]
    public void ListInstall_FromLoneSingleExe_ReturnsZeroAndNonEmptyList()
    {
        var source = Environment.GetEnvironmentVariable(PathVariable)!;
        Assert.True(File.Exists(source), $"{PathVariable} wskazuje nieistniejący plik: {source}");
        using var temp = new TempDirectory();
        var lone = temp.Sub("solo");
        Directory.CreateDirectory(lone);
        var exe = Path.Combine(lone, Path.GetFileName(source));
        File.Copy(source, exe);
        var output = temp.Sub("lista.json");

        var start = new ProcessStartInfo(exe, $"--list-install --out \"{output}\"")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = lone,
        };
        start.EnvironmentVariables[TestGuards.BlockRealWinget] = "1";
        start.EnvironmentVariables[TestGuards.BlockRealNetwork] = "1";
        start.EnvironmentVariables[TestGuards.BlockRealMsiexec] = "1";
        using var process = Process.Start(start)!;
        if (!process.WaitForExit((int)ExitTimeout.TotalMilliseconds))
        {
            process.Kill();
            Assert.Fail($"Jeden exe nie zakończył --list-install w {ExitTimeout.TotalSeconds} s.");
        }

        Assert.True(File.Exists(output), $"Brak raportu {output}, kod wyjścia {process.ExitCode}.");
        var report = JObject.Parse(File.ReadAllText(output));
        Assert.True(process.ExitCode == 0, $"Kod {process.ExitCode}: " + string.Join(" | ", report["errors"]?.Children().Select(e => (string)e!) ?? Array.Empty<string>()));
        Assert.Equal("ListInstall", (string)report["command"]!);
        var rows = report["installRows"]!.Children().ToList();
        Assert.NotEmpty(rows);
        Assert.Contains(rows, row => (string)row["id"]! == "install.rustdesk");
    }
}

/// <summary>Fakt pomijany z powodem, gdy nie ustawiono <see cref="SingleExeResourcesTests.PathVariable"/> (artefakt SingleExe niezbudowany).</summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class SingleExeFactAttribute : FactAttribute
{
    public SingleExeFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(SingleExeResourcesTests.PathVariable)))
        {
            Skip = $"Artefakt SingleExe nie został zbudowany: ustaw {SingleExeResourcesTests.PathVariable} na plik z tools\\release\\Build-Release.ps1 -Variant SingleExe.";
        }
    }
}
