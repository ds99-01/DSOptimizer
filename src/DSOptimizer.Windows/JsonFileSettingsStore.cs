using System;
using System.Globalization;
using System.IO;
using System.Security;
using System.Security.AccessControl;
using System.Text;
using DSOptimizer.Core.Abstractions;
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

    private const string DefaultsSuffix = "Używam ustawień domyślnych.";

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
                return Defaults($"Plik ustawień miał niezaufanego właściciela lub uprawnienia i został odłożony: {untrustedPath}. {DefaultsSuffix}");
            }

            var length = new FileInfo(FilePath).Length;
            if (length > MaxFileBytes)
            {
                return Defaults($"Plik ustawień {FilePath} jest za duży ({length} B, limit {MaxFileBytes} B). {DefaultsSuffix}");
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
                    $"Plik ustawień {FilePath} jest uszkodzony ({ex.Message}). Używam ustawień domyślnych; plik zostanie nadpisany przy następnej zmianie.");
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException
            or InvalidOperationException or ArgumentException or NotSupportedException or JsonException)
        {
            return Defaults($"Nie można odczytać pliku ustawień {FilePath}: {ex.Message}. {DefaultsSuffix}");
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
            throw new SettingsStoreException($"Nie można zapisać ustawień {FilePath}: {ex.Message}", ex);
        }
        catch (UnauthorizedAccessException ex)
        {
            throw new SettingsStoreException($"Brak dostępu do zapisu ustawień {FilePath}: {ex.Message}", ex);
        }
        catch (SecurityException ex)
        {
            throw new SettingsStoreException($"Brak uprawnień do zapisu ustawień {FilePath}: {ex.Message}", ex);
        }
        catch (InvalidOperationException ex)
        {
            throw new SettingsStoreException($"Błąd uprawnień zapisu ustawień {FilePath}: {ex.Message}", ex);
        }
        catch (ArgumentException ex)
        {
            throw new SettingsStoreException($"Niedozwolona ścieżka ustawień {FilePath}: {ex.Message}", ex);
        }
        catch (NotSupportedException ex)
        {
            throw new SettingsStoreException($"Nieobsługiwana operacja zapisu ustawień {FilePath}: {ex.Message}", ex);
        }
        catch (JsonException ex)
        {
            throw new SettingsStoreException($"Nie można zserializować ustawień {FilePath}: {ex.Message}", ex);
        }
    }

    private static SettingsLoadResult Defaults(string warning) => new(AppSettings.Default, warning);

    private string Stamp() => _clock.UtcNow.ToLocalTime().ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
}
