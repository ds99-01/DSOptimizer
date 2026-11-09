using System;
using DSOptimizer.Core.Localization;
using DSOptimizer.Core.Storage;
using Xunit;

namespace DSOptimizer.Tests.Unit.Storage;

public sealed class SetAsideTextTests
{
    private const string Root = @"C:\ProgramData\DSOptimizer";
    private const string Aside = @"C:\ProgramData\DSOptimizer.untrusted-20261109120000000-1";

    [Fact]
    public void Root_WithHistory_Pinned()
        => Assert.Equal(
            "Folder programu C:\\ProgramData\\DSOptimizer miał zmienione uprawnienia, więc program odłożył go na bok i utworzył nowy. Najczęściej to skutek kliknięcia „Kontynuuj” w Eksploratorze plików przy tym folderze. Poprzednie pliki, w tym historia zmian, są w C:\\ProgramData\\DSOptimizer.untrusted-20261109120000000-1. Program zaczyna z ustawieniami domyślnymi i pustą historią zmian; przy zmianach sprzed odłożenia „Cofnij” przywróci wartości domyślne Windows, a nie stan sprzed zmiany. Nie otwieraj folderu programu w Eksploratorze, a raport zapisuj przyciskiem „Zapisz raport”.",
            SetAsideText.Root(Root, Aside, true));

    [Fact]
    public void Root_WithoutHistory_Pinned()
        => Assert.Equal(
            "Folder programu C:\\ProgramData\\DSOptimizer miał zmienione uprawnienia, więc program odłożył go na bok i utworzył nowy. Najczęściej to skutek kliknięcia „Kontynuuj” w Eksploratorze plików przy tym folderze. Poprzednie pliki są w C:\\ProgramData\\DSOptimizer.untrusted-20261109120000000-1. Program zaczyna z ustawieniami domyślnymi. Nie otwieraj folderu programu w Eksploratorze, a raport zapisuj przyciskiem „Zapisz raport”.",
            SetAsideText.Root(Root, Aside, false));

    [Fact]
    public void Folder_Pinned()
        => Assert.Equal(
            "Podfolder programu miał zmienione uprawnienia, więc program odłożył go na bok i utworzył nowy; najczęściej to skutek kliknięcia „Kontynuuj” w Eksploratorze plików. Poprzednie pliki są w C:\\ProgramData\\DSOptimizer.untrusted-20261109120000000-1. Nie otwieraj folderu programu w Eksploratorze.",
            SetAsideText.Folder(Aside));

    [Fact]
    public void Settings_Pinned()
        => Assert.Equal(
            "Plik ustawień miał zmienione uprawnienia, więc program odłożył go na bok i używa ustawień domyślnych; najczęściej to skutek kliknięcia „Kontynuuj” w Eksploratorze plików. Poprzedni plik: C:\\ProgramData\\DSOptimizer\\settings.json.untrusted-1. Nie otwieraj folderu programu w Eksploratorze.",
            SetAsideText.Settings(@"C:\ProgramData\DSOptimizer\settings.json.untrusted-1"));

    [Fact]
    public void History_Pinned()
        => Assert.Equal(
            "Historia zmian miała zmienione uprawnienia, więc program odłożył ją na bok i prowadzi nową; najczęściej to skutek kliknięcia „Kontynuuj” w Eksploratorze plików. Poprzednia historia: C:\\a\\journal.json, C:\\b\\journal.json. Przy zmianach sprzed odłożenia „Cofnij” przywróci wartości domyślne Windows, a nie stan sprzed zmiany. Nie otwieraj folderu programu w Eksploratorze.",
            SetAsideText.History(@"C:\a\journal.json, C:\b\journal.json"));

    [Fact]
    public void English_Pinned()
    {
        using var _ = L.Use(UiLanguage.English);

        Assert.Equal(
            "The program folder C:\\ProgramData\\DSOptimizer had its permissions changed, so the program set it aside and created a new one. This usually happens after clicking \"Continue\" in File Explorer for this folder. The previous files, including the change history, are in C:\\ProgramData\\DSOptimizer.untrusted-20261109120000000-1. The program starts with default settings and an empty change history; for changes made before the folder was set aside, \"Undo\" restores the Windows default values, not the state before the change. Don't open the program folder in File Explorer, and save the report with the \"Save report\" button.",
            SetAsideText.Root(Root, Aside, true));
        Assert.Equal(
            "The program folder C:\\ProgramData\\DSOptimizer had its permissions changed, so the program set it aside and created a new one. This usually happens after clicking \"Continue\" in File Explorer for this folder. The previous files are in C:\\ProgramData\\DSOptimizer.untrusted-20261109120000000-1. The program starts with default settings. Don't open the program folder in File Explorer, and save the report with the \"Save report\" button.",
            SetAsideText.Root(Root, Aside, false));
        Assert.Equal(
            "A program folder had its permissions changed, so the program set it aside and created a new one; this usually happens after clicking \"Continue\" in File Explorer. The previous files are in C:\\ProgramData\\DSOptimizer.untrusted-20261109120000000-1. Don't open the program folder in File Explorer.",
            SetAsideText.Folder(Aside));
        Assert.Equal(
            "The settings file had its permissions changed, so the program set it aside and uses default settings; this usually happens after clicking \"Continue\" in File Explorer. Previous file: C:\\s.untrusted-1. Don't open the program folder in File Explorer.",
            SetAsideText.Settings(@"C:\s.untrusted-1"));
        Assert.Equal(
            "The change history had its permissions changed, so the program set it aside and keeps a new one; this usually happens after clicking \"Continue\" in File Explorer. Previous history: C:\\a\\journal.json. For changes made before it was set aside, \"Undo\" restores the Windows default values, not the state before the change. Don't open the program folder in File Explorer.",
            SetAsideText.History(@"C:\a\journal.json"));
    }

    [Fact]
    public void NoLineBreaks()
    {
        foreach (var language in new[] { UiLanguage.Polish, UiLanguage.English })
        {
            using var _ = L.Use(language);
            var all = new[]
            {
                SetAsideText.Root(Root, Aside, true),
                SetAsideText.Root(Root, Aside, false),
                SetAsideText.Folder(Aside),
                SetAsideText.Settings(Aside),
                SetAsideText.History(Aside),
            };

            Assert.All(all, text =>
            {
                Assert.DoesNotContain('\r', text);
                Assert.DoesNotContain('\n', text);
            });
        }
    }
}
