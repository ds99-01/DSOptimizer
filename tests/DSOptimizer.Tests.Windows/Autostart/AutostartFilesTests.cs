using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using DSOptimizer.Core.Autostart;
using DSOptimizer.Core.Errors;
using DSOptimizer.Tests.Windows.Support;
using DSOptimizer.Windows.Autostart;
using DSOptimizer.Windows.Native;
using Xunit;

namespace DSOptimizer.Tests.Windows.Autostart;

/// <summary>
/// Pliki autostartu na prawdziwym systemie: wyłącznie katalog testu w <c>%TEMP%\dso-tests\&lt;guid&gt;</c>; skróty zapisywane przez COM
/// w procesie testu; z plików systemu tylko odczyt wersji <c>notepad.exe</c>. Prawdziwe foldery Autostart nie są czytane.
/// </summary>
[Trait("Category", "Windows")]
public class AutostartFilesTests
{
    private readonly AutostartFiles _files = new();

    [Fact]
    public void ListFolder_FilesOnly()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(temp.Sub("a.lnk"), "x");
        File.WriteAllText(temp.Sub("desktop.ini"), "x");
        File.SetAttributes(temp.Sub("desktop.ini"), FileAttributes.Hidden | FileAttributes.System);
        Directory.CreateDirectory(temp.Sub("podfolder"));
        File.WriteAllText(Path.Combine(temp.Sub("podfolder"), "b.lnk"), "x");

        var names = _files.ListFolder(temp.Root);

