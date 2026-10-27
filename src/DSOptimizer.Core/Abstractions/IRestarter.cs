namespace DSOptimizer.Core.Abstractions;

public interface IRestarter
{
    /// <summary>
    /// Zleca ponowne uruchomienie komputera. Rzuca wyłącznie <see cref="Errors.OperationException"/>
    /// z polskim komunikatem, gdy restartu nie da się zlecić.
    /// </summary>
    void Restart();
}
