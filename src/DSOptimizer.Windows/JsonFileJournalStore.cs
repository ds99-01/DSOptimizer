using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.AccessControl;
using System.Security;
using System.Text;
using DSOptimizer.Core.Abstractions;
using DSOptimizer.Core.Journal;
using DSOptimizer.Core.Localization;
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
                    L.F("Journal miał niezaufanego właściciela lub uprawnienia i został odłożony: {0}. Utworzono nowy.", untrustedPath));
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
                return new JournalLoadResult(NewDocument(), L.F("Journal był uszkodzony ({0}). Przeniesiono go do {1} i utworzono nowy.", ex.Message, corruptPath));
            }
        }
        catch (IOException ex)
        {
            throw new JournalStoreException(L.F("Nie można odczytać journala {0}: {1}", FilePath, ex.Message), ex);
        }
        catch (UnauthorizedAccessException ex)
        {
            throw new JournalStoreException(L.F("Brak dostępu do journala {0}: {1}", FilePath, ex.Message), ex);
        }
        catch (SecurityException ex)
        {
            throw new JournalStoreException(L.F("Brak uprawnień do journala {0}: {1}", FilePath, ex.Message), ex);
        }
        catch (InvalidOperationException ex)
        {
            throw new JournalStoreException(L.F("Błąd uprawnień journala {0}: {1}", FilePath, ex.Message), ex);
        }
        catch (ArgumentException ex)
        {
            throw new JournalStoreException(L.F("Niedozwolona ścieżka journala {0}: {1}", FilePath, ex.Message), ex);
        }
        catch (NotSupportedException ex)
        {
            throw new JournalStoreException(L.F("Nieobsługiwana operacja na journalu {0}: {1}", FilePath, ex.Message), ex);
        }
        catch (JsonException ex)
        {
            throw new JournalStoreException(L.F("Nie można zinterpretować journala {0}: {1}", FilePath, ex.Message), ex);
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
            throw new JournalStoreException(L.F("Nie można zapisać journala {0}: {1}", FilePath, ex.Message), ex);
        }
        catch (UnauthorizedAccessException ex)
        {
            throw new JournalStoreException(L.F("Brak dostępu do zapisu journala {0}: {1}", FilePath, ex.Message), ex);
        }
        catch (SecurityException ex)
        {
            throw new JournalStoreException(L.F("Brak uprawnień do zapisu journala {0}: {1}", FilePath, ex.Message), ex);
        }
        catch (InvalidOperationException ex)
        {
            throw new JournalStoreException(L.F("Błąd uprawnień zapisu journala {0}: {1}", FilePath, ex.Message), ex);
        }
        catch (ArgumentException ex)
        {
            throw new JournalStoreException(L.F("Niedozwolona ścieżka journala {0}: {1}", FilePath, ex.Message), ex);
        }
        catch (NotSupportedException ex)
        {
            throw new JournalStoreException(L.F("Nieobsługiwana operacja zapisu journala {0}: {1}", FilePath, ex.Message), ex);
        }
        catch (JsonException ex)
        {
            throw new JournalStoreException(L.F("Nie można zserializować journala {0}: {1}", FilePath, ex.Message), ex);
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
            : L.F("Katalog journala miał niezaufanego właściciela lub uprawnienia i został odłożony – poprzedni journal nie jest używany: {0}. Utworzono nowy.", string.Join(", ", found));
    }

    private string Stamp() => _clock.UtcNow.ToLocalTime().ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);

    private JournalDocument NewDocument() => new() { Machine = _machineName };
}