        Assert.Equal(new[] { "a.lnk", "desktop.ini" }, names.OrderBy(n => n, StringComparer.Ordinal));
    }

    [Fact]
    public void ListFolder_Missing_Empty()
    {
        using var temp = new TempDirectory();

        Assert.Empty(_files.ListFolder(temp.Sub("nie-ma")));
    }

    [Fact]
    public void ListFolder_Denied_OperationException()
    {
        // Odmowa tylko na folderze testowym w %TEMP%\dso-tests\<guid>, zdejmowana w finally (właściciel = ten proces).
        using var temp = new TempDirectory();
        var locked = temp.Sub("zablokowany");
        Directory.CreateDirectory(locked);
        File.WriteAllText(Path.Combine(locked, "a.lnk"), "x");
        var everyone = new SecurityIdentifier(WellKnownSidType.WorldSid, null);
        var acl = Directory.GetAccessControl(locked);
        acl.AddAccessRule(new FileSystemAccessRule(everyone, FileSystemRights.ListDirectory, InheritanceFlags.None, PropagationFlags.None, AccessControlType.Deny));
        Directory.SetAccessControl(locked, acl);
        try
        {
            // SeBackupPrivilege/SeRestorePrivilege omijają odmowę z ACL (Git Bash) – wyłączone tylko w tokenie tego wątku.
            using (ThreadPrivileges.Disable(ThreadPrivileges.Backup, ThreadPrivileges.Restore))
            {
                var ex = Assert.Throws<OperationException>(() => _files.ListFolder(locked));
                Assert.Equal(OperationErrorKind.AccessDenied, ex.Kind);
                Assert.Contains(locked, ex.Message, StringComparison.OrdinalIgnoreCase);
            }
        }
        finally
        {
            var restore = Directory.GetAccessControl(locked);
            restore.RemoveAccessRuleAll(new FileSystemAccessRule(everyone, FileSystemRights.ListDirectory, AccessControlType.Deny));
            Directory.SetAccessControl(locked, restore);
        }
    }

    [Fact]
    public void ListFolder_PathIsFile_OperationException()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(temp.Sub("plik"), "x");

        Assert.Throws<OperationException>(() => _files.ListFolder(temp.Sub("plik")));
    }

    [Fact]
    public void ReadShortcut_SavedLnk_TargetAndArguments()
    {
        using var temp = new TempDirectory();
        var target = temp.Sub("sonda.exe");
        File.WriteAllText(target, "x");
        ShortcutWriter.Save(temp.Sub("Sonda.lnk"), target, "--tray /min");

        var shortcut = _files.ReadShortcut(temp.Sub("Sonda.lnk"));

        Assert.NotNull(shortcut);
        Assert.Equal(target, shortcut!.TargetPath, ignoreCase: true);
        Assert.Equal("--tray /min", shortcut.Arguments);
        Assert.False(shortcut.Advertised);
    }

    [Fact]
    public void ReadShortcut_EnvironmentTarget_RawPath()
    {
        using var temp = new TempDirectory();
        ShortcutWriter.Save(temp.Sub("Notatnik.lnk"), @"%SystemRoot%\System32\notepad.exe");

        var shortcut = _files.ReadShortcut(temp.Sub("Notatnik.lnk"));

        Assert.Equal(@"%SystemRoot%\System32\notepad.exe", shortcut!.TargetPath, ignoreCase: true);
        Assert.Null(shortcut.Arguments);
    }

    [Fact]
    public void ReadShortcut_DarwinFlag_Advertised()
    {
        using var temp = new TempDirectory();
        var target = temp.Sub("sonda.exe");
        File.WriteAllText(target, "x");
        ShortcutWriter.Save(temp.Sub("Msi.lnk"), target, darwinId: "{00000000-0000-0000-0000-000000000000}DsoTest>Sonda");

        Assert.True(_files.ReadShortcut(temp.Sub("Msi.lnk"))!.Advertised);
    }

    [Fact]
    public void ReadShortcut_NotShortcut_Null()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(temp.Sub("udawany.lnk"), "[InternetShortcut]\r\nURL=https://example.com/\r\n");
        File.WriteAllBytes(temp.Sub("pusty.lnk"), Array.Empty<byte>());

        Assert.Null(_files.ReadShortcut(temp.Sub("udawany.lnk")));
        Assert.Null(_files.ReadShortcut(temp.Sub("pusty.lnk")));
    }

    [Fact]
    public void ReadShortcut_MissingFile_OperationException()
    {
        using var temp = new TempDirectory();

        var ex = Assert.Throws<OperationException>(() => _files.ReadShortcut(temp.Sub("nie-ma.lnk")));
        Assert.Equal(OperationErrorKind.NotFound, ex.Kind);
    }

    private static readonly byte[] LinkHeader =
    {
        0x4C, 0x00, 0x00, 0x00, 0x01, 0x14, 0x02, 0x00, 0x00, 0x00, 0x00, 0x00, 0xC0, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x46,
    };

    // Plik z nagłówkiem skrótu i dowolną treścią (folder Autostart jest zapisywalny przez konto): wynik null albo OperationException,
    // nigdy inny wyjątek, który przerwałby odczyt całej strony.
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(56)]
    [InlineData(4096)]
    public void ReadShortcut_HeaderValidGarbage_NullOrOperationException(int garbageBytes)
    {
        using var temp = new TempDirectory();
        var garbage = new byte[garbageBytes];
        new Random(garbageBytes).NextBytes(garbage);
        var bytes = new byte[LinkHeader.Length + garbage.Length];
        LinkHeader.CopyTo(bytes, 0);
        garbage.CopyTo(bytes, LinkHeader.Length);
        File.WriteAllBytes(temp.Sub("zepsuty.lnk"), bytes);

        var error = Record.Exception(() => _files.ReadShortcut(temp.Sub("zepsuty.lnk")));

        Assert.True(error is null or OperationException, error?.GetType().FullName + ": " + error?.Message);
    }

    // HRESULT-y COM CLR zamienia na różne typy wyjątków; każdy (poza brakiem uprawnień) = OperationException(Unexpected) z oryginałem w środku.
    [Theory]
    [InlineData(typeof(OutOfMemoryException))]
    [InlineData(typeof(BadImageFormatException))]
    [InlineData(typeof(NotImplementedException))]
    [InlineData(typeof(NullReferenceException))]
    [InlineData(typeof(COMException))]
    [InlineData(typeof(InvalidCastException))]
    [InlineData(typeof(FileNotFoundException))]
    public void ReadShortcut_AnyComFailure_OperationExceptionUnexpected(Type failure)
    {
        using var temp = new TempDirectory();
        File.WriteAllBytes(temp.Sub("a.lnk"), LinkHeader);
        var files = new AutostartFiles(_ => 3, _ => (true, 0u, 0), _ => throw (Exception)Activator.CreateInstance(failure)!);

        var ex = Assert.Throws<OperationException>(() => files.ReadShortcut(temp.Sub("a.lnk")));

        Assert.Equal(OperationErrorKind.Unexpected, ex.Kind);
        Assert.IsType(failure, ex.InnerException);
    }

    [Fact]
    public void ReadShortcut_ComAccessDenied_OperationExceptionAccessDenied()
    {
        using var temp = new TempDirectory();
        File.WriteAllBytes(temp.Sub("a.lnk"), LinkHeader);
        var files = new AutostartFiles(_ => 3, _ => (true, 0u, 0), _ => throw new UnauthorizedAccessException());

        Assert.Equal(OperationErrorKind.AccessDenied, Assert.Throws<OperationException>(() => files.ReadShortcut(temp.Sub("a.lnk"))).Kind);
    }

    // Plik otwarty przez kogoś innego do zapisu i usuwania: odczyt skrótu (nagłówek i COM z STGM_SHARE_DENY_NONE) nie odmawia ani nie blokuje.
    [Fact]
    public void ReadShortcut_FileOpenForWriting_StillReads()
    {
        using var temp = new TempDirectory();
        var target = temp.Sub("sonda.exe");
        File.WriteAllText(target, "x");
        ShortcutWriter.Save(temp.Sub("Sonda.lnk"), target, "--tray");

        using (new FileStream(temp.Sub("Sonda.lnk"), FileMode.Open, FileAccess.ReadWrite, FileShare.Read | FileShare.Write | FileShare.Delete))
        {
            Assert.Equal("--tray", _files.ReadShortcut(temp.Sub("Sonda.lnk"))!.Arguments);
        }
    }

    [Fact]
    public void Probe_Existing_Exists()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(temp.Sub("a.exe"), "x");

        Assert.Equal(FileProbe.Exists, _files.Probe(temp.Sub("a.exe")));
    }

    [Fact]
    public void Probe_MissingFile_Missing()
    {
        using var temp = new TempDirectory();

        Assert.Equal(FileProbe.Missing, _files.Probe(temp.Sub("nie-ma.exe")));
    }

    [Fact]
    public void Probe_MissingDirectory_Missing()
    {
        using var temp = new TempDirectory();

        Assert.Equal(FileProbe.Missing, _files.Probe(Path.Combine(temp.Sub("nie-ma"), "a.exe")));
    }

    // Katalog o ścieżce polecenia (np. prefiks „C:\Program Files” bez cudzysłowu) nigdy nie wygrywa jako plik.
    [Fact]
    public void Probe_ExistingDirectory_UnknownNotExists()
    {
        using var temp = new TempDirectory();
        Directory.CreateDirectory(temp.Sub("katalog"));

        Assert.Equal(FileProbe.Unknown, _files.Probe(temp.Sub("katalog")));
    }

    [Fact]
    public void Probe_Unc_UnknownWithoutIo()
    {
        var calls = new List<string>();
        var files = new AutostartFiles(
            root => { calls.Add(root); return 3; },
            path => { calls.Add(path); return (true, 0u, 0); });

        Assert.Equal(FileProbe.Unknown, files.Probe(@"\\server\share\a.exe"));
        Assert.Equal(FileProbe.Unknown, files.Probe(@"\\?\C:\Windows\notepad.exe"));
        Assert.Empty(calls);
    }

    [Fact]
    public void Probe_Relative_Unknown()
    {
        var calls = new List<string>();
        var files = new AutostartFiles(
            root => { calls.Add(root); return 3; },
            path => { calls.Add(path); return (true, 0u, 0); });

        Assert.Equal(FileProbe.Unknown, files.Probe("a.exe"));
        Assert.Equal(FileProbe.Unknown, files.Probe(@"C:a.exe"));
        Assert.Equal(FileProbe.Unknown, files.Probe(@"bin\a.exe"));
        Assert.Equal(FileProbe.Unknown, files.Probe(string.Empty));
        Assert.Empty(calls);
    }

    [Fact]
    public void Probe_NotFixedDrive_UnknownWithoutAttributes()
    {
        var calls = new List<string>();
        var files = new AutostartFiles(
            root => { calls.Add("drive:" + root); return 4; },
            path => { calls.Add(path); return (false, 0u, 2); });

        Assert.Equal(FileProbe.Unknown, files.Probe(@"Z:\a.exe"));
        Assert.Equal(new[] { @"drive:Z:\" }, calls);
    }

    [Theory]
    [InlineData(2, FileProbe.Missing)]
    [InlineData(3, FileProbe.Missing)]
    [InlineData(5, FileProbe.Unknown)]
    [InlineData(21, FileProbe.Unknown)]
    [InlineData(123, FileProbe.Unknown)]
    public void Probe_Error_OnlyNotFoundIsMissing(int error, FileProbe expected)
    {
        var files = new AutostartFiles(_ => 3, _ => (false, 0u, error));

        Assert.Equal(expected, files.Probe(@"C:\a.exe"));
    }

    [Fact]
    public void Probe_TooLongPath_UnknownWithoutIo()
    {
        var calls = 0;
        var files = new AutostartFiles(_ => { calls++; return 3; }, _ => { calls++; return (false, 0u, 3); });

        Assert.Equal(FileProbe.Unknown, files.Probe(@"C:\" + new string('a', 300) + ".exe"));
        Assert.Equal(0, calls);
    }

    [Fact]
    public void Details_Notepad_MicrosoftCorporation()
    {
        var notepad = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "notepad.exe");

        var details = _files.Details(notepad);

        Assert.NotNull(details);
        Assert.Equal("Microsoft Corporation", details!.CompanyName);
        Assert.False(string.IsNullOrWhiteSpace(details.ProductName));
    }

    [Fact]
    public void Details_NoVersionOrMissing_Null()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(temp.Sub("a.exe"), "x");

        Assert.Null(_files.Details(temp.Sub("a.exe")));
        Assert.Null(_files.Details(temp.Sub("nie-ma.exe")));
    }
}
