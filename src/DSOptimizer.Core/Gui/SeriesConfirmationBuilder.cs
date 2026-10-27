using System;
using System.Collections.Generic;
using System.Linq;
using DSOptimizer.Core.Abstractions;
using DSOptimizer.Core.Model;
using DSOptimizer.Core.Operations;

namespace DSOptimizer.Core.Gui;

/// <summary>Składa <see cref="SeriesConfirmation"/> z definicji pozycji i podglądu zmian (teksty PL, bez WPF).</summary>
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
        var title = isSeries ? (operation == GuiOperation.Apply ? "Zastosuj zaznaczone" : "Cofnij zaznaczone") : operationName;
        var subtitle = isSeries ? GuiText.Count(tweaks.Count, "pozycja", "pozycje", "pozycji") : tweaks[0].Title;
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
                "Punkt przywracania",
                restorePointEnabled ? "Program spróbuje go utworzyć przed pierwszą zmianą" : "Nie – wyłączony w Ustawieniach"));
        }
        else if (operation == GuiOperation.Undo)
        {
            facts.Add(new SeriesFact("Punkt przywracania", "Nie – cofanie go nie tworzy"));
        }

        if (operation != GuiOperation.Execute)
        {
            var reboots = tweaks.Count(tweak => tweak.RequiresReboot);
            facts.Add(new SeriesFact(
                "Restart",
                reboots > 0 ? "Wymagany dla " + GuiText.Count(reboots, "pozycji", "pozycji", "pozycji") : "Niepotrzebny"));
        }

        if (operation == GuiOperation.Execute)
        {
            facts.Add(new SeriesFact("Cofanie", "Niemożliwe – akcji nie da się cofnąć"));
        }
        else if (operation == GuiOperation.Apply)
        {
            var irreversible = tweaks.Where(tweak => !tweak.Reversible).Select(tweak => tweak.Title).ToList();
            facts.Add(new SeriesFact(
                "Cofanie",
                irreversible.Count == 0 ? "Możliwe dla wszystkich, z historii zmian" : "Niemożliwe dla: " + string.Join("; ", irreversible)));
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
            notes.Add(string.IsNullOrWhiteSpace(userName) ? "Dotyczy bieżącego konta." : $"Dotyczy konta {userName}.");
        }

        if (tweak.RequiresReboot)
        {
            notes.Add("Zadziała po ponownym uruchomieniu.");
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
                        technical.Add(new SeriesFact("Polecenie", change.Description));
                        break;
                    case GuiOperation.Undo:
                        technical.Add(new SeriesFact("Gdzie", change.Description));
                        technical.Add(new SeriesFact("Zmiana", "przywrócenie stanu sprzed zmiany z historii"));
                        break;
                    default:
                        technical.Add(new SeriesFact("Gdzie", change.Description));
                        technical.Add(new SeriesFact("Zmiana", $"{change.Current} na {change.Target}"));
                        break;
                }
            }
        }
        catch (Exception ex)
        {
            log.Warn(Component, $"Podgląd zmian {tweak.Id} niedostępny: {ex.Message}");
            return new List<SeriesFact> { new("Szczegóły", $"niedostępne: {ex.Message}") };
        }

        return technical;
    }
}
