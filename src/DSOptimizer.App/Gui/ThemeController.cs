using System;
using System.Windows;
using DSOptimizer.Core.Abstractions;
using DSOptimizer.Core.Gui;
using DSOptimizer.Core.Localization;
using DSOptimizer.Core.Settings;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace DSOptimizer.App.Gui;

/// <summary>
/// Motyw treści wg ustawienia (Systemowy / Jasny / Ciemny), zawsze bez Mica; pasek marki i menu mają stałe kolory z Tokens.xaml.
/// Po każdej zmianie motywu – z Ustawień i ze śledzenia motywu Windows (<see cref="ApplicationThemeManager.Changed"/>) – akcent treści
/// dostaje kolory motywu z <see cref="BrandPalette"/> (zasoby <see cref="BrandAccentResources"/> i akcent WPF-UI, nie systemowy).
/// Wołać z wątku UI.
/// Błąd WPF-UI jest łapany i logowany: motyw zostaje poprzedni, okno działa dalej.
/// </summary>
public sealed class ThemeController
{
    private const string Component = "Gui";

    private readonly ILog _log;
    private ThemePreference _preference = ThemePreference.System;
    private Window? _window;
    private bool _watching;

    public ThemeController(ILog log)
    {
        _log = log ?? throw new ArgumentNullException(nameof(log));

        // Zmiana motywu Windows przy śledzeniu (SystemThemeWatcher) przełącza motyw WPF-UI bez udziału tej klasy – akcent idzie za nim.
        ApplicationThemeManager.Changed += (theme, _) => OnApplicationThemeChanged(theme);
    }

    /// <summary>
    /// Przed utworzeniem okna. <see cref="ThemePreference.System"/> = <c>ApplySystemTheme(false)</c>: dotyka Application.MainWindow
    /// (Mica), ale okna jeszcze nie ma. Jasny/Ciemny = <c>Apply(…, WindowBackdropType.None, false)</c>.
    /// </summary>
    public void ApplyAtStartup(ThemePreference preference)
    {
        _preference = preference;
        try
        {
            if (preference == ThemePreference.System)
            {
                ApplicationThemeManager.ApplySystemTheme(false);
            }
            else
            {
                ApplicationThemeManager.Apply(ToApplicationTheme(preference), WindowBackdropType.None, false);
            }

            ApplyBrandAccent(ApplicationThemeManager.GetAppTheme());
        }
        catch (Exception ex)
        {
            // Motyw startowy z App.xaml (jasny) zostaje – okno działa dalej.
            _log.Fail(Component, L.T("Nie udało się ustawić motywu."), ex);
        }
    }

    /// <summary>
    /// Po <c>Loaded</c> okna głównego. Przy <see cref="ThemePreference.System"/> śledzi motyw Windows: Watch na załadowanym oknie
    /// nie woła ApplySystemTheme (to włączyłoby Mica na MainWindow), a zmiany systemu stosuje z <see cref="WindowBackdropType.None"/>.
    /// </summary>
    public void Attach(Window window)
    {
        _window = window ?? throw new ArgumentNullException(nameof(window));
        if (_preference == ThemePreference.System)
        {
            try
            {
                Watch(window);
            }
            catch (Exception ex)
            {
                // Bez śledzenia motyw zostaje taki, jak przy starcie – okno działa dalej.
                _log.Fail(Component, L.T("Nie udało się włączyć śledzenia motywu systemu."), ex);
            }
        }
    }

    /// <summary>
    /// Zmiana z Ustawień, od razu. Systemowy: motyw systemu przez <c>Apply(…, None, false)</c> (nie ApplySystemTheme – patrz
    /// <see cref="ApplyAtStartup"/>) i ponowne śledzenie. Jasny/Ciemny: koniec śledzenia, potem <c>Apply(…, None, false)</c>.
    /// </summary>
    public void Change(ThemePreference preference)
    {
        try
        {
            if (preference == ThemePreference.System)
            {
                SystemThemeManager.UpdateSystemThemeCache();
                ApplicationThemeManager.Apply(FromSystemTheme(ApplicationThemeManager.GetSystemTheme()), WindowBackdropType.None, false);
                if (_window is { IsLoaded: true } window)
                {
                    Watch(window);
                }
            }
            else
            {
                if (_watching && _window is { IsLoaded: true } window)
                {
                    SystemThemeWatcher.UnWatch(window);
                    _watching = false;
                }

                ApplicationThemeManager.Apply(ToApplicationTheme(preference), WindowBackdropType.None, false);
            }

            ApplyBrandAccent(ApplicationThemeManager.GetAppTheme());
            _preference = preference;
        }
        catch (Exception ex)
        {
            _log.Fail(Component, L.T("Nie udało się zmienić motywu."), ex);
        }
    }

    /// <summary>
    /// Odwzorowanie motywu Windows na motyw aplikacji jak w WPF-UI 4.3.0 <c>ApplicationThemeManager.ApplySystemTheme(bool)</c>:
    /// https://github.com/lepoco/wpfui/blob/4.3.0/src/Wpf.Ui/Appearance/ApplicationThemeManager.cs
    /// (Dark, CapturedMotion, Glow → Dark; HC1, HC2, HCBlack, HCWhite → HighContrast; reszta → Light).
    /// </summary>
    internal static ApplicationTheme FromSystemTheme(SystemTheme systemTheme) => systemTheme switch
    {
        SystemTheme.Dark or SystemTheme.CapturedMotion or SystemTheme.Glow => ApplicationTheme.Dark,
        SystemTheme.HC1 or SystemTheme.HC2 or SystemTheme.HCBlack or SystemTheme.HCWhite => ApplicationTheme.HighContrast,
        _ => ApplicationTheme.Light,
    };

    private static ApplicationTheme ToApplicationTheme(ThemePreference preference)
        => preference == ThemePreference.Dark ? ApplicationTheme.Dark : ApplicationTheme.Light;

    /// <summary>Jedno śledzenie na okno (drugi Watch tego samego okna dodałby drugi hak WndProc).</summary>
    private void Watch(Window window)
    {
        if (_watching)
        {
            return;
        }

        SystemThemeWatcher.Watch(window, WindowBackdropType.None, false);
        _watching = true;
    }

    /// <summary>Akcent treści dla motywu aplikacji: ciemny tylko przy <see cref="ApplicationTheme.Dark"/>, inaczej jasny.</summary>
    public static BrandAccentColors PaletteFor(ApplicationTheme theme) => BrandPalette.For(theme == ApplicationTheme.Dark);

    private void OnApplicationThemeChanged(ApplicationTheme currentApplicationTheme)
    {
        try
        {
            ApplyBrandAccent(currentApplicationTheme);
        }
        catch (Exception ex)
        {
            // Akcent zostaje poprzedni – okno działa dalej.
            _log.Fail(Component, L.T("Nie udało się ustawić akcentu motywu."), ex);
        }
    }

    private static void ApplyBrandAccent(ApplicationTheme theme)
    {
        if (Application.Current?.Resources is { } resources)
        {
            var accent = BrandAccentResources.Apply(resources, PaletteFor(theme));
            ApplicationAccentColorManager.Apply(accent, accent, accent, accent);
        }
    }
}
