using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using DSOptimizer.Tests.Unit.Support;
using Xunit;

namespace DSOptimizer.Tests.Unit.Repo;

/// <summary>
/// Strażnik polskich tekstów poza tłumaczeniem. Polski literał w C# (poza <c>L.T</c>/<c>L.F</c>/<c>L.P</c>) albo zwykły tekst w XAML
/// (bez <c>{l:T '…'}</c>) jest błędem, chyba że plik obejmuje wpis z <c>polish-literals-pending.txt</c> (folder z <c>/</c> na końcu
/// albo dokładny plik; obszar jeszcze nie przetłumaczony) albo literał jest w <c>polish-literals-allowed.txt</c> (celowo po polsku, z powodem).
/// </summary>
public class PolishLiteralRulesTests
{
    private static readonly string RepoListDirectory = Path.Combine("tests", "DSOptimizer.Tests.Unit", "Repo");

    [Fact]
    public void Flags_PolishLiteralOutsideL()
    {
        Assert.Single(Scan("var s = \"Usunięto plik\";"));
        Assert.Single(Scan("var s = $\"Usunięto {n} plików\";"));
        Assert.Single(Scan("var s = @\"Ścieżka\";"));
        Assert.StartsWith("a.cs:2: \"Usunięto\"", Assert.Single(Scan("var a = 1;\nlog.Info(\"Usunięto\");")), StringComparison.Ordinal);
    }

    [Fact]
    public void Flags_PolishWordWithoutDiacritics()
    {
        Assert.Single(Scan("var s = \"Zastosuj\";"));
        Assert.Single(Scan("var s = \"to nie dziala\";"));
        Assert.Empty(Scan("var s = \"Applied\"; var t = \"nieznany-id\"; var u = \"Niebieski\";"));
    }

    [Fact]
    public void Ignores_Comment()
    {
        Assert.Empty(Scan("// \"Usunięto\"\n/// <summary>„Zastosuj”</summary>\n/* \"Cofnij\"\n \"Zamknij\" */ var x = 1;"));
        Assert.Single(Scan("var u = \"http://x\"; var s = \"Ścieżka\"; // \"Nie\""));
    }

    [Fact]
    public void Ignores_InsideTranslateCall()
    {
        Assert.Empty(Scan("var s = L.T(\"Zastosuj\"); var t = L.F(\"Usunięto {0}\", n);"));
        Assert.Empty(Scan("var s = L.P(\"{0} plik\", \"{0} pliki\", \"{0} plików\", n);"));
        Assert.Empty(Scan("var s = $\"{L.T(\"Tak\")} – {n}\";"));
        Assert.Single(Scan("var s = L.F(\"Plik {0}\", \"Brak\");"));
        Assert.Single(Scan("var s = $\"{(ok ? \"Tak\" : \"x\")}\";"));
        Assert.Single(Scan("var s = L.T(name) + \"Nie\";"));
    }

    [Fact]
    public void Ignores_ArgumentException()
    {
        Assert.Empty(Scan("throw new ArgumentException(\"Forma bez {0}.\", nameof(one));"));
        Assert.Empty(Scan("    throw new ArgumentNullException(nameof(x), \"Brak wartości\");"));
        Assert.Empty(Scan("throw new ArgumentOutOfRangeException(nameof(x), \"Zła liczba\");"));
        Assert.Single(Scan("throw new InvalidOperationException(\"Zły stan\");"));
    }

    [Fact]
    public void Ignores_AllowedLiteral()
    {
        var allowed = AllowedLiteral.Parse("a.cs\t\"Bezpiecznik testów\"\tbezpiecznik testów\n");
        Assert.Empty(PolishLiteralScanner.Violations("a.cs", "var s = \"Bezpiecznik testów\";", allowed));
        Assert.Single(PolishLiteralScanner.Violations("a.cs", "var s = \"Bezpiecznik testów!\";", allowed));
        Assert.Single(PolishLiteralScanner.Violations("b.cs", "var s = \"Bezpiecznik testów\";", allowed));
    }

