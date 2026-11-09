using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security;
using System.Security.AccessControl;
using System.Text;
using DSOptimizer.Core.Journal;
using DSOptimizer.Core.Settings;
using DSOptimizer.Core.Storage;
using Newtonsoft.Json;

namespace DSOptimizer.Windows;

internal enum SalvageOutcome
{
    Moved,
    NotPresent,
    Rejected,
}

/// <summary>Wynik dla jednego pliku: <see cref="Source"/> = ścieżka w odłożonym folderze.</summary>
internal sealed record SalvageFile(SalvageOutcome Outcome, string Source, SalvageRejection? Rejection = null, string? Detail = null, bool Transient = false);

/// <summary><see cref="SetAside"/> = foldery odłożone przy zabezpieczaniu korzenia albo <c>journal</c> przed zapisem kopii.</summary>
internal sealed record AsideSalvage(SalvageFile Journal, SalvageFile Settings, IReadOnlyList<string> SetAside);

/// <summary>
/// Oba puste (<see cref="Moved"/> = <c>null</c>, <see cref="Rejected"/> bez elementów) = brak kandydatów.
/// <see cref="SetAside"/> jak w <see cref="AsideSalvage"/>.
/// </summary>
internal sealed record OldAsidesSalvage(SalvageFile? Moved, IReadOnlyList<SalvageFile> Rejected, IReadOnlyList<string> SetAside);

/// <summary>
/// Ratowanie historii zmian i ustawień z folderu odłożonego jako <c>.untrusted-*</c>. Plik przechodzi tylko przez
/// <see cref="ProtectedFileReader"/> (dowód zapisu przez administratora), poprawną treść i zgodny komputer. Nowy plik w korzeniu
/// powstaje od zera z chronionym deskryptorem (<see cref="SecureDirectory.CreateTrustedFileSecurity"/>) i ma bajty źródła bez
/// zmian; źródło zostaje na miejscu.
/// </summary>
internal static class SetAsideSalvage
{
    public const long MaxJournalBytes = 16L * 1024 * 1024;
    public const int MaxOldAsides = 64;

    private static readonly Encoding StrictUtf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    /// <summary>Folder odłożony przy tym starcie: <c>journal\journal.json</c> i <c>settings.json</c>, każdy osobno.</summary>
    public static AsideSalvage FromAside(string asideRoot, SecureRoot root, string machineName)
    {
        if (asideRoot is null)
        {
            throw new ArgumentNullException(nameof(asideRoot));
        }

        if (root is null)
        {
            throw new ArgumentNullException(nameof(root));
        }

        if (machineName is null)
        {
            throw new ArgumentNullException(nameof(machineName));
        }

        var setAside = new List<string>();
        var journalSource = Path.Combine(asideRoot, SecureRoot.JournalLeafName, JsonFileJournalStore.FileName);
        var journal = ReadJournal(journalSource, machineName, out var journalBytes)
            ?? WriteJournal(root, journalSource, journalBytes!, setAside);
        var settingsSource = Path.Combine(asideRoot, JsonFileSettingsStore.FileName);
        var settings = ReadSettings(settingsSource, out var settingsBytes)
            ?? WriteNew(settingsSource, Path.Combine(root.FullPath, JsonFileSettingsStore.FileName), settingsBytes!);
        return new AsideSalvage(journal, settings, setAside);
    }

