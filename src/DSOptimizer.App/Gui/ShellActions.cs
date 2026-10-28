using System;
using System.Threading.Tasks;
using DSOptimizer.Core;
using DSOptimizer.Core.Abstractions;
using DSOptimizer.Core.Gui;
using DSOptimizer.Core.Localization;

namespace DSOptimizer.App.Gui;

/// <summary>
/// Akcje powłoki z okna: „pokaż plik w Eksploratorze”, „otwórz stronę repozytorium” i „otwórz dozwolony adres”. Sprawdzenie powłoki konta (WMI) trwa 1–3 s,
/// więc revealer i opener idą przez <see cref="Task.Run(System.Action)"/>, a schowek i dziennik wracają na wątek UI.
/// Odmowa (każdy wyjątek z revealera/openera, zwykle OperationException) = ostrzeżenie w dzienniku i kopia do schowka;
/// akcja powłoki nigdy nie przewraca okna. W trakcie jednej akcji kolejne wywołania są ignorowane (podwójny klik).
/// Wołać z wątku UI (kontekst synchronizacji WPF).
/// </summary>
public sealed class ShellActions
{
    private const string Component = "Gui";

    private readonly IFileRevealer _revealer;
    private readonly IUrlOpener? _urlOpener;
    private readonly ILog _log;
    private bool _busy;

    /// <param name="urlOpener">Null = wariant „tylko kopiowanie”: adres trafia do schowka, przeglądarka nie startuje.</param>
    public ShellActions(IFileRevealer revealer, IUrlOpener? urlOpener, ILog log)
    {
        _revealer = revealer ?? throw new ArgumentNullException(nameof(revealer));
        _urlOpener = urlOpener;
        _log = log ?? throw new ArgumentNullException(nameof(log));
    }

    /// <summary>Eksplorator z zaznaczonym plikiem; odmowa = ostrzeżenie w dzienniku i ścieżka w schowku.</summary>
    public async Task RevealAsync(string path)
    {
        if (path is null)
        {
            throw new ArgumentNullException(nameof(path));
        }

        if (_busy)
        {
            return;
        }

        _busy = true;
        try
        {
            var refusal = await RunAsync(() => _revealer.Reveal(path)).ConfigureAwait(true);
            if (refusal is not null)
            {
                var copied = Copy(path, L.T("Nie udało się skopiować ścieżki do schowka."));
                _log.Warn(Component, LogLineText.RevealFailed(refusal, copied));
            }
        }
        finally
        {
            _busy = false;
        }
    }

    /// <summary>Strona repozytorium programu w przeglądarce; bez openera tylko kopiowanie adresu, odmowa = ostrzeżenie i kopia adresu.</summary>
    public Task OpenRepositoryPageAsync() => OpenUrlAsync(ProductInfo.RepositoryUrl);

    /// <summary>
    /// Dowolny adres w przeglądarce; opener sam sprawdza go w <see cref="ExternalLinkPolicy"/>, więc adres spoza listy kończy się odmową
    /// (ostrzeżenie i kopia adresu). Bez openera tylko kopiowanie adresu.
    /// </summary>
    public async Task OpenUrlAsync(string url)
    {
        if (url is null)
        {
            throw new ArgumentNullException(nameof(url));
        }

        if (_busy)
        {
            return;
        }

        _busy = true;
        try
        {
            var opener = _urlOpener;
            // Przełącznik awaryjny: bez otwieracza adres tylko trafia do schowka. Zostawiony celowo, nieużywany, dopóki pomiar na VM się utrzymuje.
            if (opener is null)
            {
                if (Copy(url, L.T("Nie udało się skopiować adresu do schowka.")))
                {
                    _log.Info(Component, GuiText.UrlCopied(url));
                }

                return;
            }

            var refusal = await RunAsync(() => opener.Open(url)).ConfigureAwait(true);
            if (refusal is not null)
            {
                var copied = Copy(url, L.T("Nie udało się skopiować adresu do schowka."));
                _log.Warn(Component, GuiText.OpenUrlFailed(refusal, copied));
            }
        }
        finally
        {
            _busy = false;
        }
    }

    /// <summary>Akcja poza wątkiem UI; zwraca powód odmowy albo null, gdy się udała.</summary>
    private static async Task<string?> RunAsync(Action action)
    {
        try
        {
            await Task.Run(action).ConfigureAwait(true);
            return null;
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }

    /// <summary>Schowek bywa zajęty przez inny proces (CLIPBRD_E_CANT_OPEN): ClipboardCopy sprawdza odczytem zwrotnym.</summary>
    private bool Copy(string text, string failure)
    {
        var result = ClipboardCopy.Copy(WpfClipboardText.Instance, text);
        if (!result.Copied)
        {
            _log.Fail(Component, failure, result.Error);
        }
        else if (result.Error is not null)
        {
            _log.Info(Component, ClipboardCopy.RecoveredMessage(result.Error));
        }

        return result.Copied;
    }
}
