using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using DSOptimizer.Core.Hardware;

namespace DSOptimizer.Core.Localization;

/// <summary>
/// Tłumaczenie tekstów interfejsu. Teksty źródłowe w kodzie są polskie; <see cref="T"/> zwraca angielski odpowiednik z tabeli
/// albo (gdy tłumaczenia brak) tekst polski, nigdy nie rzuca z powodu tabeli. Język procesu ustawia <see cref="SetDefault"/>,
/// a <see cref="Use"/> nadpisuje go tylko w bieżącym przepływie (<see cref="AsyncLocal{T}"/>).
/// </summary>
public static class L
{
    private const string ResourcePrefix = "DSOptimizer.Core.Translations.";

    private static readonly AsyncLocal<UiLanguage?> LanguageOverride = new();

    private static readonly AsyncLocal<TranslationTable?> TableOverride = new();

    private static readonly object Gate = new();

    private static int defaultLanguage = (int)UiLanguage.Polish;

    private static TranslationTable? builtIn;

    private static string? loadWarning;

    /// <summary>Język bieżącego przepływu: nadpisanie z <see cref="Use"/> albo domyślny język procesu.</summary>
    public static UiLanguage Current => LanguageOverride.Value ?? (UiLanguage)Volatile.Read(ref defaultLanguage);

    /// <summary>Kultura formatowania liczb i dat dla bieżącego języka: pl-PL albo en-GB (instancje tylko do odczytu).</summary>
    public static CultureInfo Culture => CultureInfo.GetCultureInfo(Current == UiLanguage.English ? "en-GB" : "pl-PL");

    /// <summary>
    /// Wzorzec daty, w obu językach jawny: dane NLS hosta bywają inne niż oczekiwane (dla pl-PL standardowy "d" daje
    /// <c>d.MM.yyyy</c>), więc wynik nie może zależeć od systemu. Ukośnik jest w apostrofach, by separator daty kultury go nie zmienił.
    /// </summary>
    public static string DatePattern => Current == UiLanguage.English ? "dd'/'MM'/'yyyy" : "dd.MM.yyyy";

    /// <summary>Wzorzec daty z godziną (bez sekund), jawny w obu językach.</summary>
    public static string DateTimePattern => Current == UiLanguage.English ? "dd'/'MM'/'yyyy HH:mm" : "dd.MM.yyyy HH:mm";

    /// <summary>Wzorzec daty z godziną i sekundami, jawny w obu językach.</summary>
    public static string DateTimeSecondsPattern => Current == UiLanguage.English ? "dd'/'MM'/'yyyy HH:mm:ss" : "dd.MM.yyyy HH:mm:ss";

    /// <summary>Komunikat, jeśli wbudowane tłumaczenia nie dało się wczytać (wtedy tabela jest pusta); inaczej <c>null</c>.</summary>
    public static string? LoadWarning
    {
        get
        {
            _ = Table();
            return Volatile.Read(ref loadWarning);
        }
    }

    /// <summary>Ustawia domyślny język całego procesu.</summary>
    public static void SetDefault(UiLanguage language)
    {
        Volatile.Write(ref defaultLanguage, (int)language);
    }

    /// <summary>Nadpisuje język w bieżącym przepływie do czasu <see cref="IDisposable.Dispose"/>; zagnieżdżenie przywraca poprzedni.</summary>
    public static IDisposable Use(UiLanguage language)
    {
        var previous = LanguageOverride.Value;
        LanguageOverride.Value = language;
        return new Restore(() => LanguageOverride.Value = previous);
    }

    /// <summary>Tłumaczy polski tekst na bieżący język; bez tłumaczenia zwraca tekst polski.</summary>
    public static string T(string polish)
    {
        if (polish is null)
        {
            throw new ArgumentNullException(nameof(polish));
        }

        return Table().Find(Current, polish) ?? polish;
    }

    /// <summary>Formatuje przetłumaczony format w kulturze bieżącego języka.</summary>
    public static string F(string polishFormat, params object?[] args) =>
        string.Format(Culture, T(polishFormat), args);

    /// <summary>
    /// Liczba z odmianą: każda forma zawiera <c>{0}</c> (= <paramref name="count"/> jako zwykłe <c>{0}</c> w kulturze <see cref="Culture"/>, bez separatora tysięcy – „1000 pozycji”, „1000 items”).
    /// Po polsku forma z <see cref="PolishPlural.Choose"/>; po angielsku 1 → tłumaczenie <paramref name="one"/>, inaczej tłumaczenie
    /// <paramref name="many"/> (<paramref name="few"/> nie jest kluczem angielskim). Brak tłumaczenia wybranej formy = polska forma
    /// dla tej liczby, nigdy błędna polska odmiana.
    /// </summary>
    public static string P(string one, string few, string many, int count)
    {
        RequirePlaceholder(one, nameof(one));
        RequirePlaceholder(few, nameof(few));
        RequirePlaceholder(many, nameof(many));
        var form = Current == UiLanguage.English
            ? Table().Find(UiLanguage.English, count == 1 ? one : many)
            : null;
        return string.Format(Culture, form ?? PolishPlural.Choose(count, one, few, many), count);
    }