    /// <summary>
    /// Foldery <c>&lt;korzeń&gt;.untrusted-*</c> z dawnych startów (warunek wołającego: w korzeniu nie ma
    /// <c>journal\journal.json</c>). Bierze pod uwagę tylko zwykłe foldery (nie linki) z właścicielem Administratorzy albo SYSTEM,
    /// najwyżej <see cref="MaxOldAsides"/>. Przenosi jeden journal: poprawny o największym czasie zapisu z uchwytu. Gdy któryś plik jest
    /// chwilowo zajęty (<see cref="SalvageFile.Transient"/>), nie przenosi nic.
    /// </summary>
    public static OldAsidesSalvage FromOldAsides(SecureRoot root, string machineName)
    {
        if (root is null)
        {
            throw new ArgumentNullException(nameof(root));
        }

        if (machineName is null)
        {
            throw new ArgumentNullException(nameof(machineName));
        }

        var rejected = new List<SalvageFile>();
        var setAside = new List<string>();
        string? bestSource = null;
        byte[]? bestBytes = null;
        var bestTime = DateTime.MinValue;
        foreach (var aside in OldAsideFolders(root))
        {
            var source = Path.Combine(aside, SecureRoot.JournalLeafName, JsonFileJournalStore.FileName);
            var verdict = ReadJournal(source, machineName, out var bytes, out var lastWriteUtc);
            if (verdict is { Outcome: SalvageOutcome.NotPresent })
            {
                continue;
            }

            if (verdict is not null)
            {
                rejected.Add(verdict);
                continue;
            }

            if (bestSource is null || lastWriteUtc > bestTime)
            {
                bestSource = source;
                bestBytes = bytes;
                bestTime = lastWriteUtc;
            }
        }

        // Plik chwilowo zajęty (np. antywirus) może być najnowszą historią: przeniesienie starszego zamknęłoby skanowanie na zawsze,
        // więc decyzja czeka na kolejny start.
        if (bestSource is null || rejected.Exists(file => file.Transient))
        {
            return new OldAsidesSalvage(null, rejected, setAside);
        }

        var written = WriteJournal(root, bestSource, bestBytes!, setAside);
        if (written.Outcome == SalvageOutcome.Moved)
        {
            return new OldAsidesSalvage(written, rejected, setAside);
        }

        rejected.Add(written);
        return new OldAsidesSalvage(null, rejected, setAside);
    }

    private static IEnumerable<string> OldAsideFolders(SecureRoot root)
    {
        var parent = Path.GetDirectoryName(root.FullPath);
        if (string.IsNullOrEmpty(parent))
        {
            return Array.Empty<string>();
        }

        try
        {
            // Najpierw odsiew folderów, potem limit: zwykłe konto może założyć w C:\ProgramData dowolnie wiele folderów
            // o pasującej nazwie, ale nie z właścicielem Administratorzy/SYSTEM, więc nie wypchnie nimi prawdziwych z limitu.
            return Directory.GetDirectories(parent, Path.GetFileName(root.FullPath) + ".untrusted-*")
                .OrderByDescending(path => path, StringComparer.Ordinal)
                .Where(SecureDirectory.IsAdminOwnedPlainFolder)
                .Take(MaxOldAsides)
                .ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException or ArgumentException)
        {
            return Array.Empty<string>();
        }
    }

    private static SalvageFile? ReadJournal(string source, string machineName, out byte[]? bytes)
        => ReadJournal(source, machineName, out bytes, out _);

    /// <summary><c>null</c> = poprawny kandydat (<paramref name="bytes"/>); inaczej wynik NotPresent albo Rejected.</summary>
    private static SalvageFile? ReadJournal(string source, string machineName, out byte[]? bytes, out DateTime lastWriteUtc)
    {
        bytes = null;
        lastWriteUtc = default;
        var read = ProtectedFileReader.TryRead(source, MaxJournalBytes);
        if (Verdict(source, read) is { } verdict)
        {
            return verdict;
        }

        JournalDocument document;
        try
        {
            document = JournalSerializer.Deserialize(Decode(read!.Bytes!));
        }
        catch (Exception ex) when (ex is JournalFormatException or JsonException or DecoderFallbackException)
        {
            return new SalvageFile(SalvageOutcome.Rejected, source, SalvageRejection.Corrupted, ex.Message);
        }

        if (!string.Equals(document.Machine, machineName, StringComparison.OrdinalIgnoreCase))
        {
            return new SalvageFile(SalvageOutcome.Rejected, source, SalvageRejection.OtherComputer, document.Machine);
        }

        bytes = read.Bytes;
        lastWriteUtc = read.LastWriteUtc;
        return null;
    }

