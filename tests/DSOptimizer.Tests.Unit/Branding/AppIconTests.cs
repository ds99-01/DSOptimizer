using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using DSOptimizer.Core.Gui;
using DSOptimizer.Tests.Unit.Support;
using Xunit;

namespace DSOptimizer.Tests.Unit.Branding;

/// <summary>
/// Znak „DŚO” jest zdefiniowany w jednym pliku (<c>Themes/BrandMark.xaml</c>), a <c>dsoptimizer.ico</c> powstaje z niego
/// skryptem <c>tools/brand/Build-AppIcon.ps1</c>. Testy czytają oba pliki prosto z repo.
/// </summary>
public class AppIconTests
{
    private static readonly XNamespace Presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    private static readonly int[] RequiredFrames = [16, 20, 24, 32, 40, 48, 64, 256];

    private static string IconPath => RepoRoot.Combine("src", "DSOptimizer.App", "Assets", "dsoptimizer.ico");

    private static string BrandMarkPath => RepoRoot.Combine("src", "DSOptimizer.App", "Themes", "BrandMark.xaml");

    private static string TokensPath => RepoRoot.Combine("src", "DSOptimizer.App", "Themes", "Tokens.xaml");

    [Fact]
    public void Ico_HasRequiredFrames()
    {
        var bytes = File.ReadAllBytes(IconPath);

        Assert.True(bytes.Length >= 6, "Plik .ico krótszy niż nagłówek ICONDIR.");
        Assert.Equal(0, BitConverter.ToUInt16(bytes, 0));
        Assert.Equal(1, BitConverter.ToUInt16(bytes, 2));
        int count = BitConverter.ToUInt16(bytes, 4);

        var sizes = new int[count];
        for (var i = 0; i < count; i++)
        {
            var entry = 6 + (16 * i);
            var width = bytes[entry] == 0 ? 256 : bytes[entry];
            var height = bytes[entry + 1] == 0 ? 256 : bytes[entry + 1];
            var length = BitConverter.ToInt32(bytes, entry + 8);
            var offset = BitConverter.ToInt32(bytes, entry + 12);

            Assert.Equal(width, height);
            Assert.True(offset > 0 && length > 24 && offset + length <= bytes.Length, $"Klatka {width} px poza plikiem.");
            Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47 }, bytes.Skip(offset).Take(4).ToArray());
            Assert.Equal(width, PngDimension(bytes, offset + 16));
            Assert.Equal(height, PngDimension(bytes, offset + 20));
            sizes[i] = width;
        }

        Assert.Equal(RequiredFrames, sizes.OrderBy(s => s).ToArray());
    }

    [Fact]
    public void BrandMark_DefinesKeys()
    {
        var keyed = Keyed(XDocument.Load(BrandMarkPath));

        Assert.True(keyed.ContainsKey("BrandMarkLettersGeometry"), "Brak BrandMarkLettersGeometry.");
        Assert.True(keyed.ContainsKey("BrandMarkRingGeometry"), "Brak BrandMarkRingGeometry.");
        Assert.Equal("DrawingImage", keyed["BrandMarkImage"].Name.LocalName);
        Assert.Equal("DrawingImage", keyed["BrandMarkSmallImage"].Name.LocalName);
    }

    /// <summary>Pierścień „O” = <see cref="BrandPalette.MarkRing"/> w obu motywach (nie akcent treści, który w jasnym motywie jest ciemniejszy).</summary>
    [Fact]
    public void BrandMark_ColorsMatchPalette()
    {
        var keyed = Keyed(XDocument.Load(BrandMarkPath));

        var image = Drawings(keyed["BrandMarkImage"]);
        Assert.Equal(new[] { "#FFFFFF", BrandPalette.MarkLetters, BrandPalette.MarkRing }, image.Select(d => Color(d)).ToArray());
        Assert.Equal("{StaticResource BrandMarkLettersGeometry}", (string?)image[1].Attribute("Geometry"));
        Assert.Equal("{StaticResource BrandMarkRingGeometry}", (string?)image[2].Attribute("Geometry"));

        var small = Drawings(keyed["BrandMarkSmallImage"]);
        Assert.Equal(new[] { "#FFFFFF", BrandPalette.MarkRing }, small.Select(d => Color(d)).ToArray());
    }

    /// <summary>
    /// Tokens.xaml ma wartości akcentu jasnego motywu (motyw startowy App.xaml); ThemeController podmienia je przy każdej zmianie motywu
    /// na <see cref="BrandPalette.For"/>. Znacznik menu ma osobny pędzel w kolorze <see cref="BrandPalette.NavMarker"/>.
    /// </summary>
    [Fact]
    public void Tokens_AccentDefaultsMatchLightPalette()
    {
        var tokens = Keyed(XDocument.Load(TokensPath));
        var light = BrandPalette.Light;

        Assert.Equal(light.Accent, tokens["BrandAccentColor"].Value.Trim().ToUpperInvariant());
        Assert.Equal(light.Accent, Color(tokens["BrandAccentBrush"]));
        Assert.Equal(light.Hover, Color(tokens["BrandAccentHoverBrush"]));
        Assert.Equal(light.Pressed, Color(tokens["BrandAccentPressedBrush"]));
        Assert.Equal(light.OnAccent, Color(tokens["BrandOnAccentBrush"]));
        Assert.Equal(BrandPalette.NavMarker, Color(tokens["NavMarkerBrush"]));
    }

    /// <summary>Pędzle akcentu zmieniają się z motywem, więc w XAML wolno je brać tylko przez DynamicResource (StaticResource zostałby przy starym kolorze).</summary>
    [Fact]
    public void AccentBrushes_OnlyDynamicResourceInXaml()
    {
        var appDir = RepoRoot.Combine("src", "DSOptimizer.App");
        var offenders = Directory.EnumerateFiles(appDir, "*.xaml", SearchOption.AllDirectories)
            .Where(path => path.IndexOf(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) < 0
                && path.IndexOf(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) < 0)
            .SelectMany(path => File.ReadAllLines(path)
                .Select((line, index) => (path, line, index))
                .Where(x => Regex.IsMatch(x.line, @"StaticResource\s+Brand(Accent|OnAccent)")))
            .Select(x => $"{Path.GetFileName(x.path)}:{x.index + 1}")
            .ToList();

        Assert.Empty(offenders);
    }

    private static int PngDimension(byte[] bytes, int offset) =>
        (bytes[offset] << 24) | (bytes[offset + 1] << 16) | (bytes[offset + 2] << 8) | bytes[offset + 3];

    private static Dictionary<string, XElement> Keyed(XDocument document) =>
        document.Root!.Elements()
            .Where(e => e.Attribute(Xaml + "Key") is not null)
            .ToDictionary(e => (string)e.Attribute(Xaml + "Key")!, StringComparer.Ordinal);

    private static XElement[] Drawings(XElement image) =>
        image.Descendants(Presentation + "GeometryDrawing").ToArray();

    private static string Color(XElement element)
    {
        var value = (string?)element.Attribute("Color") ?? (string?)element.Attribute("Brush");
        Assert.False(string.IsNullOrEmpty(value), $"Element {element.Name.LocalName} bez koloru podanego literalnie.");
        return value!.ToUpperInvariant();
    }
}