    /// <summary>
    /// Teksty w każdym języku interfejsu: najpierw wszystkie w bieżącym języku, potem w pozostałych (kolejność <see cref="UiLanguage"/>),
    /// bez powtórzeń. Do rozpoznawania własnych tekstów zapisanych wcześniej (journal) w innym języku niż bieżący.
    /// </summary>
    internal static IReadOnlyList<string> AllLanguages(params Func<string>[] texts)
    {
        if (texts is null)
        {
            throw new ArgumentNullException(nameof(texts));
        }

        var current = Current;
        var languages = new[] { current }.Concat(((UiLanguage[])Enum.GetValues(typeof(UiLanguage))).Where(l => l != current));
        var result = new List<string>(texts.Length * 2);
        foreach (var language in languages)
        {
            using (Use(language))
            {
                foreach (var text in texts)
                {
                    var value = text();
                    if (!result.Contains(value, StringComparer.Ordinal))
                    {
                        result.Add(value);
                    }
                }
            }
        }

        return result;
    }

    /// <summary>Wbudowane pliki tłumaczeń (zasoby <c>DSOptimizer.Core.Translations.*.json</c> zestawu Core).</summary>
    public static IReadOnlyList<TranslationSource> BuiltInSources()
    {
        var assembly = typeof(L).Assembly;
        var sources = new List<TranslationSource>();
        foreach (var resource in assembly.GetManifestResourceNames()
                     .Where(n => n.StartsWith(ResourcePrefix, StringComparison.Ordinal))
                     .OrderBy(n => n, StringComparer.Ordinal))
        {
            using var stream = assembly.GetManifestResourceStream(resource)
                ?? throw new InvalidOperationException("Brak zasobu " + resource);
            using var reader = new StreamReader(stream, System.Text.Encoding.UTF8);
            sources.Add(new TranslationSource(resource.Substring(ResourcePrefix.Length), reader.ReadToEnd()));
        }

        return sources;
    }

    /// <summary>Szew testowy: podmienia tabelę w bieżącym przepływie do czasu <see cref="IDisposable.Dispose"/>.</summary>
    internal static IDisposable UseTable(TranslationTable table)
    {
        if (table is null)
        {
            throw new ArgumentNullException(nameof(table));
        }

        var previous = TableOverride.Value;
        TableOverride.Value = table;
        return new Restore(() => TableOverride.Value = previous);
    }

    private static void RequirePlaceholder(string form, string name)
    {
        if (form is null)
        {
            throw new ArgumentNullException(name);
        }

        if (form.IndexOf("{0}", StringComparison.Ordinal) < 0)
        {
            throw new ArgumentException("Forma liczebnika musi zawierać {0}: " + form, name);
        }
    }

    private static TranslationTable Table()
    {
        var overridden = TableOverride.Value;
        if (overridden is not null)
        {
            return overridden;
        }

        var loaded = Volatile.Read(ref builtIn);
        if (loaded is not null)
        {
            return loaded;
        }

        lock (Gate)
        {
            if (builtIn is null)
            {
                builtIn = LoadBuiltIn(BuiltInSources, out loadWarning);
            }

            return builtIn;
        }
    }

    /// <summary>Wczytuje tabelę ze źródeł; każdy niekrytyczny wyjątek daje pustą tabelę i ostrzeżenie (T nigdy nie rzuca).</summary>
    internal static TranslationTable LoadBuiltIn(Func<IReadOnlyList<TranslationSource>> sources, out string? warning)
    {
        try
        {
            warning = null;
            return TranslationTable.Load(sources());
        }
        catch (Exception ex) when (ex is not (OutOfMemoryException or StackOverflowException or ThreadAbortException or AccessViolationException))
        {
            warning = "Nie wczytano tłumaczeń: " + ex.Message;
            return TranslationTable.Empty;
        }
    }

    private sealed class Restore : IDisposable
    {
        private Action? action;

        public Restore(Action action)
        {
            this.action = action;
        }

        public void Dispose()
        {
            var current = Interlocked.Exchange(ref action, null);
            current?.Invoke();
        }
    }
}
