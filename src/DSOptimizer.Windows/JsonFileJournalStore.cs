using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.AccessControl;
using System.Security;
using System.Text;
using DSOptimizer.Core.Abstractions;
using DSOptimizer.Core.Journal;
using Newtonsoft.Json;

namespace DSOptimizer.Windows;

/// <summary>
/// Journal w pliku JSON: zapis atomowy (plik .tmp + File.Replace), uszkodzony plik odkładany na bok.
/// Każdy błąd IO/ACL/JSON jest opakowany w <see cref="JournalStoreException"/> (kontrakt, na którym polega silnik).
/// </summary>
public sealed class JsonFileJournalStore : IJournalStore
{
    public const string FileName = "journal.json";

    private static readonly Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    private readonly SecureRoot _root;
    private readonly string _directory;
    private readonly string _machineName;
    private readonly IClock _clock;

    public JsonFileJournalStore(SecureRoot root, string machineName, IClock clock)
    {
        _root = root ?? throw new ArgumentNullException(nameof(root));
        _directory = root.JournalDirectory;
        _machineName = machineName ?? throw new ArgumentNullException(nameof(machineName));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    public string FilePath => Path.Combine(_directory, FileName);

    public JournalLoadResult Load()
    {
        try
        {
            _root.EnsureLeaf(_directory);
            if (!File.Exists(FilePath))
            {
                return new JournalLoadResult(NewDocument(), DescribeSetAsideJournals());
            }

            if (!SecureDirectory.IsTrustedFile(FilePath))
            {
                var untrustedPath = Path.Combine(_directory, $"journal.json.untrusted-{Stamp()}");
                File.Move(FilePath, untrustedPath);
                return new JournalLoadResult(
                    NewDocument(),
                    $"Journal miał niezaufanego właściciela lub uprawnienia i został odłożony: {untrustedPath}. Utworzono nowy.");
            }

            var json = File.ReadAllText(FilePath, Utf8NoBom);
            try
            {
                return new JournalLoadResult(JournalSerializer.Deserialize(json), null);
            }
            catch (JournalFormatException ex)
            {
                var corruptPath = Path.Combine(_directory, $"journal.corrupt-{Stamp()}.json");
                File.Move(FilePath, corruptPath);
                return new JournalLoadResult(NewDocument(), $"Journal był uszkodzony ({ex.Message}). Przeniesiono go do {corruptPath} i utworzono nowy.");
            }
        }
        catch (IOException ex)
        {
            throw new JournalStoreException($"Nie można odczytać journala {FilePath}: {ex.Message}", ex);
        }
        catch (UnauthorizedAccessException ex)
        {
            throw new JournalStoreException($"Brak dostępu do journala {FilePath}: {ex.Message}", ex);
        }
        catch (SecurityException ex)
        {
            throw new JournalStoreException($"Brak uprawnień do journala {FilePath}: {ex.Message}", ex);
        }
        catch (InvalidOperationException ex)
        {
            throw new JournalStoreException($"Błąd uprawnień journala {FilePath}: {ex.Message}", ex);
        }
        catch (ArgumentException ex)
        {
            throw new JournalStoreException($"Niedozwolona ścieżka journala {FilePath}: {ex.Message}", ex);
        }
        catch (NotSupportedException ex)
        {
            throw new JournalStoreException($"Nieobsługiwana operacja na journalu {FilePath}: {ex.Message}", ex);
        }
        catch (JsonException ex)
        {
            throw new JournalStoreException($"Nie można zinterpretować journala {FilePath}: {ex.Message}", ex);
        }
    }

    public void Save(JournalDocument document)
    {
        if (document is null)
        {
            throw new ArgumentNullException(nameof(document));
        }

        try
        {
            _root.EnsureLeaf(_directory);
            var temporary = FilePath + ".tmp";
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }

            // .tmp powstaje od razu z chronionym DACL (Administratorzy + SYSTEM) - bez okna z dziedziczonym, otwartym ACL.
            var bytes = Utf8NoBom.GetBytes(JournalSerializer.Serialize(document));
            using (var stream = new FileStream(
                temporary, FileMode.CreateNew, FileSystemRights.Write, FileShare.None, 4096, FileOptions.None, SecureDirectory.CreateTrustedFileSecurity()))
            {
                stream.Write(bytes, 0, bytes.Length);
            }
            if (File.Exists(FilePath))
            {
                File.Replace(temporary, FilePath, destinationBackupFileName: null);
            }
            else
            {
                File.Move(temporary, FilePath);
            }

            SecureDirectory.SetTrustedOwnerAndProtectedAcl(FilePath);
        }
        catch (IOException ex)
        {
            throw new JournalStoreException($"Nie można zapisać journala {FilePath}: {ex.Message}", ex);
        }
        catch (UnauthorizedAccessException ex)
        {
            throw new JournalStoreException($"Brak dostępu do zapisu journala {FilePath}: {ex.Message}", ex);
        }
        catch (SecurityException ex)
        {
            throw new JournalStoreException($"Brak uprawnień do zapisu journala {FilePath}: {ex.Message}", ex);
        }
        catch (InvalidOperationException ex)
        {
            throw new JournalStoreException($"Błąd uprawnień zapisu journala {FilePath}: {ex.Message}", ex);
        }
        catch (ArgumentException ex)
        {
            throw new JournalStoreException($"Niedozwolona ścieżka journala {FilePath}: {ex.Message}", ex);
        }
        catch (NotSupportedException ex)
        {
            throw new JournalStoreException($"Nieobsługiwana operacja zapisu journala {FilePath}: {ex.Message}", ex);
        }
        catch (JsonException ex)
        {
            throw new JournalStoreException($"Nie można zserializować journala {FilePath}: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Brak journal.json może oznaczać, że <see cref="SecureRoot.EnsureLeaf"/> (tu albo w innym komponencie, np. FileLog)
    /// odłożył niezaufany katalog journala lub jego rodzica jako <c>.untrusted-*</c>. Wtedy zwraca ostrzeżenie ze ścieżkami
    /// odłożonych journali, żeby undo z wartości domyślnych nie przeszło bez słowa. Brak odłożonych kopii → null.
    /// </summary>
    private string? DescribeSetAsideJournals()
    {
        var leaf = Path.GetFullPath(_directory).TrimEnd('\\');
        var parent = Path.GetDirectoryName(leaf);
        if (string.IsNullOrEmpty(parent))
        {
            return null;
        }

        var leafName = Path.GetFileName(leaf);
        var candidates = Directory.GetDirectories(parent, leafName + ".untrusted-*")
            .Select(aside => Path.Combine(aside, FileName))
            .ToList();
        var grandParent = Path.GetDirectoryName(parent);
        if (!string.IsNullOrEmpty(grandParent))
        {
            candidates.AddRange(Directory.GetDirectories(grandParent, Path.GetFileName(parent) + ".untrusted-*")
                .Select(aside => Path.Combine(aside, leafName, FileName)));
        }

        var found = candidates.Where(File.Exists).ToList();
        return found.Count == 0
            ? null
            : $"Katalog journala miał niezaufanego właściciela lub uprawnienia i został odłożony – poprzedni journal nie jest używany: {string.Join(", ", found)}. Utworzono nowy.";
    }

    private string Stamp() => _clock.UtcNow.ToLocalTime().ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);

    private JournalDocument NewDocument() => new() { Machine = _machineName };
}
