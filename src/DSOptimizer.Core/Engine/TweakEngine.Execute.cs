using System;
using System.Threading;
using DSOptimizer.Core.Abstractions;
using DSOptimizer.Core.Errors;
using DSOptimizer.Core.Localization;
using DSOptimizer.Core.Model;

namespace DSOptimizer.Core.Engine;

public sealed partial class TweakEngine
{
    /// <summary>Uruchamia akcję/panel (bez stanu i bez journala): <c>launch</c> przez launcher albo <c>script.apply</c> przez runner.</summary>
    public TweakResult Execute(TweakDefinition tweak, IProgress<string>? output = null, CancellationToken cancellationToken = default)
        => Execute(tweak, output, null, cancellationToken);

    /// <summary>
    /// To samo z postępem długiej akcji (<paramref name="progress"/> trafia tą samą referencją do handlera; skrypt i launch go nie używają).
    /// </summary>
    public TweakResult Execute(TweakDefinition tweak, IProgress<string>? output, IProgress<ActionProgress>? progress, CancellationToken cancellationToken)
    {
        if (tweak is null)
        {
            throw new ArgumentNullException(nameof(tweak));
        }

        if (tweak.Kind == TweakKind.Tweak)
        {
            return Finish(tweak, TweakOutcome.Refused, null, L.T("Execute dotyczy akcji i paneli – dla tweaków użyj Apply."));
        }

        if (tweak.Handler is { } handlerName)
        {
            return tweak.Launch is null && tweak.Script?.Apply is null
                ? RunHandler(tweak, handlerName, output, progress, cancellationToken)
                : Finish(tweak, TweakOutcome.Refused, null, L.T("Pozycja ma handler razem z launch albo script.apply."));
        }

        if (tweak.Launch is { } launch)
        {
            return tweak.Script?.Apply is null
                ? Launch(tweak, launch, cancellationToken)
                : Finish(tweak, TweakOutcome.Refused, null, L.T("Pozycja ma jednocześnie launch i script.apply."));
        }

        if (tweak.Script?.Apply is not { } body)
        {
            return Finish(tweak, TweakOutcome.Refused, null, L.T("Brak skryptu apply."));
        }

        _s.Log.Info(Component, $"Execute {tweak.Id}: start");
        // R18: linie zbieramy sami (synchronicznie), żeby przy wyjątku runnera (limit czasu) wyjście do chwili błędu trafiło do logu.
        var collector = new CollectingProgress(output);
        try
        {
            var result = _s.Scripts.Run(new ScriptRequest { Body = body, UserSid = _s.User.Sid, Timeout = tweak.Timeout }, collector, cancellationToken);
            // Akcja nie ma journala: jej stdout (np. co zmieniono i jaka była poprzednia wartość) jest jedynym śladem.
            var lines = new System.Collections.Generic.List<string>(result.Output);
            while (lines.Count > 0 && string.IsNullOrWhiteSpace(lines[lines.Count - 1]))
            {
                lines.RemoveAt(lines.Count - 1);
            }

            var stdout = lines.Count == 0 ? null : string.Join(Environment.NewLine, lines);
            if (result.ExitCode == 0)
            {
                return Finish(tweak, TweakOutcome.Executed, null, stdout, requiresReboot: tweak.RequiresReboot);
            }

            // Przy błędzie wynik niesie stderr (bez zalewania okna), a stdout (np. kopia konfiguracji sprzed zmiany) trafia do pliku logu.
            if (stdout is not null)
            {
                _s.Log.Info(Component, L.F("{0}: wyjście skryptu przed błędem:{1}{2}", tweak.Id, Environment.NewLine, stdout));
            }

            var errors = result.Errors.Count == 0 ? L.T("(brak stderr)") : string.Join(" | ", result.Errors);
            return Finish(tweak, TweakOutcome.Failed, null, L.F("Skrypt zakończył się kodem {0}: {1}", result.ExitCode, errors));
        }
        catch (Exception ex)
        {
            if (ex is not OperationException)
            {
                _s.Log.Fail(Component, L.F("Nieoczekiwany błąd wykonania {0}.", tweak.Id), ex);
            }

            var partial = collector.Text();
            if (partial is not null)
            {
                _s.Log.Info(Component, L.F("{0}: wyjście skryptu przed błędem:{1}{2}", tweak.Id, Environment.NewLine, partial));
            }

            return Finish(tweak, TweakOutcome.Failed, null, ex.Message);
        }
    }

