using DSOptimizer.Core.Model;

namespace DSOptimizer.Core.Abstractions;

/// <summary>Uruchamianie pozycji <c>launch</c> (panele, launchery) poza Job Objectem skryptów (spec, sekcja 4, A6).</summary>
public interface IProcessLauncher
{
    /// <summary>
    /// Uruchamia plik bez czekania na proces i okno. Niedozwolony albo brakujący plik i błąd startu:
    /// <see cref="Errors.OperationException"/>.
    /// </summary>
    void Launch(LaunchSpec launch);
}
