using System;
using System.Collections.Generic;
using System.Linq;
using DSOptimizer.Core.Localization;
using DSOptimizer.Core.Storage;
using Xunit;

namespace DSOptimizer.Tests.Unit.Storage;

public sealed class SetAsideTextTests
{
    private const string Aside = @"C:\ProgramData\DSOptimizer.untrusted-20261109120000000-1";
    private const string Source = @"C:\ProgramData\DSOptimizer.untrusted-20261109120000000-1\journal\journal.json";
    private const string SettingsSource = @"C:\ProgramData\DSOptimizer.untrusted-20261109120000000-1\settings.json";

    [Fact]
    public void RootMoved_Pinned()
        => Assert.Equal(
            "Folder programu miał zmienione uprawnienia, więc program odłożył go na bok (C:\\ProgramData\\DSOptimizer.untrusted-20261109120000000-1) i utworzył nowy.",
            SetAsideText.RootMoved(Aside));

    [Fact]
    public void HistoryMoved_Pinned()
        => Assert.Equal("Historię zmian przeniesiono do nowego folderu programu.", SetAsideText.HistoryMoved);

    [Fact]
    public void SettingsMoved_Pinned()
        => Assert.Equal("Ustawienia przeniesiono do nowego folderu programu.", SetAsideText.SettingsMoved);

    [Fact]
    public void HistoryRestored_Pinned()
        => Assert.Equal(
            "Historię zmian odzyskano z C:\\ProgramData\\DSOptimizer.untrusted-20261109120000000-1\\journal\\journal.json.",
            SetAsideText.HistoryRestored(Source));

    [Fact]
    public void HistoryNotMoved_Pinned()
        => Assert.Equal(
            "Nie przeniesiono historii zmian z C:\\ProgramData\\DSOptimizer.untrusted-20261109120000000-1\\journal\\journal.json: plik ma więcej niż jedno dowiązanie.",
            SetAsideText.HistoryNotMoved(Source, "plik ma więcej niż jedno dowiązanie"));

    [Fact]
    public void SettingsNotMoved_Pinned()
        => Assert.Equal(
            "Nie przeniesiono ustawień z C:\\ProgramData\\DSOptimizer.untrusted-20261109120000000-1\\settings.json: plik jest uszkodzony (x). Program używa ustawień domyślnych.",
            SetAsideText.SettingsNotMoved(SettingsSource, "plik jest uszkodzony (x)"));

    [Fact]
    public void HistoryLost_Pinned()
        => Assert.Equal(
            "Dane programu zostały zmienione poza programem, więc historia zmian zaczyna się od nowa. Cofnięcie wcześniejszych zmian przywróci wartości domyślne Windows, a nie stan sprzed zmiany. Poprzednie pliki są w C:\\a, C:\\b.",
            SetAsideText.HistoryLost(@"C:\a, C:\b"));

    [Fact]
    public void Folder_Pinned()
        => Assert.Equal(
            "Podfolder programu miał zmienione uprawnienia, więc program odłożył go na bok i utworzył nowy. Poprzednie pliki są w C:\\ProgramData\\DSOptimizer.untrusted-20261109120000000-1.",
            SetAsideText.Folder(Aside));

    [Fact]
    public void Settings_Pinned()
        => Assert.Equal(
            "Plik ustawień miał zmienione uprawnienia, więc program odłożył go na bok i używa ustawień domyślnych. Poprzedni plik: C:\\ProgramData\\DSOptimizer\\settings.json.untrusted-1.",
            SetAsideText.Settings(@"C:\ProgramData\DSOptimizer\settings.json.untrusted-1"));

    [Fact]
    public void History_Pinned()
        => Assert.Equal(
            "Historia zmian miała zmienione uprawnienia, więc program odłożył ją na bok i prowadzi nową. Poprzednia historia: C:\\a\\journal.json, C:\\b\\journal.json. Przy zmianach sprzed odłożenia „Cofnij” przywróci wartości domyślne Windows, a nie stan sprzed zmiany.",
            SetAsideText.History(@"C:\a\journal.json, C:\b\journal.json"));

