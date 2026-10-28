using System;
using System.Globalization;
using System.IO;
using System.Security;
using System.Security.AccessControl;
using System.Text;
using DSOptimizer.Core.Abstractions;
using DSOptimizer.Core.Localization;
using DSOptimizer.Core.Settings;
using Newtonsoft.Json;

namespace DSOptimizer.Windows;

/// <summary>
/// Ustawienia aplikacji w <c>%ProgramData%\DSOptimizer\settings.json</c>: zapis atomowy (plik .tmp + File.Replace),
/// kontrola zaufania jak w journalu. <see cref="Load"/> nigdy nie rzuca (błąd = wartości domyślne + ostrzeżenie),
/// <see cref="Save"/> rzuca wyłącznie <see cref="SettingsStoreException"/>.
/// </summary>
public sealed class JsonFileSettingsStore : ISettingsStore
{
    public const string FileName = "settings.json";

    internal const int MaxFileBytes = 65536;

    private static readonly Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    private readonly SecureRoot _root;
    private readonly IClock _clock;

    public JsonFileSettingsStore(SecureRoot root, IClock clock)
    {
        _root = root ?? throw new ArgumentNullException(nameof(root));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    public string FilePath => Path.Combine(_root.FullPath, FileName);

    public SettingsLoadResult Load()
    {
        try
        {
            _root.Ensure();
            if (!File.Exists(FilePath))
            {
                return new SettingsLoadResult(AppSettings.Default, null);
            }

            if (!SecureDirectory.IsTrustedFile(FilePath))
            {
                var untrustedPath = Path.Combine(_root.FullPath, $"{FileName}.untrusted-{Stamp()}");
                File.Move(FilePath, untrustedPath);
                return Defaults(L.F("Plik ustawień miał niezaufanego właściciela lub uprawnienia i został odłożony: {0}. Używam ustawień domyślnych.", untrustedPath));
            }

            var length = new FileInfo(FilePath).Length;
            if (length > MaxFileBytes)
            {
                return Defaults(L.F("Plik ustawień {0} jest za duży ({1} B, limit {2} B). Używam ustawień domyślnych.", FilePath, length, MaxFileBytes));
            }

            // ReadAllText (.NET Framework) wykrywa i zdejmuje BOM, więc plik zapisany w Notatniku z BOM się wczytuje;
            // sam serializer odrzuca BOM dopiero w surowym napisie.
            var json = File.ReadAllText(FilePath, Utf8NoBom);
            try
            {
                return new SettingsLoadResult(AppSettingsSerializer.Deserialize(json), null);
            }
            catch (SettingsFormatException ex)
            {
                return Defaults(
                    L.F("Plik ustawień {0} jest uszkodzony ({1}). Używam ustawień domyślnych; plik zostanie nadpisany przy następnej zmianie.", FilePath, ex.Message));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException
            or InvalidOperationException or ArgumentException or NotSupportedException or JsonException)
        {
            return Defaults(L.F("Nie można odczytać pliku ustawień {0}: {1}. Używam ustawień domyślnych.", FilePath, ex.Message));
        }
    }

    /// <summary>
    /// Tylko odczyt zapamiętanego języka: bez tworzenia folderu, bez przenoszenia pliku, nigdy nie rzuca.
    /// <c>null</c> = brak folderu/pliku, plik niezaufany, za duży, uszkodzony albo bez poprawnego pola.
    /// </summary>
    public UiLanguage? PeekLanguage()
    {
        try
        {
            if (!File.Exists(FilePath) || !SecureDirectory.IsTrustedFile(FilePath))
            {
                return null;
            }

            if (new FileInfo(FilePath).Length > MaxFileBytes)
            {
                return null;
            }

            var json = File.ReadAllText(FilePath, Utf8NoBom);
            return AppSettingsSerializer.Deserialize(json).Language;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException
            or InvalidOperationException or ArgumentException or NotSupportedException or JsonException
            or SettingsFormatException)
        {
            return null;
        }
    }

    public void Save(AppSettings settings)
    {
        if (settings is null)
        {
            throw new ArgumentNullException(nameof(settings));
        }

        try
        {
            _root.Ensure();
            var temporary = FilePath + ".tmp";
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }

            // .tmp powstaje od razu z chronionym DACL (Administratorzy + SYSTEM) - bez okna z dziedziczonym, otwartym ACL.
            var bytes = Utf8NoBom.GetBytes(AppSettingsSerializer.Serialize(settings));
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

            // File.Replace przenosi ACL zastępowanego pliku, więc po zamianie ustawiamy zaufanego właściciela i chroniony DACL ponownie.
            SecureDirectory.SetTrustedOwnerAndProtectedAcl(FilePath);
        }
        catch (IOException ex)
        {
            throw new SettingsStoreException(L.F("Nie można zapisać ustawień {0}: {1}", FilePath, ex.Message), ex);
        }
        catch (UnauthorizedAccessException ex)
        {
            throw new SettingsStoreException(L.F("Brak dostępu do zapisu ustawień {0}: {1}", FilePath, ex.Message), ex);
        }
        catch (SecurityException ex)
        {
            throw new SettingsStoreException(L.F("Brak uprawnień do zapisu ustawień {0}: {1}", FilePath, ex.Message), ex);
        }
        catch (InvalidOperationException ex)
        {
            throw new SettingsStoreException(L.F("Błąd uprawnień zapisu ustawień {0}: {1}", FilePath, ex.Message), ex);
        }
        catch (ArgumentException ex)
        {
            throw new SettingsStoreException(L.F("Niedozwolona ścieżka ustawień {0}: {1}", FilePath, ex.Message), ex);
        }
        catch (NotSupportedException ex)
        {
            throw new SettingsStoreException(L.F("Nieobsługiwana operacja zapisu ustawień {0}: {1}", FilePath, ex.Message), ex);
        }
        catch (JsonException ex)
        {
            throw new SettingsStoreException(L.F("Nie można zserializować ustawień {0}: {1}", FilePath, ex.Message), ex);
        }
    }

    private static SettingsLoadResult Defaults(string warning) => new(AppSettings.Default, warning);

    private string Stamp() => _clock.UtcNow.ToLocalTime().ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
}
