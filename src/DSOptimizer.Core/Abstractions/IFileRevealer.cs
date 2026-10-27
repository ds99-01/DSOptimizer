namespace DSOptimizer.Core.Abstractions;

/// <summary>Pokazuje plik w Eksploratorze (zaznaczony) bez podnoszenia Eksploratora. Rzuca <c>OperationException</c> z polskim komunikatem, gdy nie może.</summary>
public interface IFileRevealer
{
    void Reveal(string path);
}
