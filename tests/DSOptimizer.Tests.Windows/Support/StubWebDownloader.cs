using System;
using System.Threading;
using DSOptimizer.Core.Abstractions;

namespace DSOptimizer.Tests.Windows.Support;

/// <summary>
/// Atrapa <see cref="IWebDownloader"/> bez sieci: <see cref="GetText"/> zwraca podany wynik, opcjonalnie dopiero po otwarciu
/// bramki z konstruktora (symulacja wiszącej sieci); <see cref="Download"/> zawsze rzuca, bo sprawdzanie wersji nie pobiera plików.
/// </summary>
public sealed class StubWebDownloader : IWebDownloader
{
    private readonly WebText _text;
    private readonly ManualResetEventSlim? _gate;
    private int _calls;

    public StubWebDownloader(WebText text, ManualResetEventSlim? gate = null)
    {
        _text = text;
        _gate = gate;
    }

    /// <summary>Ustawiane, gdy wywołanie <see cref="GetText"/> już trwa (przed czekaniem na bramkę).</summary>
    public ManualResetEventSlim Entered { get; } = new(false);

    public int Calls => Volatile.Read(ref _calls);

    public static WebText Release(string tag) =>
        new(WebFailure.None, 200, "{\"tag_name\":\"" + tag + "\",\"prerelease\":false,\"draft\":false}", null);

    public WebText GetText(Uri url, string accept, TimeSpan timeout)
    {
        Interlocked.Increment(ref _calls);
        Entered.Set();
        _gate?.Wait();
        return _text;
    }

    public WebFile Download(Uri url, Func<Uri, bool> allowRedirect, long maxBytes, string targetPath, TimeSpan timeout, Action<long>? onBytes) =>
        throw new InvalidOperationException("Sprawdzanie wersji niczego nie pobiera.");
}
