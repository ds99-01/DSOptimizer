using System;
using System.Collections.Generic;
using DSOptimizer.Core.Abstractions;

namespace DSOptimizer.Tests.Unit.Fakes;

/// <summary>
/// HTTP w pamięci (nigdy prawdziwa sieć): zapisuje każde wywołanie, odpowiedzi <see cref="GetText"/> bierze z kolejki
/// (pusta = <see cref="DefaultText"/>), a <see cref="Download"/> – jak prawdziwy downloader – najpierw sprawdza predykatem każdy skok
/// z <see cref="RedirectHops"/> (pierwsza odmowa = <see cref="WebFailure.RedirectRefused"/> z tym adresem), potem zgłasza kolejne
/// wartości <see cref="ByteSteps"/> i zwraca <see cref="FileResult"/> (domyślnie sukces z ostatnią liczbą bajtów).
/// </summary>
public sealed class FakeWebDownloader : IWebDownloader
{
    private readonly Queue<WebText> _texts = new();

    public List<(Uri Url, string Accept, TimeSpan Timeout)> TextCalls { get; } = new();

    public List<(Uri Url, long MaxBytes, string TargetPath, TimeSpan Timeout)> DownloadCalls { get; } = new();

    /// <summary>Wszystkie wywołania po kolei: <c>GetText &lt;url&gt;</c> albo <c>Download &lt;url&gt;</c>.</summary>
    public List<string> Calls { get; } = new();

    /// <summary>Wspólny ślad kolejności wywołań wszystkich fake'ów (opcjonalny).</summary>
    public List<string>? Trace { get; set; }

    public WebText DefaultText { get; set; } = new(WebFailure.Network, null, null, "brak sieci (fake)");

    public List<Uri> RedirectHops { get; } = new();

    /// <summary>Adresy skoków, o które downloader zapytał predykat, i jego odpowiedzi.</summary>
    public List<(Uri Url, bool Allowed)> RedirectChecks { get; } = new();

    public List<long> ByteSteps { get; } = new();

    /// <summary>Wynik pobrania; <c>null</c> = sukces 200 z ostatnią wartością <see cref="ByteSteps"/>.</summary>
    public WebFile? FileResult { get; set; }

    public Action? OnGetText { get; set; }

    /// <summary>Wołane na początku pobrania (np. wyjątek bezpiecznika testów).</summary>
    public Action? OnDownload { get; set; }

    public FakeWebDownloader EnqueueText(WebText result)
    {
        _texts.Enqueue(result);
        return this;
    }

    public void ClearTexts() => _texts.Clear();

    public static WebText Ok(string body) => new(WebFailure.None, 200, body, null);

    public static WebText Failure(WebFailure failure, int? status = null) => new(failure, status, null, "błąd (fake)");

    public WebText GetText(Uri url, string accept, TimeSpan timeout)
    {
        TextCalls.Add((url, accept, timeout));
        Calls.Add("GetText " + url);
        Trace?.Add("GetText " + url);
        OnGetText?.Invoke();
        return _texts.Count > 0 ? _texts.Dequeue() : DefaultText;
    }

    public WebFile Download(Uri url, Func<Uri, bool> allowRedirect, long maxBytes, string targetPath, TimeSpan timeout, Action<long>? onBytes)
    {
        DownloadCalls.Add((url, maxBytes, targetPath, timeout));
        Calls.Add("Download " + url);
        Trace?.Add("Download " + url + " " + maxBytes.ToString(System.Globalization.CultureInfo.InvariantCulture));
        OnDownload?.Invoke();
        foreach (var hop in RedirectHops)
        {
            var allowed = allowRedirect(hop);
            RedirectChecks.Add((hop, allowed));
            if (!allowed)
            {
                return new WebFile(WebFailure.RedirectRefused, 302, 0, hop, "przekierowanie odrzucone (fake)");
            }
        }

        foreach (var bytes in ByteSteps)
        {
            onBytes?.Invoke(bytes);
        }

        return FileResult ?? new WebFile(WebFailure.None, 200, ByteSteps.Count > 0 ? ByteSteps[ByteSteps.Count - 1] : 0, null, null);
    }
}
