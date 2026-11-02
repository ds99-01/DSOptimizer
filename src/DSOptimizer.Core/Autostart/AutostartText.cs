using System;
using DSOptimizer.Core.Localization;

namespace DSOptimizer.Core.Autostart;

/// <summary>Teksty modułu Autostart (w bieżącym języku, <see cref="L"/>).</summary>
public static class AutostartText
{
    /// <summary>Nazwa wiersza dla wartości Run o pustej nazwie.</summary>
    public static string NoName => L.T("(bez nazwy)");

    public static string NoMeasurement => L.T("Brak pomiaru Windows – w kolumnie pomiaru będzie „nie zmierzono”.");

    public static string EnvironmentUnreadable(string key, string error) => L.F("Nie udało się odczytać zmiennych konta z {0}: {1}", key, error);

    public static string SourceUnreadable(string where, string error) => L.F("Nie udało się odczytać {0}: {1}", where, error);

    public static string ValueSkipped(string key, string name) => L.F("Pominięto wartość {1} w {0}: nieobsługiwany typ.", key, name);

    public static string MeasurementFileSkipped(string file) => L.F("Pominięto plik pomiaru Windows {0}: błędny format.", file);

    public static string StoreUnreadable(string error) => L.F("Nie udało się odczytać aplikacji ze Sklepu: {0}", error);

    /// <summary>Opis pozycji spoza bazy.</summary>
    public static string NotAssessed => L.T("Bez oceny – brak tej pozycji w bazie programu.");

    /// <summary>Opis pozycji bez pliku (spoza bazy).</summary>
    public static string MissingFileSummary => L.T("Plik z polecenia nie istnieje, więc ta pozycja niczego nie uruchamia. Wyłączenie niczego nie psuje.");

    /// <summary>Podpowiedź kłódki pozycji chronionej.</summary>
    public static string ProtectedTooltip => L.T("Chroniona – DSOptimizer jej nie wyłącza");

    /// <summary>Powód „tylko do odczytu”; null, gdy pozycję można przełączać.</summary>
    public static string? Blocked(AutostartBlocker blocker) => blocker switch
    {
        AutostartBlocker.None => null,
        AutostartBlocker.Protected => ProtectedTooltip,
        AutostartBlocker.UserUncertain => L.T("Nie udało się ustalić zalogowanego użytkownika."),
        AutostartBlocker.UserHiveMissing => L.T("Ustawienia tego konta są niedostępne – uruchom DSOptimizer na zalogowanym koncie."),
        AutostartBlocker.Unreadable => L.T("Nie udało się odczytać stanu – DSOptimizer tej pozycji nie zmienia."),
        AutostartBlocker.UnknownFormat => L.T("Nieznany zapis stanu – DSOptimizer tej pozycji nie zmienia."),
        AutostartBlocker.TaskStateNotSupported => L.T("Stan ustawiony zasadami albo nieznany – DSOptimizer tej pozycji nie zmienia."),
        AutostartBlocker.WriteFromZeroNotMeasured => L.T("Tej pozycji nikt jeszcze nie przełączał – DSOptimizer jej nie zmienia, wyłączysz ją Menedżerem zadań."),
        AutostartBlocker.NoName => L.T("Wpis bez nazwy – DSOptimizer go nie zmienia."),
        _ => throw new ArgumentOutOfRangeException(nameof(blocker), blocker, null),
    };

    /// <summary>Zakres pozycji w wierszu: konto zalogowanego użytkownika, cały komputer albo aplikacja ze Sklepu.</summary>
    public static string Scope(AutostartScope scope) => scope switch
    {
        AutostartScope.User => L.T("to konto"),
        AutostartScope.Machine => L.T("wszystkie konta"),
        AutostartScope.Store => L.T("Sklep"),
        _ => throw new ArgumentOutOfRangeException(nameof(scope), scope, null),
    };
}
