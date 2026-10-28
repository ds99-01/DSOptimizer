using System;
using System.Collections.Generic;
using DSOptimizer.Core.Localization;

namespace DSOptimizer.App.Gui;

/// <summary>
/// Komunikaty startu programu i nieobsłużonych wyjątków (<c>Program</c>): okna <c>MessageBox</c> i linie logu. Każde zdanie jest
/// jednym tekstem do tłumaczenia; nowa linia (<see cref="Environment.NewLine"/>) wchodzi jako wartość.
/// </summary>
internal static class StartupText
{
    public static string WarningTitle => L.T("DSOptimizer – uwaga");

    public static string ErrorTitle => L.T("DSOptimizer – błąd");

    public static string CatalogInvalid(IEnumerable<string> errors)
        => L.F("Wbudowany katalog jest nieprawidłowy:{0}{1}", Environment.NewLine, string.Join(Environment.NewLine, errors));

    public static string WorkingFolderFailed(string reason) => L.F("Nie można przygotować katalogu roboczego DSOptimizer: {0}", reason);

    public static string SettingsReadFailed(string reason) => L.F("Nie można odczytać ustawień: {0} Używam ustawień domyślnych.", reason);

    public static string AppsListInvalid(string reason) => L.F("Wbudowana lista aplikacji jest nieprawidłowa:{0}{1}", Environment.NewLine, reason);

    public static string InstallListInvalid(string reason) => L.F("Wbudowana lista instalacji jest nieprawidłowa:{0}{1}", Environment.NewLine, reason);

    public static string WindowExceptionLog => L.T("Nieobsłużony wyjątek w oknie.");

    public static string FatalExceptionLog(bool isTerminating)
        => isTerminating ? L.T("Nieobsłużony wyjątek – program zostanie zamknięty.") : L.T("Nieobsłużony wyjątek w tle.");

    public static string WindowError(string message)
        => L.F("Wystąpił nieoczekiwany błąd okna:{0}{1}{0}{0}Szczegóły zapisano w pliku logu. Okno może działać niepoprawnie – zamknij program po zakończeniu bieżącej operacji i uruchom go ponownie.",
            Environment.NewLine,
            message);

    public static string FatalError(string? message)
        => L.F("Nieoczekiwany błąd – program zostanie zamknięty:{0}{1}{0}{0}Szczegóły zapisano w pliku logu. Sprawdź stan zmienionych pozycji po ponownym uruchomieniu.",
            Environment.NewLine,
            message ?? string.Empty);
}