    public static TheoryData<SalvageRejection, string> PolishReasons => new()
    {
        { SalvageRejection.OpenFailed, "nie można otworzyć pliku: x" },
        { SalvageRejection.NotRegularFile, "to nie jest zwykły plik (link, folder albo urządzenie)" },
        { SalvageRejection.MultipleLinks, "plik ma więcej niż jedno dowiązanie" },
        { SalvageRejection.UntrustedAcl, "plik ma obcego właściciela albo uprawnienia" },
        { SalvageRejection.TooLarge, "plik jest za duży (x B)" },
        { SalvageRejection.Corrupted, "plik jest uszkodzony (x)" },
        { SalvageRejection.OtherComputer, "historia zmian pochodzi z innego komputera (x)" },
        { SalvageRejection.WriteFailed, "nie można zapisać kopii: x" },
    };

    [Theory]
    [MemberData(nameof(PolishReasons))]
    public void Reason_EveryValue_Pinned(SalvageRejection rejection, string expected)
        => Assert.Equal(expected, SetAsideText.Reason(rejection, "x"));

    [Fact]
    public void Reason_EveryEnumValueCovered()
        => Assert.Equal(
            Enum.GetValues(typeof(SalvageRejection)).Cast<SalvageRejection>().OrderBy(r => r),
            PolishReasons.Select(row => (SalvageRejection)row[0]).OrderBy(r => r));

    [Fact]
    public void HistoryNotMoved_DetailEndingWithPeriod_SinglePeriod()
        => Assert.Equal(
            "Nie przeniesiono historii zmian z C:\\ProgramData\\DSOptimizer.untrusted-20261109120000000-1\\journal\\journal.json: nie można otworzyć pliku: Odmowa dostępu.",
            SetAsideText.HistoryNotMoved(Source, SetAsideText.Reason(SalvageRejection.OpenFailed, "Odmowa dostępu.")));

    [Fact]
    public void SettingsNotMoved_DetailEndingWithPeriod_SinglePeriod()
        => Assert.Equal(
            "Nie przeniesiono ustawień z C:\\ProgramData\\DSOptimizer.untrusted-20261109120000000-1\\settings.json: nie można zapisać kopii: Plik istnieje. Program używa ustawień domyślnych.",
            SetAsideText.SettingsNotMoved(SettingsSource, SetAsideText.Reason(SalvageRejection.WriteFailed, "Plik istnieje.")));

    [Fact]
    public void Reason_DetailWithoutPeriodOrNull_Unchanged()
    {
        Assert.Equal("nie można otworzyć pliku: x", SetAsideText.Reason(SalvageRejection.OpenFailed, "x"));
        Assert.Equal("nie można otworzyć pliku: ", SetAsideText.Reason(SalvageRejection.OpenFailed, null));
        Assert.Equal("plik jest uszkodzony (x.)", SetAsideText.Reason(SalvageRejection.Corrupted, "x.."));
    }

    [Fact]
    public void Reason_UnknownValue_Throws()
        => Assert.Throws<ArgumentOutOfRangeException>(() => SetAsideText.Reason((SalvageRejection)999, "x"));

