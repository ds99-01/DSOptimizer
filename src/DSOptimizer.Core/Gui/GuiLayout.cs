using System;

namespace DSOptimizer.Core.Gui;

/// <summary>Prostokąt w DIP (lewy górny róg i rozmiar).</summary>
public readonly record struct LayoutRect(double Left, double Top, double Width, double Height);

/// <summary>Reguły geometrii okna, kolumny szczegółów i dziennika (wszystko w DIP).</summary>
public static class GuiLayout
{
    public const double MaxWidth = 1600;
    public const double MaxHeight = 1000;
    public const double MinWidth = 800;
    public const double MinHeight = 520;
    public const double WorkAreaShare = 0.9;

    /// <summary>
    /// Pełne menu: mieści „Bezpieczeństwo i szyfrowanie” pogrubione z licznikiem dwucyfrowym obok ikony 20 px. Hanken Grotesk
    /// SemiBold 14 px: 193 (Display 100%) + licznik „88/88” 33 + ikona i odstępy 72 = 298 (najgorzej przy 125%: 298,4);
    /// 304 = najmniejsza wielokrotność 4 z zapasem ≥ 5 px. Pilnuje tego NavMenuWidthTests (Tests.Windows).
    /// </summary>
    public const double NavFullWidth = 304;

    /// <summary>Menu zwinięte do samych ikon (ikona 20 px wyśrodkowana, nazwa w podpowiedzi).</summary>
    public const double NavCompactWidth = 56;

    /// <summary>Poniżej tej szerokości okna menu zwija się do ikon (okno startowe na ekranie 1024×768 ma ok. 922 px).</summary>
    public const double NavCompactBelow = 1100;

    private const double DetailMin = 280;
    private const double DetailMax = 420;
    private const double DetailNarrowMin = 240;
    private const double ListMin = 360;
    private const double LogMin = 146;
    private const double LogMax = 240;

    /// <summary>Startowy rozmiar okna: 90% obszaru roboczego w granicach min/max, nigdy większy niż obszar; wyśrodkowany.</summary>
    public static LayoutRect InitialBounds(LayoutRect workArea)
    {
        if (workArea.Width <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(workArea), "Szerokość obszaru roboczego musi być dodatnia.");
        }

        if (workArea.Height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(workArea), "Wysokość obszaru roboczego musi być dodatnia.");
        }

        var width = Fit(workArea.Width, MinWidth, MaxWidth);
        var height = Fit(workArea.Height, MinHeight, MaxHeight);
        return new LayoutRect(
            workArea.Left + ((workArea.Width - width) / 2),
            workArea.Top + ((workArea.Height - height) / 2),
            width,
            height);
    }

    /// <summary>
    /// Szerokość kolumny szczegółów: 1/3 treści w granicach 280–420. Przy wąskiej treści (np. ekran 1024×768) szczegóły
    /// ustępują liście, żeby kolumna listy (z odstępem przed szczegółami) miała 360, ale nie schodzą poniżej 240.
    /// </summary>
    public static double DetailColumnWidth(double contentWidth)
    {
        var regular = Math.Min(DetailMax, Math.Max(DetailMin, contentWidth / 3));
        return contentWidth - regular >= ListMin
            ? regular
            : Math.Max(DetailNarrowMin, contentWidth - ListMin);
    }

    /// <summary>Czy lewe menu ma być zwinięte do ikon przy danej szerokości okna (na żywo, przy każdej zmianie rozmiaru).</summary>
    public static bool IsNavCompact(double windowWidth) => windowWidth < NavCompactBelow;

    /// <summary>Szerokość kolumny menu przy danej szerokości okna: 56 (zwinięte) albo 304 (pełne).</summary>
    public static double NavWidth(double windowWidth) => IsNavCompact(windowWidth) ? NavCompactWidth : NavFullWidth;

    /// <summary>Startowa wysokość dziennika: 20% okna w granicach 146–240.</summary>
    public static double InitialLogHeight(double windowHeight) =>
        Math.Min(LogMax, Math.Max(LogMin, windowHeight * 0.2));

    /// <summary>Maksymalna wysokość dziennika: połowa okna, nie mniej niż 146.</summary>
    public static double MaxLogHeight(double windowHeight) =>
        Math.Max(LogMin, windowHeight / 2);

    private static double Fit(double available, double min, double max) =>
        Math.Min(available, Math.Max(min, Math.Min(max, available * WorkAreaShare)));
}