    [Fact]
    public void Flags_StaleAllowedEntry()
    {
        var allowed = AllowedLiteral.Parse("# komentarz\na.cs\t\"Już nie ma\"\tpowód\na.cs\t\"Plik jest\"\tpowód\nb.cs\t\"Brak pliku\"\tpowód\n");
        var files = new Dictionary<string, string>(StringComparer.Ordinal) { ["a.cs"] = "var s = \"Plik jest\"; var t = L.T(\"Już nie ma\");" };

        var stale = PolishLiteralScanner.StaleAllowed(allowed, p => files.TryGetValue(p, out var text) ? text : null);

        Assert.Equal(2, stale.Count);
        Assert.Contains(stale, s => s.IndexOf("Już nie ma", StringComparison.Ordinal) >= 0);
        Assert.Contains(stale, s => s.IndexOf("b.cs", StringComparison.Ordinal) >= 0);
        Assert.Throws<FormatException>(() => AllowedLiteral.Parse("a.cs\t\"x\"\n"));
        Assert.Throws<FormatException>(() => AllowedLiteral.Parse("a.cs\t\"x\"\t \n"));
    }

    [Fact]
    public void Xaml_FlagsPlainText()
    {
        Assert.StartsWith("a.xaml:1: Szukaj", Assert.Single(ScanXaml("<TextBox Text=\"Szukaj\" />")), StringComparison.Ordinal);
        Assert.Single(ScanXaml("<Button Content=\"Save\" />"));
        Assert.Single(ScanXaml("<Button AutomationProperties.Name=\"Odśwież\" />"));
        Assert.Single(ScanXaml("<TextBlock Text=\"{}{0} pozycji\" />"));
        Assert.Equal(2, ScanXaml("<Grid>\n  <TextBlock>Pobierz <Hyperlink>stąd</Hyperlink></TextBlock>\n</Grid>").Count);
        Assert.Single(ScanXaml("<TextBlock><Run>Tekst</Run></TextBlock>"));
    }

    [Fact]
    public void Xaml_IgnoresBindingAndTranslate()
    {
        Assert.Empty(ScanXaml("<TextBlock Text=\"{Binding Title}\" ToolTip=\"{l:T 'Kopiuj'}\" Tag=\"Zwykły tag\" />"));
        Assert.Empty(ScanXaml("<Button Content=\"&#xE72C;\" x:Name=\"Szukaj\" Header=\"–\" />"));
        Assert.Empty(ScanXaml("<!-- <TextBlock Text=\"Komentarz\" /> --><Geometry>M3,4 L5,6</Geometry>"));
    }

    [Fact]
    public void Xaml_IgnoresProperName()
    {
        Assert.Empty(ScanXaml("<TextBlock Text=\"DSOptimizer\" Title=\"DŚO\" />"));
        Assert.Empty(ScanXaml("<TextBlock Text=\"© DSOptimizer\" ToolTip=\"GitHub\" />"));
        Assert.Empty(ScanXaml("<RadioButton Content=\"Polski\" /><RadioButton Content=\"English\" /><TextBlock Text=\"winget\" />"));
        Assert.Single(ScanXaml("<TextBlock Text=\"DSOptimizer – wersja\" />"));
    }

    [Fact]
    public void Xaml_FlagsTranslateInThemes()
    {
        const string Style = "<Style>\n  <Setter Property=\"ToolTip\" Value=\"{l:T 'Kopiuj'}\" />\n</Style>";
        Assert.StartsWith("src/DSOptimizer.App/Themes/Tokens.xaml:2:", Assert.Single(PolishLiteralScanner.ThemeViolations("src/DSOptimizer.App/Themes/Tokens.xaml", Style)), StringComparison.Ordinal);
        Assert.Single(PolishLiteralScanner.ThemeViolations("src/DSOptimizer.App/App.xaml", Style));
        Assert.Empty(PolishLiteralScanner.ThemeViolations("src/DSOptimizer.App/Themes/Tokens.xaml", "<!-- {l:T 'x'} --><Style />"));
        Assert.Empty(PolishLiteralScanner.ThemeViolations("src/DSOptimizer.App/MainWindow.xaml", Style));
    }