    private static SalvageFile? ReadSettings(string source, out byte[]? bytes)
    {
        bytes = null;
        var read = ProtectedFileReader.TryRead(source, JsonFileSettingsStore.MaxFileBytes);
        if (Verdict(source, read) is { } verdict)
        {
            return verdict;
        }

        try
        {
            AppSettingsSerializer.Deserialize(Decode(read!.Bytes!));
        }
        catch (Exception ex) when (ex is SettingsFormatException or JsonException or DecoderFallbackException)
        {
            return new SalvageFile(SalvageOutcome.Rejected, source, SalvageRejection.Corrupted, ex.Message);
        }

        bytes = read.Bytes;
        return null;
    }

    /// <summary>Brak pliku albo odrzucenie przez <see cref="ProtectedFileReader"/>; <c>null</c> = treść do sprawdzenia.</summary>
    private static SalvageFile? Verdict(string source, ProtectedFileRead? read)
    {
        if (read is null)
        {
            return new SalvageFile(SalvageOutcome.NotPresent, source);
        }

        return read.Rejection is { } rejection
            ? new SalvageFile(SalvageOutcome.Rejected, source, rejection, read.Detail, read.Transient)
            : null;
    }

    /// <summary>Ścisłe UTF-8: BOM pominięty, niepoprawna sekwencja = <see cref="DecoderFallbackException"/>.</summary>
    private static string Decode(byte[] bytes)
    {
        var start = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF ? 3 : 0;
        return StrictUtf8.GetString(bytes, start, bytes.Length - start);
    }

    /// <summary>Foldery odłożone przy zabezpieczaniu korzenia i <c>journal</c> trafiają do <paramref name="setAside"/>.</summary>
    private static SalvageFile WriteJournal(SecureRoot root, string source, byte[] bytes, List<string> setAside)
    {
        try
        {
            setAside.AddRange(root.EnsureLeaf(root.JournalDirectory));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new SalvageFile(SalvageOutcome.Rejected, source, SalvageRejection.WriteFailed, ex.Message);
        }

        return WriteNew(source, Path.Combine(root.JournalDirectory, JsonFileJournalStore.FileName), bytes);
    }

    /// <summary>
    /// Nowy plik z chronionym deskryptorem i bajtami bez zmian: zapis do <c>&lt;cel&gt;.tmp</c> (<see cref="FileMode.CreateNew"/>),
    /// potem przeniesienie na cel, jak <see cref="JsonFileJournalStore.Save"/>. Przerwany zapis nie zostawia uciętego pliku
    /// docelowego. Istniejący cel nigdy nie jest nadpisywany (przeniesienie się nie udaje); pozostałość <c>.tmp</c> po przerwanym
    /// zapisie jest usuwana przed zapisem, a <c>.tmp</c> po błędzie sprzątany.
    /// </summary>
    private static SalvageFile WriteNew(string source, string target, byte[] bytes)
    {
        var temporary = target + ".tmp";
        try
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }

            using (var stream = new FileStream(
                temporary, FileMode.CreateNew, FileSystemRights.Write, FileShare.None, 4096, FileOptions.None, SecureDirectory.CreateTrustedFileSecurity()))
            {
                stream.Write(bytes, 0, bytes.Length);
            }

            File.Move(temporary, target);
            return new SalvageFile(SalvageOutcome.Moved, source);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            TryDelete(temporary);
            return new SalvageFile(SalvageOutcome.Rejected, source, SalvageRejection.WriteFailed, ex.Message);
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
            // Plik .tmp zostaje; kolejny zapis usuwa go przed swoim.
        }
        catch (UnauthorizedAccessException)
        {
            // j.w.
        }
    }
}
