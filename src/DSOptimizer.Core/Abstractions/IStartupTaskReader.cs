using System.Collections.Generic;

namespace DSOptimizer.Core.Abstractions;

/// <summary>
/// Zadanie startowe (<c>windows.startupTask</c>) z manifestu pakietu zainstalowanego dla konta. Nazwy już rozwinięte (zasoby
/// <c>ms-resource:</c> jak na stronie „Aplikacje”); <see cref="EnabledInManifest"/> = atrybut <c>Enabled</c> zadania.
/// </summary>
public sealed record StartupTaskManifest(
    string PackageName,
    string PackageFullName,
    string PackageFamilyName,
    string InstallLocation,
    string TaskId,
    bool EnabledInManifest,
    string? DisplayName,
    string? PublisherDisplayName,
    string? Executable);

/// <summary>Zadania startowe konta i ostrzeżenia odczytu (np. pakiet z nieczytelnym manifestem pominięty).</summary>
public sealed record StartupTaskReading(IReadOnlyList<StartupTaskManifest> Tasks, IReadOnlyList<string> Warnings);

/// <summary>Zadania startowe aplikacji ze Sklepu zainstalowanych dla konta – tylko odczyt.</summary>
public interface IStartupTaskReader
{
    /// <summary>Zadania konta o podanym SID. Błąd całego odczytu (np. limit czasu) = <see cref="Errors.OperationException"/>.</summary>
    StartupTaskReading Read(string userSid);
}
