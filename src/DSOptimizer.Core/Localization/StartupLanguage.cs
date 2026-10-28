using System;
using DSOptimizer.Core.Abstractions;
using DSOptimizer.Core.Settings;

namespace DSOptimizer.Core.Localization;

/// <summary>Wynik wyboru języka na starcie: ustawienia do dalszego użycia, język i opcjonalne ostrzeżenie.</summary>
public sealed record StartupLanguageResult(AppSettings Settings, UiLanguage Language, string? Warning);

/// <summary>
/// Wybór języka interfejsu przy starcie programu. Zapisany język = bez pytania i bez zapisu.
/// Brak wyboru = pytanie z wykrytym językiem jako propozycją; zamknięcie okna bez odpowiedzi daje język wykryty.
/// Błąd zapisu nie blokuje startu: wynik niesie ostrzeżenie, a okno wyboru wróci przy następnym uruchomieniu.
/// </summary>
public static class StartupLanguage
{
    public static StartupLanguageResult Choose(
        AppSettings loaded,
        UiLanguage detected,
        Func<UiLanguage, UiLanguage?> ask,
        ISettingsStore store)
    {
        if (loaded is null)
        {
            throw new ArgumentNullException(nameof(loaded));
        }

        if (ask is null)
        {
            throw new ArgumentNullException(nameof(ask));
        }

        if (store is null)
        {
            throw new ArgumentNullException(nameof(store));
        }

        if (loaded.Language is { } saved)
        {
            return new StartupLanguageResult(loaded, saved, null);
        }

        var chosen = ask(detected) ?? detected;
        var updated = loaded with { Language = chosen };
        try
        {
            store.Save(updated);
        }
        catch (SettingsStoreException ex)
        {
            // Ostrzeżenie trafia do programu działającego już w wybranym języku.
            using (L.Use(chosen))
            {
                var warning = L.F("Nie udało się zapisać wybranego języka: {0} Okno wyboru pokaże się przy następnym uruchomieniu.", ex.Message);
                return new StartupLanguageResult(updated, chosen, warning);
            }
        }

        return new StartupLanguageResult(updated, chosen, null);
    }
}
