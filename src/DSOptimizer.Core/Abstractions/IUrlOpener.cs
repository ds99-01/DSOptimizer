namespace DSOptimizer.Core.Abstractions;

/// <summary>Otwiera dozwolony adres w przeglądarce bez podnoszenia przeglądarki. Rzuca <c>OperationException</c> z polskim komunikatem, gdy nie może.</summary>
public interface IUrlOpener
{
    void Open(string url);
}
