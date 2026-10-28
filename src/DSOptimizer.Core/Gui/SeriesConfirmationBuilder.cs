using System;
using System.Collections.Generic;
using System.Linq;
using DSOptimizer.Core.Abstractions;
using DSOptimizer.Core.Localization;
using DSOptimizer.Core.Model;
using DSOptimizer.Core.Operations;

namespace DSOptimizer.Core.Gui;

/// <summary>Składa <see cref="SeriesConfirmation"/> z definicji pozycji i podglądu zmian (teksty przez <see cref="L"/>, bez WPF).</summary>
internal static class SeriesConfirmationBuilder
{
    private const string Component = "Gui";

    /// <param name="operation">Apply, Undo albo Execute (Execute tylko dla pojedynczej pozycji).</param>
    /// <param name="tweaks">Pozycje okna (dla pojedynczej dokładnie jedna).</param>
    /// <param name="isSeries">True = nagłówki serii („Zastosuj zaznaczone”), false = pojedyncza pozycja.</param>
    /// <param name="restorePointEnabled">Ustawienie „Utwórz punkt przywracania” (liczy się tylko dla Apply).</param>
    /// <param name="userName">Nazwa konta dla pozycji scope=user; null/biały znak = „bieżące konto”.</param>
    /// <param name="preview">Podgląd zmian pozycji (czyta rejestr i usługi); wyjątek daje wiersz „Szczegóły”.</param>
    /// <param name="log">Dziennik, do którego trafia ostrzeżenie o niedostępnym podglądzie.</param>
    public static SeriesConfirmation Build(
        GuiOperation operation,
        IReadOnlyList<TweakDefinition> tweaks,
        bool isSeries,
        bool restorePointEnabled,
        string? userName,
        Func<TweakDefinition, IReadOnlyList<PlannedChange>> preview,
        ILog log)
    {
        var createsRestorePoint = operation == GuiOperation.Apply && restorePointEnabled;
        var operationName = GuiText.OperationName(operation);
        var title = isSeries ? (operation == GuiOperation.Apply ? L.T("Zastosuj zaznaczone") : L.T("Cofnij zaznaczone")) : operationName;
        var subtitle = isSeries ? L.P("{0} pozycja", "{0} pozycje", "{0} pozycji", tweaks.Count) : tweaks[0].Title;
        var primary = isSeries ? $"{operationName} ({tweaks.Count})" : operationName;
        var items = tweaks.Select(tweak => BuildItem(tweak, operation, userName, preview, log)).ToList();
        return new SeriesConfirmation(operation, title, subtitle, BuildFacts(operation, tweaks, restorePointEnabled), items, primary, createsRestorePoint);
    }

    private static List<SeriesFact> BuildFacts(GuiOperation operation, IReadOnlyList<TweakDefinition> tweaks, bool restorePointEnabled)
    {
        var facts = new List<SeriesFact>();
        if (operation == GuiOperation.Apply)
        {
            facts.Add(new SeriesFact(
                L.T("Punkt przywracania"),
                restorePointEnabled ? L.T("Program spróbuje go utworzyć przed pierwszą zmianą") : L.T("Nie – wyłączony w Ustawieniach")));
        }
        else if (operation == GuiOperation.Undo)
        {
            facts.Add(new SeriesFact(L.T("Punkt przywracania"), L.T("Nie – cofanie go nie tworzy")));
        }

        if (operation != GuiOperation.Execute)
        {
            var reboots = tweaks.Count(tweak => tweak.RequiresReboot);
            // Polski tekst dla 1 i dla 2+ jest ten sam („pozycji”), więc 1 ma własny klucz – po angielsku „1 item”, nie „1 items”.
            var rebootText = reboots switch
            {
                0 => L.T("Niepotrzebny"),
                1 => L.T("Wymagany dla 1 pozycji"),
                _ => L.P("Wymagany dla {0} pozycji", "Wymagany dla {0} pozycji", "Wymagany dla {0} pozycji", reboots),
            };
            facts.Add(new SeriesFact(L.T("Restart"), rebootText));
        }

        if (operation == GuiOperation.Execute)
        {
            facts.Add(new SeriesFact(L.T("Cofanie"), L.T("Niemożliwe – akcji nie da się cofnąć")));
        }
        else if (operation == GuiOperation.Apply)
        {
            var irreversible = tweaks.Where(tweak => !tweak.Reversible).Select(tweak => tweak.Title).ToList();
            facts.Add(new SeriesFact(
                L.T("Cofanie"),
                irreversible.Count == 0 ? L.T("Możliwe dla wszystkich, z historii zmian") : L.F("Niemożliwe dla: {0}", string.Join("; ", irreversible))));
        }

        return facts;
    }

    private static SeriesItem BuildItem(
        TweakDefinition tweak,
        GuiOperation operation,
        string? userName,
        Func<TweakDefinition, IReadOnlyList<PlannedChange>> preview,
        ILog log)
    {
        var notes = new List<string>();
        if (tweak.Scope == TweakScope.User)
        {
            notes.Add(string.IsNullOrWhiteSpace(userName) ? L.T("Dotyczy bieżącego konta.") : L.F("Dotyczy konta {0}.", userName));
        }

        if (tweak.RequiresReboot)
        {
            notes.Add(L.T("Zadziała po ponownym uruchomieniu."));
        }

        return new SeriesItem(
            tweak.Id,
            tweak.Title,
            tweak.Risk,
            tweak.Summary,
            notes,
            tweak.Warnings.ToList(),
            BuildTechnical(tweak, operation, preview, log));
    }

    private static List<SeriesFact> BuildTechnical(
        TweakDefinition tweak,
        GuiOperation operation,
        Func<TweakDefinition, IReadOnlyList<PlannedChange>> preview,
        ILog log)
    {
        var technical = new List<SeriesFact>();
        try
        {
            foreach (var change in preview(tweak))
            {
                switch (operation)
                {
                    case GuiOperation.Execute:
                        technical.Add(new SeriesFact(L.T("Polecenie"), change.Description));
                        break;
                    case GuiOperation.Undo:
                        technical.Add(new SeriesFact(L.T("Gdzie"), change.Description));
                        technical.Add(new SeriesFact(L.T("Zmiana"), L.T("przywrócenie stanu sprzed zmiany z historii")));
                        break;
                    default:
                        technical.Add(new SeriesFact(L.T("Gdzie"), change.Description));
                        technical.Add(new SeriesFact(L.T("Zmiana"), L.F("{0} na {1}", change.Current, change.Target)));
                        break;
                }
            }
        }
        catch (Exception ex)
        {
            log.Warn(Component, L.F("Podgląd zmian {0} niedostępny: {1}", tweak.Id, ex.Message));
            return new List<SeriesFact> { new(L.T("Szczegóły"), L.F("niedostępne: {0}", ex.Message)) };
        }

        return technical;
    }
}
