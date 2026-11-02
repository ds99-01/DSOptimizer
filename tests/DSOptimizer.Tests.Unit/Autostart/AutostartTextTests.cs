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
    public void EngineTexts_Verbatim()
    {
        Assert.Equal("Wyłączona – zadziała od następnego logowania.", AutostartText.Disabled);
        Assert.Equal("Włączona – zadziała od następnego logowania.", AutostartText.Enabled);
        Assert.Equal("Była już wyłączona – nic nie zmieniono.", AutostartText.AlreadyDisabled);
        Assert.Equal("Była już włączona – nic nie zmieniono.", AutostartText.AlreadyEnabled);
        Assert.Equal("Cofnięto wcześniejszą zmianę z Historii zmian – zadziała od następnego logowania.", AutostartText.UndoneByToggle);
        Assert.Equal("Odczyt po zapisie nie potwierdził zmiany – przywrócono poprzedni stan.", AutostartText.NotConfirmed);
        Assert.Equal("Odczyt po zapisie nie potwierdził zmiany, a poprzedniego stanu nie udało się przywrócić.", AutostartText.RollbackIncomplete);
        Assert.Equal("Zmieniono poza programem.", AutostartText.ChangedOutside);
        Assert.Equal("Nie udało się zapisać journala – nic nie zmieniono: dysk", AutostartText.JournalFailed("dysk"));
        Assert.Equal("Zapis stanu zmienił się od odczytu listy – nic nie zmieniono. Odśwież listę.", AutostartText.FormatChanged);
        Assert.Equal("Przywrócono stan sprzed zmiany – zadziała od następnego logowania.", AutostartText.UndoneFromHistory);
        Assert.Equal("Brak aktywnej zmiany tej pozycji w Historii zmian.", AutostartText.NoActiveEntry);
    }

    [Fact]
    public void EngineTexts_Translated()
    {
        using (L.Use(UiLanguage.English))
        {
            Assert.Equal("Disabled – takes effect at the next sign-in.", AutostartText.Disabled);
            Assert.Equal("Couldn't save the journal – nothing was changed: disk", AutostartText.JournalFailed("disk"));
        }
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
