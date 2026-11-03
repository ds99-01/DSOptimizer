namespace DSOptimizer.Core.Abstractions;

public interface IProgramRelauncher
{
    /// <summary>
    /// Startuje nową instancję programu z argumentem <c>--relaunched</c>; nowa instancja czeka na zwolnienie mutexa przez bieżącą. Nie zamyka bieżącej – robi to wywołujący.
    /// Rzuca wyłącznie <see cref="Errors.OperationException"/> z komunikatem przez <c>L.T</c>.
    /// </summary>
    void Relaunch();
}