    [Fact]
    public void English_Pinned()
    {
        using var _ = L.Use(UiLanguage.English);

        Assert.Equal(
            "The program folder had its permissions changed, so the program set it aside (C:\\ProgramData\\DSOptimizer.untrusted-20261109120000000-1) and created a new one.",
            SetAsideText.RootMoved(Aside));
        Assert.Equal("The change history was moved to the new program folder.", SetAsideText.HistoryMoved);
        Assert.Equal("The settings were moved to the new program folder.", SetAsideText.SettingsMoved);
        Assert.Equal(
            "The change history was recovered from C:\\ProgramData\\DSOptimizer.untrusted-20261109120000000-1\\journal\\journal.json.",
            SetAsideText.HistoryRestored(Source));
        Assert.Equal(
            "The change history from C:\\ProgramData\\DSOptimizer.untrusted-20261109120000000-1\\journal\\journal.json wasn't moved: r.",
            SetAsideText.HistoryNotMoved(Source, "r"));
        Assert.Equal(
            "The settings from C:\\ProgramData\\DSOptimizer.untrusted-20261109120000000-1\\settings.json weren't moved: r. The program uses default settings.",
            SetAsideText.SettingsNotMoved(SettingsSource, "r"));
        Assert.Equal(
            "The program's data was changed outside the program, so the change history starts over. Undoing earlier changes restores the Windows default values, not the state before the change. The previous files are in C:\\a.",
            SetAsideText.HistoryLost(@"C:\a"));
        Assert.Equal(
            "A program subfolder had its permissions changed, so the program set it aside and created a new one. The previous files are in C:\\ProgramData\\DSOptimizer.untrusted-20261109120000000-1.",
            SetAsideText.Folder(Aside));
        Assert.Equal(
            "The settings file had its permissions changed, so the program set it aside and uses default settings. Previous file: C:\\s.untrusted-1.",
            SetAsideText.Settings(@"C:\s.untrusted-1"));
        Assert.Equal(
            "The change history had its permissions changed, so the program set it aside and keeps a new one. Previous history: C:\\a\\journal.json. For changes made before it was set aside, \"Undo\" restores the Windows default values, not the state before the change.",
            SetAsideText.History(@"C:\a\journal.json"));
        Assert.Equal("the file can't be opened: x", SetAsideText.Reason(SalvageRejection.OpenFailed, "x"));
        Assert.Equal("it isn't a regular file (link, folder or device)", SetAsideText.Reason(SalvageRejection.NotRegularFile, "x"));
        Assert.Equal("the file has more than one hard link", SetAsideText.Reason(SalvageRejection.MultipleLinks, "x"));
        Assert.Equal("the file has a foreign owner or permissions", SetAsideText.Reason(SalvageRejection.UntrustedAcl, "x"));
        Assert.Equal("the file is too large (x B)", SetAsideText.Reason(SalvageRejection.TooLarge, "x"));
        Assert.Equal("the file is corrupted (x)", SetAsideText.Reason(SalvageRejection.Corrupted, "x"));
        Assert.Equal("the change history comes from another computer (x)", SetAsideText.Reason(SalvageRejection.OtherComputer, "x"));
        Assert.Equal("the copy couldn't be saved: x", SetAsideText.Reason(SalvageRejection.WriteFailed, "x"));
    }

    [Fact]
    public void NoLineBreaks()
    {
        foreach (var language in new[] { UiLanguage.Polish, UiLanguage.English })
        {
            using var _ = L.Use(language);
            Assert.All(AllTexts(), text =>
            {
                Assert.DoesNotContain('\r', text);
                Assert.DoesNotContain('\n', text);
            });
        }
    }

    [Fact]
    public void NoContinueAdvice()
    {
        foreach (var language in new[] { UiLanguage.Polish, UiLanguage.English })
        {
            using var _ = L.Use(language);
            Assert.All(AllTexts(), text =>
            {
                Assert.DoesNotContain("Kontynuuj", text, StringComparison.Ordinal);
                Assert.DoesNotContain("Nie otwieraj", text, StringComparison.Ordinal);
                Assert.DoesNotContain("Continue", text, StringComparison.Ordinal);
                Assert.DoesNotContain("Don't open", text, StringComparison.Ordinal);
            });
        }
    }

    private static List<string> AllTexts()
    {
        var texts = new List<string>
        {
            SetAsideText.RootMoved(Aside),
            SetAsideText.HistoryMoved,
            SetAsideText.SettingsMoved,
            SetAsideText.HistoryRestored(Source),
            SetAsideText.HistoryNotMoved(Source, "r"),
            SetAsideText.SettingsNotMoved(SettingsSource, "r"),
            SetAsideText.HistoryLost(Aside),
            SetAsideText.Folder(Aside),
            SetAsideText.Settings(Aside),
            SetAsideText.History(Aside),
        };
        texts.AddRange(Enum.GetValues(typeof(SalvageRejection)).Cast<SalvageRejection>().Select(r => SetAsideText.Reason(r, "x")));
        return texts;
    }
}
