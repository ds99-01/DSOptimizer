using DSOptimizer.Core.Install;
using Xunit;

namespace DSOptimizer.Tests.Unit.Install;

public class InstallSearchTests
{
    private static readonly AppsInstallCatalog Catalog = AppsInstallCatalog.Load("""
        { "entries": [
          { "id": "install.7zip", "wingetId": "7zip.7zip", "title": "7-Zip", "summary": "Archiwizator plików.",
            "category": "tools", "match": { "displayName": "7-Zip*" }, "source": "https://example.com/7zip" },
          { "id": "install.paintnet", "wingetId": "dotPDN.PaintDotNet", "title": "Paint.NET", "summary": "Edytor obrazów.",
            "category": "multimedia", "match": { "displayName": "Paint.NET*" }, "source": "https://example.com/paint" },
          { "id": "install.vlc", "wingetId": "VideoLAN.VLC", "title": "VLC", "summary": "Odtwarzacz.",
            "category": "multimedia", "match": { "displayName": "VLC media player" }, "source": "https://example.com/vlc" },
          { "id": "install.rustdesk", "github": "rustdesk/rustdesk", "title": "RustDesk", "summary": "Zdalny pulpit.",
            "category": "remote-help", "match": { "displayName": "RustDesk", "publisher": "Purslane Tech*" },
            "source": "https://github.com/rustdesk/rustdesk/releases" },
          { "id": "install.firefox", "wingetId": "Mozilla.Firefox", "title": "Przeglądarka", "summary": "Strony www.",
            "category": "browsers", "match": { "displayName": "Mozilla Firefox*" }, "source": "https://example.com/ff" }
        ] }
        """);

    private static bool Matches(string id, string query)
    {
        var entry = Catalog.Find(id)!;
        return InstallSearch.Matches(InstallTarget.FromEntry(entry), entry.Summary, query);
    }

    [Theory]
    [InlineData("7-Zip", "7zip")]
    [InlineData("Paint.NET", "paintnet")]
    [InlineData("Notepad++", "notepad++")]
    [InlineData(" - . ", "")]
    [InlineData("Źródło_Łódź", "zrodlolodz")]
    [InlineData(null, "")]
    public void Normalize_FoldsAndDropsSeparators(string? input, string expected)
        => Assert.Equal(expected, InstallSearch.Normalize(input));

    [Theory]
    [InlineData("install.7zip", "7zip")]
    [InlineData("install.paintnet", "paint net")]
    [InlineData("install.vlc", "vlc")]
    [InlineData("install.rustdesk", "rustdesk/rust")]
    [InlineData("install.firefox", "mozilla.fire")]
    [InlineData("install.firefox", "przegladarka")]
    [InlineData("install.7zip", "archiwizator")]
    [InlineData("install.vlc", "")]
    [InlineData("install.vlc", " - ")]
    public void Matches_True(string id, string query) => Assert.True(Matches(id, query));

    [Theory]
    [InlineData("install.7zip", "xyz")]
    [InlineData("install.vlc", "7zip")]
    public void Matches_False(string id, string query) => Assert.False(Matches(id, query));

    [Fact]
    public void Matches_NullSummary_StillMatchesTitle()
        => Assert.True(InstallSearch.Matches(InstallTarget.FromEntry(Catalog.Find("install.vlc")!), null, "vlc"));

    [Fact]
    public void Matches_NullArguments()
    {
        Assert.Throws<System.ArgumentNullException>(() => InstallSearch.Matches(null!, null, "x"));
        Assert.True(InstallSearch.Matches(InstallTarget.FromEntry(Catalog.Find("install.vlc")!), null, null!));
    }
}
