using System;
using System.Linq;
using DSOptimizer.Core.Autostart;
using DSOptimizer.Core.Localization;
using Xunit;

namespace DSOptimizer.Tests.Unit.Autostart;

public class AutostartTextTests
{
    [Fact]
    public void Blocked_NoneNull_EachBlockerText()
    {
        Assert.Null(AutostartText.Blocked(AutostartBlocker.None));
        Assert.Equal("Chroniona – DSOptimizer jej nie wyłącza", AutostartText.Blocked(AutostartBlocker.Protected));
        Assert.Equal(AutostartText.ProtectedTooltip, AutostartText.Blocked(AutostartBlocker.Protected));
        Assert.Equal("Nie udało się ustalić zalogowanego użytkownika.", AutostartText.Blocked(AutostartBlocker.UserUncertain));
        Assert.Equal(
            "Ustawienia tego konta są niedostępne – uruchom DSOptimizer na zalogowanym koncie.",
            AutostartText.Blocked(AutostartBlocker.UserHiveMissing));
        Assert.Equal("Nie udało się odczytać stanu – DSOptimizer tej pozycji nie zmienia.", AutostartText.Blocked(AutostartBlocker.Unreadable));
        Assert.Equal("Nieznany zapis stanu – DSOptimizer tej pozycji nie zmienia.", AutostartText.Blocked(AutostartBlocker.UnknownFormat));
        Assert.Equal(
            "Stan ustawiony zasadami albo nieznany – DSOptimizer tej pozycji nie zmienia.",
            AutostartText.Blocked(AutostartBlocker.TaskStateNotSupported));
        Assert.Equal(
            "Tej pozycji nikt jeszcze nie przełączał – DSOptimizer jej nie zmienia, wyłączysz ją Menedżerem zadań.",
            AutostartText.Blocked(AutostartBlocker.WriteFromZeroNotMeasured));
        Assert.Equal("Wpis bez nazwy – DSOptimizer go nie zmienia.", AutostartText.Blocked(AutostartBlocker.NoName));
        Assert.Throws<ArgumentOutOfRangeException>(() => AutostartText.Blocked((AutostartBlocker)99));
    }

    [Fact]
    public void Blocked_EveryBlockerTranslated()
    {
        using (L.Use(UiLanguage.English))
        {
            var texts = Enum.GetValues(typeof(AutostartBlocker)).Cast<AutostartBlocker>().Where(b => b != AutostartBlocker.None)
                .Select(AutostartText.Blocked).ToList();

            Assert.Equal(texts.Count, texts.Distinct(StringComparer.Ordinal).Count());
            Assert.Equal("Protected – DSOptimizer doesn't disable it", AutostartText.Blocked(AutostartBlocker.Protected));
        }
    }

    [Fact]
    public void Scope_ThreeTexts()
    {
        Assert.Equal("to konto", AutostartText.Scope(AutostartScope.User));
        Assert.Equal("wszystkie konta", AutostartText.Scope(AutostartScope.Machine));
        Assert.Equal("Sklep", AutostartText.Scope(AutostartScope.Store));
        Assert.Throws<ArgumentOutOfRangeException>(() => AutostartText.Scope((AutostartScope)99));
    }

    [Fact]
    public void RowTexts_FromRender()
    {
        Assert.Equal("Bez oceny – brak tej pozycji w bazie programu.", AutostartText.NotAssessed);
        Assert.Equal(
            "Plik z polecenia nie istnieje, więc ta pozycja niczego nie uruchamia. Wyłączenie niczego nie psuje.",
            AutostartText.MissingFileSummary);
    }
}
