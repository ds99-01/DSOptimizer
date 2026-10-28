namespace DSOptimizer.Core.Abstractions;

public interface IProgramRelauncher
{
    /// <summary>
    /// Startuje nową instancję programu (bez argumentów). Nie zamyka bieżącej – robi to wywołujący.
    /// Rzuca wyłącznie <see cref="Errors.OperationException"/> z komunikatem przez <c>L.T</c>.
    /// </summary>
    void Relaunch();
}