    [Fact]
    public void Scanner_HandlesRawAndVerbatimStrings()
    {
        Assert.Empty(Scan("var s = \"\"\"\n  Get-AppxPackage -User $UserSid\n  \"\"\";\nvar t = @\"C:\\a \"\"b\"\"\";"));
        Assert.Single(Scan("var s = \"\"\"\n  Usuń pakiet\n  \"\"\";"));
        Assert.Empty(Scan("var c = '\"'; var d = '\\''; var s = \"a\\\"b\";"));
        Assert.Single(Scan("var s = $@\"{x} {{ścieżka}}\";"));
    }

    [Fact]
    public void CSharp_NoPolishLiteralsOutsidePending()
    {
        var allowed = LoadAllowed();
        var violations = ScanRepo(".cs", (path, text) => PolishLiteralScanner.Violations(path, text, allowed));
        violations.AddRange(PolishLiteralScanner.StaleAllowed(allowed.Where(a => a.Path.EndsWith(".cs", StringComparison.Ordinal)), ReadRepoFile));
        Assert.True(violations.Count == 0, string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void Xaml_NoPlainTextOutsidePending()
    {
        var allowed = LoadAllowed();
        var violations = ScanRepo(".xaml", (path, text) => PolishLiteralScanner.Violations(path, text, allowed));
        violations.AddRange(PolishLiteralScanner.StaleAllowed(allowed.Where(a => a.Path.EndsWith(".xaml", StringComparison.Ordinal)), ReadRepoFile));
        Assert.True(violations.Count == 0, string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void Themes_HaveNoTranslateExtension()
    {
        var violations = ScanRepo(".xaml", PolishLiteralScanner.ThemeViolations, applyPending: false);
        Assert.True(violations.Count == 0, string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void Pending_FolderIsPrefixFileIsExact()
    {
        Assert.True(PolishLiteralScanner.PendingCovers("src/A/", "src/A/B/C.cs"));
        Assert.False(PolishLiteralScanner.PendingCovers("src/A/", "src/AB/C.cs"));
        Assert.True(PolishLiteralScanner.PendingCovers("src/A/MainWindow.xaml", "src/A/MainWindow.xaml"));
        Assert.False(PolishLiteralScanner.PendingCovers("src/A/MainWindow.xaml", "src/A/MainWindow.xaml.cs"));
    }

    [Fact]
    public void Pending_EntriesExistOnDisk()
    {
        var pending = LoadPending();
        var problems = new List<string>();
        foreach (var prefix in pending)
        {
            var full = RepoRoot.Combine(prefix.TrimEnd('/').Replace('/', Path.DirectorySeparatorChar));
            var exists = prefix.EndsWith("/", StringComparison.Ordinal) ? Directory.Exists(full) : File.Exists(full);
            if (!exists || !prefix.StartsWith("src/", StringComparison.Ordinal))
            {
                problems.Add("Brak na dysku albo poza src/: " + prefix);
            }
        }

        problems.AddRange(pending.GroupBy(p => p, StringComparer.Ordinal).Where(g => g.Count() > 1).Select(g => "Powtórzony wpis: " + g.Key));
        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }

    private static IReadOnlyList<string> Scan(string text) => PolishLiteralScanner.Violations("a.cs", text, Array.Empty<AllowedLiteral>());

    private static IReadOnlyList<string> ScanXaml(string text) =>
        PolishLiteralScanner.Violations(
            "a.xaml",
            "<Root xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\">" + text + "</Root>",
            Array.Empty<AllowedLiteral>());

    private static string? ReadRepoFile(string relative)
    {
        var full = RepoRoot.Combine(relative.Replace('/', Path.DirectorySeparatorChar));
        return File.Exists(full) ? File.ReadAllText(full) : null;
    }

    private static IReadOnlyList<AllowedLiteral> LoadAllowed() =>
        AllowedLiteral.Parse(File.ReadAllText(RepoRoot.Combine(RepoListDirectory, "polish-literals-allowed.txt"), Encoding.UTF8));

    private static List<string> LoadPending() =>
        File.ReadAllText(RepoRoot.Combine(RepoListDirectory, "polish-literals-pending.txt"), Encoding.UTF8)
            .Split('\n')
            .Select(l => l.Trim())
            .Where(l => l.Length > 0 && !l.StartsWith("#", StringComparison.Ordinal))
            .ToList();

    private static List<string> ScanRepo(string extension, Func<string, string, IReadOnlyList<string>> scan, bool applyPending = true)
    {
        var pending = applyPending ? LoadPending() : new List<string>();
        var result = new List<string>();
        var scanned = 0;
        foreach (var file in Directory.EnumerateFiles(RepoRoot.Combine("src"), "*" + extension, SearchOption.AllDirectories))
        {
            var relative = file.Substring(RepoRoot.Path.Length).TrimStart('\\', '/').Replace('\\', '/');
            var segments = relative.Split('/');
            if (segments.Contains("obj") || segments.Contains("bin") || !relative.EndsWith(extension, StringComparison.Ordinal))
            {
                continue;
            }

            scanned++;
            if (pending.Any(p => PolishLiteralScanner.PendingCovers(p, relative)))
            {
                continue;
            }

            result.AddRange(scan(relative, File.ReadAllText(file)));
        }

        Assert.True(scanned > 0, "Skan nie znalazl zadnego pliku " + extension + " w src.");
        return result;
    }
}

/// <summary>Znaleziony tekst: numer linii i literał (C#: dokładnie jak w źródle, z cudzysłowami; XAML: wartość po rozkodowaniu XML).</summary>
public sealed class PolishLiteralHit
{
    public PolishLiteralHit(int line, string literal)
    {
        Line = line;
        Literal = literal;
    }

    public int Line { get; }

    public string Literal { get; }
}

/// <summary>Wpis <c>polish-literals-allowed.txt</c>: <c>ścieżka&lt;TAB&gt;literał&lt;TAB&gt;powód</c>; <c>#</c> = komentarz.</summary>
public sealed class AllowedLiteral
{
    public AllowedLiteral(string path, string literal, string reason)
    {
        Path = path;
        Literal = literal;
        Reason = reason;
    }

    public string Path { get; }

    public string Literal { get; }

    public string Reason { get; }

    public static IReadOnlyList<AllowedLiteral> Parse(string content)
    {
        var result = new List<AllowedLiteral>();
        var lines = content.Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].TrimEnd('\r');
            if (line.Trim().Length == 0 || line.TrimStart().StartsWith("#", StringComparison.Ordinal))
            {
                continue;
            }

            var fields = line.Split('\t');
            if (fields.Length != 3 || fields[0].Trim().Length == 0 || fields[1].Length == 0 || fields[2].Trim().Length == 0)
            {
                throw new FormatException("Linia " + (i + 1) + ": oczekiwano ścieżka<TAB>literał<TAB>powód: " + line);
            }

            result.Add(new AllowedLiteral(fields[0].Trim(), fields[1], fields[2].Trim()));
        }

        return result;
    }
}

/// <summary>
/// Skaner (tylko do testów). C#: leksyk całego pliku – komentarze <c>//</c>, <c>///</c>, <c>/* */</c>, literały znakowe, zwykłe,
/// dosłowne (<c>@"…"</c>), interpolowane (<c>$"…"</c>, <c>$@"…"</c>, dziury jako kod z własnymi literałami) i surowe (<c>"""…"""</c>).
/// Polski = tekst literału (dziury interpolacji pominięte) z polską literą albo słowem z <see cref="PolishWords"/> (granice <c>\b</c>,
/// wielkość liter istotna). Pomijane: pierwszy argument <c>L.T(</c>/<c>L.F(</c>, trzy pierwsze <c>L.P(</c> oraz literały w linii
/// z <c>throw new Argument(Null|OutOfRange)?Exception(</c>. Czego nie łapie: tekstu zapisanego sekwencją <c>\x</c>, wywołania
/// <c>L.T</c> przez alias albo <c>using static</c>.
/// XAML: atrybuty z <see cref="TextAttributes"/> i tekst wewnątrz <c>TextBlock</c>/<c>Run</c>/<c>Hyperlink</c> z literą, poza
/// rozszerzeniem <c>{…}</c> (prefiks <c>{}</c> to zwykły tekst) i nazwami własnymi <see cref="XamlProperNames"/>.
/// </summary>
public static class PolishLiteralScanner
{
    public static readonly string[] PolishWords =
    {
        "nie", "Nie", "jest", "oraz", "lub", "albo", "przez", "dla", "jako", "Tak", "Brak", "Zastosuj", "Uruchom", "Cofnij", "Zamknij",
        "Anuluj", "Szukaj", "Wszystkie", "Pozycje", "Dalej", "Odczyt", "Zaznaczone",
    };

    public static readonly string[] XamlProperNames = { "DSOptimizer", "GitHub", "winget", "Polski", "English", "DŚO" };

    private static readonly string[] TextAttributes =
    {
        "Text", "Content", "Header", "ToolTip", "Title", "PlaceholderText", "AutomationProperties.Name", "AutomationProperties.HelpText",
    };

    private static readonly string[] TextElements = { "TextBlock", "Run", "Hyperlink" };

    private static readonly Regex Polish = new(
        "[ąćęłńóśźżĄĆĘŁŃÓŚŹŻ]|\\b(?:" + string.Join("|", PolishWords) + ")\\b", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex Letter = new("[A-Za-ząćęłńóśźżĄĆĘŁŃÓŚŹŻ]", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex ArgumentThrow = new(@"throw new Argument(?:Null|OutOfRange)?Exception\(", RegexOptions.Compiled);

    private static readonly Regex XamlComment = new(@"<!--[\s\S]*?-->", RegexOptions.Compiled);

    public static bool IsPolish(string text) => Polish.IsMatch(text);

    /// <summary>Wpis <c>pending</c> z <c>/</c> na końcu obejmuje wszystko pod folderem, bez niego – dokładnie ten plik.</summary>
    public static bool PendingCovers(string entry, string relativePath) =>
        entry.EndsWith("/", StringComparison.Ordinal)
            ? relativePath.StartsWith(entry, StringComparison.Ordinal)
            : string.Equals(entry, relativePath, StringComparison.Ordinal);

    /// <summary>Naruszenia w jednym pliku (<c>ścieżka:linia: literał</c>) po odjęciu wyjątków z <paramref name="allowed"/> dla tej ścieżki.</summary>
    public static IReadOnlyList<string> Violations(string path, string text, IEnumerable<AllowedLiteral> allowed)
    {
        var here = new HashSet<string>(allowed.Where(a => string.Equals(a.Path, path, StringComparison.Ordinal)).Select(a => a.Literal), StringComparer.Ordinal);
        return Hits(path, text)
            .Where(h => !here.Contains(h.Literal))
            .Select(h => path + ":" + h.Line + ": " + h.Literal)
            .ToList();
    }

    /// <summary>Wyjątki, których literału nie ma już wśród znalezionych tekstów pliku (albo pliku nie ma) – lista nie może gnić.</summary>
    public static IReadOnlyList<string> StaleAllowed(IEnumerable<AllowedLiteral> allowed, Func<string, string?> readFile)
    {
        var result = new List<string>();
        foreach (var group in allowed.GroupBy(a => a.Path, StringComparer.Ordinal))
        {
            var text = readFile(group.Key);
            var present = text is null
                ? new HashSet<string>(StringComparer.Ordinal)
                : new HashSet<string>(Hits(group.Key, text).Select(h => h.Literal), StringComparer.Ordinal);
            foreach (var entry in group.Where(e => !present.Contains(e.Literal)))
            {
                result.Add("Nieaktualny wyjątek (brak literału w pliku): " + entry.Path + "\t" + entry.Literal);
            }
        }

        return result;
    }

    /// <summary>Polskie albo zwykłe teksty pliku przed odjęciem wyjątków (C# albo XAML po rozszerzeniu ścieżki).</summary>
    public static IReadOnlyList<PolishLiteralHit> Hits(string path, string text) =>
        path.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase) ? Xaml(text) : CSharp(text);

    public static IReadOnlyList<PolishLiteralHit> CSharp(string text)
    {
        var lines = text.Split('\n');
        return CSharpLexer.Lex(text)
            .Where(l => !l.InTranslateCall && IsPolish(l.Text) && !ArgumentThrow.IsMatch(lines[l.Line - 1]))
            .Select(l => new PolishLiteralHit(l.Line, l.Raw))
            .ToList();
    }

    public static IReadOnlyList<PolishLiteralHit> Xaml(string text)
    {
        var result = new List<PolishLiteralHit>();
        var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null };
        using var reader = XmlReader.Create(new StringReader(text), settings);
        var info = (IXmlLineInfo)reader;
        var open = new List<string>();
        while (reader.Read())
        {
            switch (reader.NodeType)
            {
                case XmlNodeType.Element:
                    var name = reader.LocalName;
                    var empty = reader.IsEmptyElement;
                    if (reader.MoveToFirstAttribute())
                    {
                        do
                        {
                            if (TextAttributes.Contains(reader.Name) && IsPlainXamlText(reader.Value))
                            {
                                result.Add(new PolishLiteralHit(info.LineNumber, reader.Value));
                            }
                        }
                        while (reader.MoveToNextAttribute());

                        reader.MoveToElement();
                    }

                    if (!empty)
                    {
                        open.Add(name);
                    }

                    break;
                case XmlNodeType.EndElement:
                    open.RemoveAt(open.Count - 1);
                    break;
                case XmlNodeType.Text:
                case XmlNodeType.CDATA:
                    var value = reader.Value.Trim();
                    if (open.Any(TextElements.Contains) && IsPlainXamlText(value))
                    {
                        result.Add(new PolishLiteralHit(info.LineNumber, value));
                    }

                    break;
            }
        }

        return result;
    }

    /// <summary><c>App.xaml</c> i <c>Themes/*.xaml</c> nie używają <c>{l:T</c> – style powstają, zanim jest wybrany język.</summary>
    public static IReadOnlyList<string> ThemeViolations(string path, string text)
    {
        var segments = path.Split('/');
        if (!(segments.Contains("Themes") || string.Equals(segments[segments.Length - 1], "App.xaml", StringComparison.Ordinal)))
        {
            return Array.Empty<string>();
        }

        var lines = XamlComment.Replace(text, m => Regex.Replace(m.Value, "[^\r\n]", " ")).Split('\n');
        var result = new List<string>();
        for (var i = 0; i < lines.Length; i++)
        {
            if (lines[i].IndexOf("{l:T", StringComparison.Ordinal) >= 0)
            {
                result.Add(path + ":" + (i + 1) + ": {l:T in a style file (styles are created before the language is chosen)");
            }
        }

        return result;
    }

    private static bool IsPlainXamlText(string value)
    {
        var text = value;
        if (text.StartsWith("{}", StringComparison.Ordinal))
        {
            text = text.Substring(2);
        }
        else if (text.StartsWith("{", StringComparison.Ordinal))
        {
            return false;
        }

        foreach (var name in XamlProperNames)
        {
            text = text.Replace(name, string.Empty);
        }

        return Letter.IsMatch(text);
    }
}

/// <summary>Literał C# znaleziony przez <see cref="CSharpLexer"/>.</summary>
public sealed class CSharpLiteral
{
    public CSharpLiteral(int line, string raw, string text, bool inTranslateCall)
    {
        Line = line;
        Raw = raw;
        Text = text;
        InTranslateCall = inTranslateCall;
    }

    /// <summary>Linia początku literału (od 1).</summary>
    public int Line { get; }

    /// <summary>Literał dokładnie jak w źródle, z przedrostkiem i cudzysłowami.</summary>
    public string Raw { get; }

    /// <summary>Treść: proste sekwencje ucieczki rozkodowane, dziura interpolacji = <c>{}</c>.</summary>
    public string Text { get; }

    /// <summary>Pierwszy argument <c>L.T(</c>/<c>L.F(</c> albo jeden z trzech pierwszych <c>L.P(</c>.</summary>
    public bool InTranslateCall { get; }
}

/// <summary>Uproszczony leksyk C# (tylko do testów): literały tekstowe całego pliku, z dziurami interpolacji czytanymi jako kod.</summary>
public sealed class CSharpLexer
{
    private static readonly Regex TranslateCall = new(@"\GL\.([TFP])\(", RegexOptions.Compiled);

    private readonly string _s;
    private readonly List<int> _newlines = new();
    private readonly List<CSharpLiteral> _found = new();
    private int _i;

    private CSharpLexer(string text)
    {
        _s = text;
        for (var k = 0; k < text.Length; k++)
        {
            if (text[k] == '\n')
            {
                _newlines.Add(k);
            }
        }
    }

    public static IReadOnlyList<CSharpLiteral> Lex(string text)
    {
        var lexer = new CSharpLexer(text);
        lexer.Code(inHole: false);
        return lexer._found;
    }

    private char At(int offset) => _i + offset < _s.Length ? _s[_i + offset] : '\0';

    private int LineAt(int position)
    {
        var index = _newlines.BinarySearch(position);
        return (index < 0 ? ~index : index) + 1;
    }

    /// <summary>Kod do końca pliku albo (w dziurze interpolacji) do zamykającego <c>}</c>, którego nie zjada.</summary>
    private void Code(bool inHole)
    {
        var pending = 0;
        var braces = 0;
        var parens = 0;
        while (_i < _s.Length)
        {
            var c = _s[_i];
            if (c == '/' && At(1) == '/')
            {
                while (_i < _s.Length && _s[_i] != '\n')
                {
                    _i++;
                }

                continue;
            }

            if (c == '/' && At(1) == '*')
            {
                var end = _s.IndexOf("*/", _i + 2, StringComparison.Ordinal);
                _i = end < 0 ? _s.Length : end + 2;
                continue;
            }

            if (StringStarts())
            {
                ReadString(pending > 0);
                if (pending > 0)
                {
                    pending--;
                }

                continue;
            }

            if (c == '\'')
            {
                SkipChar();
                pending = 0;
                continue;
            }

            if (inHole)
            {
                if (c == '{')
                {
                    braces++;
                }
                else if (c == '}')
                {
                    if (braces == 0)
                    {
                        return;
                    }

                    braces--;
                }
                else if (c is '(' or '[')
                {
                    parens++;
                }
                else if (c is ')' or ']')
                {
                    parens--;
                }
                else if (c == ':' && braces == 0 && parens == 0 && At(1) != ':' && (_i == 0 || _s[_i - 1] != ':'))
                {
                    while (_i < _s.Length && _s[_i] != '}' && _s[_i] != '"')
                    {
                        _i++;
                    }

                    return;
                }
            }

            var call = c == 'L' && (_i == 0 || !(char.IsLetterOrDigit(_s[_i - 1]) || _s[_i - 1] == '_' || _s[_i - 1] == '.'))
                ? TranslateCall.Match(_s, _i)
                : Match.Empty;
            if (call.Success)
            {
                pending = call.Groups[1].Value == "P" ? 3 : 1;
                _i += call.Length;
                continue;
            }

            if (!char.IsWhiteSpace(c) && c != ',')
            {
                pending = 0;
            }

            _i++;
        }
    }

    private bool StringStarts()
    {
        var k = _i;
        while (k < _s.Length && (_s[k] == '$' || _s[k] == '@'))
        {
            k++;
        }

        if (k >= _s.Length || _s[k] != '"')
        {
            return false;
        }

        // '@' albo '$' bez cudzysłowu zaraz po nich to identyfikator (@class) albo coś innego; przedrostek musi stać na granicy słowa.
        return _i == 0 || !(char.IsLetterOrDigit(_s[_i - 1]) || _s[_i - 1] == '_');
    }

    private void ReadString(bool inTranslateCall)
    {
        var start = _i;
        var dollars = 0;
        var verbatim = false;
        while (_s[_i] is '$' or '@')
        {
            if (_s[_i] == '$')
            {
                dollars++;
            }
            else
            {
                verbatim = true;
            }

            _i++;
        }

        var quotes = 0;
        while (_i + quotes < _s.Length && _s[_i + quotes] == '"')
        {
            quotes++;
        }

        // Dosłowny @"""…" to nie surowy literał: "" w nim oznacza cudzysłów.
        var text = new StringBuilder();
        if (quotes >= 3 && !verbatim)
        {
            _i += quotes;
            ReadRaw(quotes, dollars, text);
        }
        else
        {
            _i++;
            ReadQuoted(verbatim, dollars > 0, text);
        }

        _found.Add(new CSharpLiteral(LineAt(start), _s.Substring(start, _i - start), text.ToString(), inTranslateCall));
    }

    private void ReadQuoted(bool verbatim, bool interpolated, StringBuilder text)
    {
        while (_i < _s.Length)
        {
            var c = _s[_i];
            if (c == '\\' && !verbatim)
            {
                var next = At(1);
                text.Append(next is '"' or '\'' or '\\' ? next : ' ');
                _i += 2;
                continue;
            }

            if (c == '"')
            {
                if (verbatim && At(1) == '"')
                {
                    text.Append('"');
                    _i += 2;
                    continue;
                }

                _i++;
                return;
            }

            if (interpolated && c == '{')
            {
                if (At(1) == '{')
                {
                    text.Append("{{");
                    _i += 2;
                    continue;
                }

                _i++;
                text.Append("{}");
                Code(inHole: true);
                if (_i < _s.Length && _s[_i] == '}')
                {
                    _i++;
                }

                continue;
            }

            if (c == '\n' && !verbatim)
            {
                return;
            }

            text.Append(c);
            _i++;
        }
    }

    private void ReadRaw(int quotes, int dollars, StringBuilder text)
    {
        while (_i < _s.Length)
        {
            var c = _s[_i];
            if (c == '"')
            {
                var run = 0;
                while (_i + run < _s.Length && _s[_i + run] == '"')
                {
                    run++;
                }

                _i += run;
                if (run >= quotes)
                {
                    return;
                }

                text.Append('"', run);
                continue;
            }

            if (dollars > 0 && c == '{')
            {
                var run = 0;
                while (_i + run < _s.Length && _s[_i + run] == '{')
                {
                    run++;
                }

                _i += run;
                if (run < dollars)
                {
                    text.Append('{', run);
                    continue;
                }

                text.Append('{', run - dollars).Append("{}");
                Code(inHole: true);
                for (var k = 0; k < dollars && _i < _s.Length && _s[_i] == '}'; k++)
                {
                    _i++;
                }

                continue;
            }

            text.Append(c);
            _i++;
        }
    }

    private void SkipChar()
    {
        _i++;
        if (_i < _s.Length && _s[_i] == '\\')
        {
            _i += 2;
        }
        else
        {
            _i++;
        }

        while (_i < _s.Length && _s[_i] != '\'' && _s[_i] != '\n')
        {
            _i++;
        }

        if (_i < _s.Length && _s[_i] == '\'')
        {
            _i++;
        }
    }
}
