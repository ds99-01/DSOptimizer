using DSOptimizer.Core.Localization;

namespace DSOptimizer.Core.Settings;

public sealed record AppSettings
{
    public const int SchemaVersion = 1;

    public static AppSettings Default { get; } = new();

    public bool CreateRestorePoint { get; init; } = true;

    public ThemePreference Theme { get; init; } = ThemePreference.System;

    /// <summary>Czy program przy starcie sprawdza, czy jest nowsza wersja.</summary>
    public bool CheckUpdatesAtStart { get; init; } = true;

    /// <summary>Wybrany język interfejsu; <c>null</c> = jeszcze nie wybrano.</summary>
    public UiLanguage? Language { get; init; }
}
