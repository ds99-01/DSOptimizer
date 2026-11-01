using System;
using System.Collections.Generic;
using System.Linq;
using DSOptimizer.Catalog;
using DSOptimizer.Core.Apps;
using DSOptimizer.Core.Catalog;
using DSOptimizer.Core.Localization;
using DSOptimizer.Core.Model;
using Xunit;

namespace DSOptimizer.Tests.Unit.Catalog;

/// <summary>
/// Brzmienia zweryfikowane na kodzie i na systemie: opis ma mówić dokładnie to, co robi program. Każdy wiersz to polski i angielski
/// tekst słowo w słowo; pole <c>warning</c> = jedna z uwag, <c>summary</c> = cały summary, <c>description</c> = fragment opisu technicznego.
/// </summary>
public class CatalogWordingTests
{
    private static readonly LoadedCatalog Polish = CatalogLoader.Load(BuiltInCatalog.Sources);

    public static IEnumerable<object[]> Rows()
    {
        yield return new object[]
        {
            "repair.disk-cleanup", "warning",
            "Pliki tymczasowe tylko starsze niż 24 h; pobrane aktualizacje i pamięci podręczne znikają w całości. Kosz i Pobrane zostają.",
            "Temp files: only those older than 24 h; downloaded updates and caches are cleared whatever their age. Recycle Bin and Downloads stay.",
        };
        yield return new object[]
        {
            "sec.bitlocker-prevent-auto", "warning",
            "Poradnik Microsoftu dla producentów sprzętu nazywa wyłączanie szyfrowania poza ich scenariuszem naruszeniem licencji; odradza przy Recall.",
            "Microsoft's guide for device makers calls disabling encryption outside their case a license violation and advises against it with Recall.",
        };
        yield return new object[]
        {
            "privacy.diagtrack", "warning",
            "Microsoft nie opisuje wyłączania tej usługi, a dane o błędach Windows Update mogą przestać być wysyłane. Przy kłopotach cofnij zmianę.",
            "Microsoft doesn't document turning this off, and Windows Update failure data may stop being sent. If you have trouble with updates, undo.",
        };
        yield return new object[]
        {
            "disk.hibernation", "warning",
            "Znika hibernacja i Szybkie uruchamianie, a na laptopie rozładowanie baterii w uśpieniu grozi utratą niezapisanych danych.",
            "Hibernation and fast startup go away; on a laptop, if the battery runs out during sleep, unsaved data can be lost.",
        };
        yield return new object[]
        {
            "disk.ntfs-8dot3", "warning",
            "Starsze programy i instalatory, które używają krótkich nazw, mogą przestać działać.",
            "Older programs and installers that rely on short names may stop working.",
        };
        yield return new object[]
        {
            "ux.classic-context-menu", "summary",
            "Prawy przycisk myszy w Eksploratorze i na pulpicie otwiera klasyczne menu Win32 zamiast menu z Windows 11.",
            "Right-clicking in File Explorer and on the desktop opens the classic Win32 menu instead of the Windows 11 menu.",
        };
        yield return new object[]
        {
            "privacy.spotlight", "description",
            "Opis Microsoftu obejmuje też ekran blokady, ale wymienia tylko Enterprise i Education; na Home i Pro ekranu blokady nie sprawdzono.",
            "Microsoft's description says this also covers the lock screen but lists only Enterprise and Education; on Home and Pro the lock screen wasn't checked.",
        };
        yield return new object[]
        {
            "perf.paging-executive", "summary",
            "Kod systemu i sterowników zostaje zawsze w pamięci RAM, zamiast być odkładany na dysk.",
            "System and driver code always stays in RAM instead of being paged out to disk.",
        };
    }

    [Theory]
    [MemberData(nameof(Rows))]
    public void Wording_MatchesVerifiedFacts(string id, string field, string polish, string english)
    {
        var pl = Polish.Definitions.Single(d => d.Id == id);
        TweakDefinition en;
        using (L.Use(UiLanguage.English))
        {
            en = CatalogTranslation.Translate(Polish).Definitions.Single(d => d.Id == id);
        }

        AssertField(pl, field, polish);
        AssertField(en, field, english);
    }

    [Theory]
    [InlineData("Rozszerzenie wideo HEVC: bez niego część filmów się nie odtworzy.", "HEVC Video Extensions: without them, some videos won't play.")]
    [InlineData("Rozszerzenie obrazów HEIF: bez niego zdjęcia HEIC/HEIF się nie otworzą.", "HEIF Image Extension: without it, HEIC/HEIF photos won't open.")]
    [InlineData("Rozszerzenie obrazów RAW: bez niego pliki RAW z aparatów się nie otworzą.", "Raw Image Extension: without it, RAW files from cameras won't open.")]
    public void CodecReasons_AreHonest(string polish, string english)
    {
        var pl = AppsBloatCatalog.Load(BuiltInCatalog.AppsBloatSource.Json);
        AppsBloatCatalog en;
        using (L.Use(UiLanguage.English))
        {
            en = AppsBloatCatalog.Load(BuiltInCatalog.AppsBloatSource.Json);
        }

        var entry = Assert.Single(pl.Protected, p => p.Reason == polish);
        Assert.Equal(english, en.Protected.Single(p => p.Id == entry.Id).Reason);
    }

    private static void AssertField(TweakDefinition definition, string field, string expected)
    {
        switch (field)
        {
            case "warning":
                Assert.Contains(expected, definition.Warnings);
                break;
            case "summary":
                Assert.Equal(expected, definition.Summary);
                break;
            case "description":
                Assert.Contains(expected, definition.Description, StringComparison.Ordinal);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(field), field, "nieznane pole");
        }
    }
}
