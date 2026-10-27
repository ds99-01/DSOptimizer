using System;

namespace DSOptimizer.Core.Abstractions;

/// <summary>
/// Kieruje wpisy do dwóch logów. Wyjątek <c>primary</c> (np. zapis do pliku) leci dalej jak bez tee;
/// wyjątek <c>secondary</c> (obserwator, np. okno) jest połykany, żeby nie przerwał silnika ani zapisu do pliku.
/// Błąd obserwatora nie jest logowany z powrotem do <c>primary</c> (brak pętli przy błędzie w trakcie logowania).
/// </summary>
public sealed class TeeLog : ILog
{
    private readonly ILog _primary;
    private readonly ILog _secondary;

    public TeeLog(ILog primary, ILog secondary)
    {
        _primary = primary ?? throw new ArgumentNullException(nameof(primary));
        _secondary = secondary ?? throw new ArgumentNullException(nameof(secondary));
    }

    public void Info(string component, string message)
    {
        _primary.Info(component, message);
        try
        {
            _secondary.Info(component, message);
        }
        catch (Exception)
        {
            // Obserwator nie może zepsuć logowania do pliku.
        }
    }

    public void Warn(string component, string message)
    {
        _primary.Warn(component, message);
        try
        {
            _secondary.Warn(component, message);
        }
        catch (Exception)
        {
            // Obserwator nie może zepsuć logowania do pliku.
        }
    }

    public void Fail(string component, string message, Exception? exception = null)
    {
        _primary.Fail(component, message, exception);
        try
        {
            _secondary.Fail(component, message, exception);
        }
        catch (Exception)
        {
            // Obserwator nie może zepsuć logowania do pliku.
        }
    }
}
