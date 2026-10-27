using System;
using System.IO;
using DSOptimizer.Core.Catalog;
using DSOptimizer.Core.Cli;
using Xunit;

namespace DSOptimizer.Tests.Unit.Cli;

public class CliCatalogTests
{
    private const string ValidJson = "[{\"id\":\"vmtest.a\",\"category\":\"vmtest\",\"kind\":\"tweak\",\"title\":\"A\",\"summary\":\"Opis A.\",\"benefit\":\"Zysk A.\",\"risk\":\"safe\",\"scope\":\"machine\",\"registry\":[{\"path\":\"HKLM\\\\SOFTWARE\\\\X\",\"name\":\"A\",\"type\":\"DWord\",\"value\":1,\"original\":\"<RemoveEntry>\"}]}]";

    [Fact]
    public void Load_NoExternal_UsesBuiltInSources()
    {
        var result = CliCatalog.Load(null, _ => throw new InvalidOperationException("nie czytać"), new[] { new CatalogSource("w.json", ValidJson) });
        Assert.Equal(CliCatalog.BuiltInSource, result.Source);
        Assert.Single(result.Catalog!.Definitions);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void Load_External_ReplacesBuiltInAndReportsFullPath()
    {
        var result = CliCatalog.Load(@"C:\dso\k.json", _ => ValidJson, new[] { new CatalogSource("inny.json", "[]") });
        Assert.Equal(@"C:\dso\k.json", result.Source);
        Assert.Equal("vmtest.a", Assert.Single(result.Catalog!.Definitions).Id);
        StringAssert(result.Catalog.Hash);
    }

    [Theory]
    [InlineData(typeof(FileNotFoundException))]
    [InlineData(typeof(UnauthorizedAccessException))]
    [InlineData(typeof(IOException))]
    public void Load_ExternalUnreadable_ReturnsErrorWithoutCatalog(Type exceptionType)
    {
        var result = CliCatalog.Load(@"C:\brak.json", _ => throw (Exception)Activator.CreateInstance(exceptionType, "boom")!, Array.Empty<CatalogSource>());
        Assert.Null(result.Catalog);
        Assert.Contains("Nie można odczytać katalogu", Assert.Single(result.Errors), StringComparison.Ordinal);
    }

    [Fact]
    public void Load_InvalidJson_ReturnsLoaderMessage()
        => Assert.Contains("błędny JSON", Assert.Single(CliCatalog.Load(@"C:\k.json", _ => "[{", Array.Empty<CatalogSource>()).Errors), StringComparison.Ordinal);

    [Fact]
    public void Load_ValidationErrors_ReturnsAllWithoutCatalog()
    {
        var bad = ValidJson.Replace("\"original\":\"<RemoveEntry>\"", "\"detectOnly\":false").Replace("vmtest.a", "ZLE");
        var result = CliCatalog.Load(@"C:\k.json", _ => bad, Array.Empty<CatalogSource>());
        Assert.Null(result.Catalog);
        Assert.Contains(result.Errors, e => e.Contains("id-format"));
        Assert.Contains(result.Errors, e => e.Contains("tweak-reversible"));
    }

    private static void StringAssert(string hash) => Assert.StartsWith("sha256:", hash, StringComparison.Ordinal);
}