    /// <summary>Akcja napisana w C#: handler z <see cref="EngineServices.Handlers"/> wykonuje robotę, silnik tylko pilnuje wyniku i logu.</summary>
    private TweakResult RunHandler(TweakDefinition tweak, string handlerName, IProgress<string>? output, IProgress<ActionProgress>? progress, CancellationToken cancellationToken)
    {
        _s.Log.Info(Component, $"Execute {tweak.Id}: handler {handlerName}");
        if (cancellationToken.IsCancellationRequested)
        {
            return Finish(tweak, TweakOutcome.Failed, null, L.T("Anulowano przed uruchomieniem."));
        }

        if (!_s.Handlers.TryGet(handlerName, out var handler))
        {
            return Finish(tweak, TweakOutcome.Refused, null, L.F("Brak implementacji akcji '{0}'.", handlerName));
        }

        ActionHandlerResult result;
        try
        {
            result = handler.Run(tweak, output, progress, cancellationToken);
        }
        catch (Exception ex)
        {
            if (ex is not OperationException)
            {
                _s.Log.Fail(Component, L.F("Nieoczekiwany błąd handlera {0} ({1}).", handlerName, tweak.Id), ex);
            }

            return Finish(tweak, TweakOutcome.Failed, null, ex.Message);
        }

        if (result.Outcome is not (TweakOutcome.Executed or TweakOutcome.Failed or TweakOutcome.Refused or TweakOutcome.NotConfirmed or TweakOutcome.NotSupported))
        {
            throw new ArgumentException($"Handler {handlerName} zwrócił niedozwolony wynik {result.Outcome}.");
        }

        return Finish(tweak, result.Outcome, null, result.Message, result.Warnings, result.NotRestored, result.Outcome == TweakOutcome.Executed && (result.RequiresReboot || tweak.RequiresReboot));
    }

    /// <summary>Zbiera linie wyjścia skryptu synchronicznie i przekazuje je dalej (bez <c>Progress&lt;T&gt;</c>, który raportuje asynchronicznie).</summary>
    private sealed class CollectingProgress : IProgress<string>
    {
        private readonly IProgress<string>? _inner;
        private readonly System.Collections.Generic.List<string> _lines = new();

        public CollectingProgress(IProgress<string>? inner) => _inner = inner;

        public void Report(string value)
        {
            lock (_lines)
            {
                _lines.Add(value);
            }

            _inner?.Report(value);
        }

        /// <summary>Zebrane linie bez pustych na końcu albo <c>null</c>, gdy nic nie przyszło.</summary>
        public string? Text()
        {
            lock (_lines)
            {
                var lines = new System.Collections.Generic.List<string>(_lines);
                while (lines.Count > 0 && string.IsNullOrWhiteSpace(lines[lines.Count - 1]))
                {
                    lines.RemoveAt(lines.Count - 1);
                }

                return lines.Count == 0 ? null : string.Join(Environment.NewLine, lines);
            }
        }
    }

    /// <summary>Okno ma przeżyć Execute, więc proces startuje poza Job Objectem skryptów i nikt na niego nie czeka.</summary>
    private TweakResult Launch(TweakDefinition tweak, LaunchSpec launch, CancellationToken cancellationToken)
    {
        _s.Log.Info(Component, $"Execute {tweak.Id}: launch {launch.File}");
        if (cancellationToken.IsCancellationRequested)
        {
            return Finish(tweak, TweakOutcome.Failed, null, L.T("Anulowano przed uruchomieniem."));
        }

        try
        {
            _s.Launcher.Launch(launch);
            return Finish(tweak, TweakOutcome.Executed, null, L.F("Uruchomiono {0}", launch.File));
        }
        catch (Exception ex)
        {
            if (ex is not OperationException)
            {
                _s.Log.Fail(Component, L.F("Nieoczekiwany błąd uruchamiania {0}.", tweak.Id), ex);
            }

            return Finish(tweak, TweakOutcome.Failed, null, ex.Message);
        }
    }
}
