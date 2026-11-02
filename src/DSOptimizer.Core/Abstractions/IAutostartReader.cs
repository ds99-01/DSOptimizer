using DSOptimizer.Core.Autostart;

namespace DSOptimizer.Core.Abstractions;

/// <summary>Odczyt pozycji autostartu zalogowanego konta razem z pomiarem Windows. Odczyt niczego nie zapisuje.</summary>
public interface IAutostartReader
{
    AutostartReading Read(AutostartContext context);
}
