using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using DSOptimizer.Tests.Unit.Support;
using Xunit;

namespace DSOptimizer.Tests.Unit.Repo;

/// <summary>
/// Strażnik wywołań tłumaczeń. Skrypt raportu wyciąga teksty regexem, więc każde <c>L.T(</c>/<c>L.F(</c>/<c>L.P(</c> w kodzie
/// i każde <c>{l:T '…'}</c> w XAML musi mieć jedną, prostą postać.
/// </summary>
public class TranslationCallRulesTests
{
    [Fact]
    public void TranslateCalls_UseSingleLiteral()
    {
        var violations = ScanRepo(".cs");
        Assert.True(violations.Count == 0, string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void TranslatedTexts_AreNotCapturedInStaticFields()
    {
        var violations = ScanRepo(".cs").Where(v => v.IndexOf(TranslationCallScanner.StaticFieldReason, StringComparison.Ordinal) >= 0).ToList();
        Assert.True(violations.Count == 0, string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void XamlTranslateExtension_UsesQuotedLiteral()
    {
        var violations = ScanRepo(".xaml");
        Assert.True(violations.Count == 0, string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void Scanner_FlagsConcatenation()
    {
        Assert.Single(Scan("var s = L.T(\"a\" + x);"));
    }

    [Fact]
    public void Scanner_FlagsInterpolated()
    {
        Assert.Single(Scan("var s = L.T($\"a{x}\");"));
    }

    [Fact]
    public void Scanner_FlagsVerbatim()
    {
        Assert.Single(Scan("var s = L.T(@\"a\");"));
        Assert.Single(Scan("var s = L.F(@\"a {0}\", x);"));
    }

    [Fact]
    public void Scanner_AcceptsEscapedQuote()
    {
        Assert.Empty(Scan("var s = L.T(\"a \\\"b\\\" c\");"));
        Assert.Empty(Scan("var s = L.F(\"a {0} \\\\\", x);"));
    }

    [Fact]
    public void Scanner_FlagsStaticReadonly()
    {
        Assert.Single(Scan("private static readonly string X = L.T(\"x\");"));
        Assert.Single(Scan("private const string Y = L.T(\"x\");"));
        Assert.Single(Scan("private static string Z = L.T(\"x\");"));
    }

    [Fact]
    public void Scanner_AcceptsExpressionBodiedProperty()
    {
        Assert.Empty(Scan("public static string X => L.T(\"x\");"));
    }

    [Fact]
    public void Scanner_AcceptsCallInMethodBody()
    {
        Assert.Empty(Scan("    static string M() { return L.T(\"x\"); }"));
        Assert.Empty(Scan("    var s = L.F(\"a {0}\", x);"));
    }

    [Fact]
    public void Scanner_IgnoresOtherIdentifiersAndComments()
    {
        Assert.Empty(Scan("var s = GL.T(x); var t = Foo.L.T(y);"));
        Assert.Empty(Scan("// L.T(x) w komentarzu"));
    }

    [Fact]
    public void Scanner_FlagsVariableAndMissingClosing()
    {
        Assert.Single(Scan("var s = L.T(name);"));
        Assert.Single(Scan("var s = L.T(\"abc);"));
        Assert.Single(Scan("var s = L.T(\"a\" );"));
    }

    [Fact]
    public void Scanner_ReportsFileAndLine()
    {
        var v = TranslationCallScanner.Violations("a.cs", "ok\nvar s = L.T(x);");
        Assert.StartsWith("a.cs:2:", Assert.Single(v), StringComparison.Ordinal);
    }

    [Fact]
    public void Scanner_Xaml_AcceptsQuotedLiteral()
    {
        Assert.Empty(ScanXaml("<TextBlock Text=\"{l:T 'Zastosuj'}\" />"));
        Assert.Empty(ScanXaml("<TextBlock Text=\"{l:T 'Nie \\'tak\\' }'}\" Tag=\"{l:T 'Dwa'}\" />"));
    }

    [Fact]
    public void Scanner_Xaml_FlagsOtherForms()
    {
        Assert.Single(ScanXaml("<TextBlock Text=\"{l:T Zastosuj}\" />"));
        Assert.Single(ScanXaml("<TextBlock Text=\"{l:T Text='x'}\" />"));
        Assert.Single(ScanXaml("<TextBlock Text=\"{l:T 'x', Foo=1}\" />"));
        Assert.Single(ScanXaml("<TextBlock Text=\"{l:T 'x\" />"));
        Assert.Single(ScanXaml("<TextBlock Text=\"{l:T}\" />"));
    }

    [Fact]
    public void Scanner_FlagsStaticAutoPropertyInitializer()
    {
        Assert.Single(Scan("public static string X { get; } = L.T(\"x\");"));
        Assert.Single(Scan("public static string Y { get; set; } = L.T(\"x\");"));
    }

    [Fact]
    public void Scanner_FlagsMultiLineStaticInitializer()
    {
        const string Text = """
            private static readonly (string, string)[] Items = {
                ("a", L.T("x")),
            };
            """;
        var v = Scan(Text);
        Assert.StartsWith("x.cs:2:", Assert.Single(v), StringComparison.Ordinal);
        const string Second = """
            private static string[] A =
                new[] { L.T("x") };
            """;
        Assert.Single(Scan(Second));
    }

    [Fact]
    public void Scanner_AcceptsCallInMethodAfterStaticField()
    {
        const string Text = """
            private static int Count = 3;
            private static readonly string Name = "n";
            public string M()
            {
                return L.T("x");
            }
            """;
        Assert.Empty(Scan(Text));
    }

    [Fact]
    public void Scanner_AcceptsKeywordInsideLiteral()
    {
        Assert.Empty(Scan("    var x = L.T(\"Use const here\");"));
        Assert.Empty(Scan("    var y = L.T(\"a\"); // static readonly"));
    }

    [Fact]
    public void Scanner_AcceptsPluralCall()
    {
        Assert.Empty(Scan("var s = L.P(\"{0} plik\", \"{0} pliki\", \"{0} plików\", n);"));
        Assert.Empty(Scan("var s = L.P(\"{0} plik\",\"{0} pliki\",  \"{0} \\\"plików\\\"\", list.Count);"));
    }

    [Fact]
    public void Scanner_FlagsPluralWithVariable()
    {
        Assert.Single(Scan("var s = L.P(one, \"x {0}\", \"y {0}\", n);"));
        Assert.Single(Scan("var s = L.P(\"a {0}\", few, \"y {0}\", n);"));
        Assert.Single(Scan("var s = L.P(\"a {0}\", \"b {0}\", $\"c {0}\", n);"));
        Assert.Single(Scan("var s = L.P(\"a {0}\", \"b {0}\" + x, \"c {0}\", n);"));
        Assert.Single(Scan("var s = L.P(\"a {0}\", \"b {0}\", \"c {0}\");"));
        Assert.Single(Scan("private static readonly string X = L.P(\"a {0}\", \"b {0}\", \"c {0}\", 1);"));
    }

    [Fact]
    public void DataCalls_OnlyInDataLoaders()
    {
        var violations = ScanRepo(".cs").Where(v => v.IndexOf(TranslationCallScanner.DataCallReason, StringComparison.Ordinal) >= 0).ToList();
        Assert.True(violations.Count == 0, string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void Scanner_FlagsDataCallOutsideLoaders()
    {
        var v = Assert.Single(Scan("var s = L.Data(text);"));
        Assert.Contains(TranslationCallScanner.DataCallReason, v, StringComparison.Ordinal);
        Assert.Single(TranslationCallScanner.Violations("src/DSOptimizer.Core/Gui/GuiSession.cs", "var s = L.Data(t.Title);"));
        Assert.Single(Scan("private static readonly string X = L.Data(y);"), x => x.Contains(TranslationCallScanner.StaticFieldReason));
        Assert.Empty(Scan("var s = XL.Data(text); // L.Data(x)"));
    }

    [Fact]
    public void Scanner_AcceptsDataCallInDataLoaders()
    {
        foreach (var file in TranslationCallScanner.DataCallFiles)
        {
            Assert.Empty(TranslationCallScanner.Violations(file, "    return L.Data(definition.Title);"));
        }

        Assert.Contains("src/DSOptimizer.Core/Catalog/CatalogTranslation.cs", TranslationCallScanner.DataCallFiles);
        Assert.Contains("src/DSOptimizer.Core/Scripts/ScriptWrapper.cs", TranslationCallScanner.DataCallFiles);
    }

    private static IReadOnlyList<string> Scan(string text) => TranslationCallScanner.Violations("x.cs", text);

    private static IReadOnlyList<string> ScanXaml(string text) => TranslationCallScanner.Violations("x.xaml", text);

    private static List<string> ScanRepo(string extension)
    {
        var src = RepoRoot.Combine("src");
        var result = new List<string>();
        var scanned = 0;
        foreach (var file in Directory.EnumerateFiles(src, "*" + extension, SearchOption.AllDirectories))
        {
            var relative = file.Substring(RepoRoot.Path.Length).TrimStart('\\', '/').Replace('\\', '/');
            var segments = relative.Split('/');
            if (segments.Contains("obj") || segments.Contains("bin"))
            {
                continue;
            }

            // L.cs to definicja; TExtension.cs dostaje tekst z XAML {l:T '…'}, który sprawdzają ten strażnik i raport braków.
            if (relative.EndsWith("Localization/L.cs", StringComparison.Ordinal)
                || relative.EndsWith("App/Gui/TExtension.cs", StringComparison.Ordinal))
            {
                continue;
            }

            scanned++;
            result.AddRange(TranslationCallScanner.Violations(relative, File.ReadAllText(file)));
        }

        Assert.True(scanned > 0, "Skan nie znalazl zadnego pliku " + extension + " w src.");
        return result;
    }
}

/// <summary>
/// Prosty skaner (tylko do testów), stanowy po liniach. C#: <c>L.T(</c>/<c>L.F(</c> musi dostać pierwszym argumentem jeden zwykły
/// literał <c>"…"</c> (z <c>\"</c> i <c>\\</c>), po nim od razu <c>)</c> albo <c>,</c>; <c>L.P(</c> – trzy takie literały w tej samej linii,
/// każdy z <c>,</c> zaraz po nim, potem czwarty argument (liczba). Zakaz wywołania (także <c>L.P</c>) wewnątrz instrukcji pola lub
/// właściwości statycznej z inicjalizatorem: od słowa <c>const</c>, <c>static readonly</c> albo <c>static … =</c> (nie <c>=&gt;</c>, więc też
/// <c>static T X { get; } = …</c>) do pierwszego <c>;</c> na zerowej głębokości nawiasów, także gdy instrukcja ma kilka linii.
/// Przed analizą z linii usuwana jest treść literałów tekstowych i znakowych oraz komentarze <c>//</c>, więc słowa kluczowe i wywołania w
/// tekście lub komentarzu nie liczą się. XAML: <c>{l:T '…'}</c> z literałem w apostrofach (<c>\'</c> i <c>\\</c> dozwolone) i <c>}</c> zaraz po nim.
/// Czego nie łapie: literału samego argumentu rozbitego na kilka linii, komentarzy blokowych (<c>/* */</c>), w pełni kwalifikowanego
/// <c>X.L.T(</c>, <c>L.T</c> przez alias, <c>using static</c> albo grupę metod, statycznego pola z krotką przed <c>=</c> bez <c>readonly</c>,
/// inicjalizatorów statycznych wywołanych pośrednio (np. przez metodę pomocniczą).
/// <c>L.Data</c> (wywołanie albo grupa metod) tylko w plikach z <see cref="DataCallFiles"/>, nigdy w polu statycznym.
/// </summary>
public static class TranslationCallScanner
{
    public const string StaticFieldReason = "static field";

    public const string DataCallReason = "L.Data outside data loaders";

    /// <summary>Jedyne pliki, w których wolno wołać <c>L.Data</c> (teksty z danych: katalog, listy aplikacji, komunikaty skryptów).</summary>
    public static IReadOnlyList<string> DataCallFiles { get; } = new[]
    {
        "src/DSOptimizer.Core/Catalog/CatalogTranslation.cs",
        "src/DSOptimizer.Core/Apps/AppsBloatCatalog.cs",
        "src/DSOptimizer.Core/Install/AppsInstallCatalog.cs",
        "src/DSOptimizer.Core/Scripts/ScriptWrapper.cs",
    };

    private static readonly Regex CallStart = new(@"(?<![\w.])L\.([TFP])\(", RegexOptions.Compiled);
    private static readonly Regex DataStart = new(@"(?<![\w.])L\.Data\b", RegexOptions.Compiled);
    private static readonly Regex StaticStart = new(@"\bconst\b|\bstatic\s+readonly\b|\bstatic\b[^=(]*=(?!>)", RegexOptions.Compiled);
    private static readonly Regex XamlStart = new(@"\{l:T(?!\w)", RegexOptions.Compiled);
    private static readonly Regex XamlValid = new(@"\G\{l:T '(?:[^'\\]|\\.)*'\}", RegexOptions.Compiled);

    public static IReadOnlyList<string> Violations(string fileName, string text)
    {
        var xaml = fileName.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase);
        var result = new List<string>();
        var lines = text.Split('\n');
        var inVerbatim = false;
        var inStatic = false;
        var depth = 0;
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].TrimEnd('\r');
            var where = fileName + ":" + (i + 1) + ": ";
            if (xaml)
            {
                CheckXaml(line, where, result);
                continue;
            }

            var code = Mask(line, ref inVerbatim);
            var ranges = StaticRanges(code, ref inStatic, ref depth);
            foreach (Match call in CallStart.Matches(code))
            {
                var name = "L." + call.Groups[1].Value;
                if (name == "L.P")
                {
                    if (!IsPluralLiterals(line, call.Index + call.Length))
                    {
                        result.Add(where + "L.P( must start with three plain \"literals\" separated by , and followed by , count");
                    }
                }
                else if (!IsSingleLiteralArgument(line, call.Index + call.Length))
                {
                    result.Add(where + name + "( must start with one plain \"literal\" followed by ) or ,");
                }

                if (ranges.Any(r => call.Index >= r.Start && call.Index < r.End))
                {
                    result.Add(where + name + "( in a " + StaticFieldReason + " or const (use a => property or a method body)");
                }
            }

            var dataAllowed = DataCallFiles.Contains(fileName, StringComparer.Ordinal);
            foreach (Match call in DataStart.Matches(code))
            {
                if (!dataAllowed)
                {
                    result.Add(where + DataCallReason + " (allowed only in: " + string.Join(", ", DataCallFiles) + ")");
                }

                if (ranges.Any(r => call.Index >= r.Start && call.Index < r.End))
                {
                    result.Add(where + "L.Data in a " + StaticFieldReason + " or const (use a => property or a method body)");
                }
            }
        }

        return result;
    }

    /// <summary>Zastępuje treść literałów spacjami (cudzysłowy zostają) i obcina komentarz <c>//</c>; długość linii się nie zmienia.</summary>
    private static string Mask(string line, ref bool inVerbatim)
    {
        var chars = line.ToCharArray();
        var i = 0;
        if (inVerbatim)
        {
            i = SkipVerbatim(chars, 0, ref inVerbatim);
        }

        while (i < chars.Length)
        {
            var c = chars[i];
            if (c == '/' && i + 1 < chars.Length && chars[i + 1] == '/')
            {
                for (var k = i; k < chars.Length; k++)
                {
                    chars[k] = ' ';
                }

                break;
            }

            if (c == '"')
            {
                var verbatim = (i > 0 && chars[i - 1] == '@') || (i > 1 && chars[i - 1] == '$' && chars[i - 2] == '@');
                if (verbatim)
                {
                    inVerbatim = true;
                    i = SkipVerbatim(chars, i + 1, ref inVerbatim);
                    continue;
                }

                i++;
                while (i < chars.Length && chars[i] != '"')
                {
                    var step = chars[i] == '\\' && i + 1 < chars.Length ? 2 : 1;
                    for (var k = 0; k < step; k++)
                    {
                        chars[i + k] = ' ';
                    }

                    i += step;
                }

                i++;
                continue;
            }

            if (c == '\'')
            {
                var j = i + 1;
                if (j < chars.Length && chars[j] == '\\')
                {
                    j += 2;
                }
                else
                {
                    j++;
                }

                if (j < chars.Length && chars[j] == '\'')
                {
                    for (var k = i + 1; k < j; k++)
                    {
                        chars[k] = ' ';
                    }

                    i = j + 1;
                    continue;
                }
            }

            i++;
        }

        return new string(chars);
    }

    private static int SkipVerbatim(char[] chars, int start, ref bool inVerbatim)
    {
        var i = start;
        while (i < chars.Length)
        {
            if (chars[i] == '"')
            {
                if (i + 1 < chars.Length && chars[i + 1] == '"')
                {
                    chars[i] = ' ';
                    chars[i + 1] = ' ';
                    i += 2;
                    continue;
                }

                inVerbatim = false;
                return i + 1;
            }

            chars[i] = ' ';
            i++;
        }

        return i;
    }

    /// <summary>Zakresy znaków tej linii leżące w instrukcji pola statycznego/const; stan przechodzi między liniami.</summary>
    private static List<(int Start, int End)> StaticRanges(string code, ref bool inStatic, ref int depth)
    {
        var ranges = new List<(int Start, int End)>();
        var pos = 0;
        while (pos <= code.Length)
        {
            int start;
            if (inStatic)
            {
                start = pos;
            }
            else
            {
                var match = StaticStart.Match(code, pos);
                if (!match.Success)
                {
                    break;
                }

                start = match.Index;
                inStatic = true;
                depth = 0;
            }

            var end = code.Length;
            var closed = false;
            for (var k = start; k < code.Length; k++)
            {
                var ch = code[k];
                if (ch == '{' || ch == '(' || ch == '[')
                {
                    depth++;
                }
                else if (ch == '}' || ch == ')' || ch == ']')
                {
                    depth--;
                }

                if ((ch == ';' && depth == 0) || depth < 0)
                {
                    end = k;
                    closed = true;
                    break;
                }
            }

            ranges.Add((start, end));
            if (!closed)
            {
                break;
            }

            inStatic = false;
            pos = end + 1;
        }

        return ranges;
    }

    private static bool IsSingleLiteralArgument(string line, int position)
    {
        var end = LiteralEnd(line, position);
        return end >= 0 && end < line.Length && (line[end] == ')' || line[end] == ',');
    }

    /// <summary><c>L.P(</c>: trzy zwykłe literały w tej samej linii, każdy zakończony <c>,</c> (po przecinku dowolne spacje), potem liczba.</summary>
    private static bool IsPluralLiterals(string line, int position)
    {
        for (var k = 0; k < 3; k++)
        {
            var end = LiteralEnd(line, position);
            if (end < 0 || end >= line.Length || line[end] != ',')
            {
                return false;
            }

            position = end + 1;
            while (position < line.Length && line[position] == ' ')
            {
                position++;
            }
        }

        return position < line.Length && line[position] != ')';
    }

    /// <summary>Pozycja tuż za zwykłym literałem <c>"…"</c> zaczynającym się w <paramref name="position"/>; -1, gdy go tam nie ma.</summary>
    private static int LiteralEnd(string line, int position)
    {
        if (position >= line.Length || line[position] != '"')
        {
            return -1;
        }

        for (var i = position + 1; i < line.Length; i++)
        {
            if (line[i] == '\\')
            {
                i++;
            }
            else if (line[i] == '"')
            {
                return i + 1;
            }
        }

        return -1;
    }

    private static void CheckXaml(string line, string where, List<string> result)
    {
        foreach (Match start in XamlStart.Matches(line))
        {
            if (!XamlValid.IsMatch(line, start.Index))
            {
                result.Add(where + "{l:T must be followed by one 'quoted literal' and }");
            }
        }
    }
}
