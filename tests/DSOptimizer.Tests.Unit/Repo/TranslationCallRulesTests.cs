using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using DSOptimizer.Tests.Unit.Support;
using Xunit;

namespace DSOptimizer.Tests.Unit.Repo;

/// <summary>
/// Strażnik wywołań tłumaczeń. Skrypt raportu wyciąga teksty regexem, więc każde <c>L.T(</c>/<c>L.F(</c> w kodzie
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

    private static IReadOnlyList<string> Scan(string text) => TranslationCallScanner.Violations("x.cs", text);

    private static IReadOnlyList<string> ScanXaml(string text) => TranslationCallScanner.Violations("x.xaml", text);

    private static List<string> ScanRepo(string extension)
    {
        var src = RepoRoot.Combine("src");
        var result = new List<string>();
        foreach (var file in Directory.EnumerateFiles(src, "*" + extension, SearchOption.AllDirectories))
        {
            var relative = file.Substring(RepoRoot.Path.Length).TrimStart((char)92, (char)47).Replace('\\', '/');
            var segments = relative.Split('/');
            if (segments.Contains("obj") || segments.Contains("bin"))
            {
                continue;
            }

            if (relative.EndsWith("Localization/L.cs", StringComparison.Ordinal))
            {
                continue;
            }

            result.AddRange(TranslationCallScanner.Violations(relative, File.ReadAllText(file)));
        }

        return result;
    }
}

/// <summary>
/// Prosty skaner liniowy (tylko do testów). C#: <c>L.T(</c>/<c>L.F(</c> musi dostać pierwszym argumentem jeden zwykły
/// literał <c>"…"</c> (z <c>\"</c> i <c>\\</c>), po nim od razu <c>)</c> albo <c>,</c>; zakaz w linii z <c>static readonly</c>/<c>const</c>
/// i w inicjalizatorze pola statycznego (<c>static T X = …L.T(</c>, nie <c>=&gt;</c>). XAML: <c>{l:T '…'}</c> z literałem w apostrofach
/// (<c>\'</c> i <c>\\</c> dozwolone) i <c>}</c> zaraz po nim.
/// Czego nie łapie: literału lub inicjalizatora rozbitego na kilka linii, wywołań w komentarzach blokowych (<c>/* */</c>) i w tekstach,
/// <c>L.T</c> przez alias lub <c>using static</c>, inicjalizatorów statycznych wywołanych pośrednio (np. przez metodę pomocniczą).
/// Linie zaczynające się od <c>//</c> są pomijane.
/// </summary>
public static class TranslationCallScanner
{
    public const string StaticFieldReason = "static field";

    private static readonly Regex CallStart = new(@"(?<![\w.])L\.([TF])\(", RegexOptions.Compiled);
    private static readonly Regex StaticInitializer = new(@"\bstatic\b[^=;(]*=(?!>)", RegexOptions.Compiled);
    private static readonly Regex StaticOrConst = new(@"\bstatic\s+readonly\b|\bconst\b", RegexOptions.Compiled);
    private static readonly Regex XamlStart = new(@"\{l:T(?!\w)", RegexOptions.Compiled);
    private static readonly Regex XamlValid = new(@"\G\{l:T '(?:[^'\\]|\\.)*'\}", RegexOptions.Compiled);

    public static IReadOnlyList<string> Violations(string fileName, string text)
    {
        var xaml = fileName.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase);
        var result = new List<string>();
        var lines = text.Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].TrimEnd('\r');
            var where = fileName + ":" + (i + 1) + ": ";
            if (xaml)
            {
                CheckXaml(line, where, result);
            }
            else if (!line.TrimStart().StartsWith("//", StringComparison.Ordinal))
            {
                CheckCs(line, where, result);
            }
        }

        return result;
    }

    private static void CheckCs(string line, string where, List<string> result)
    {
        foreach (Match call in CallStart.Matches(line))
        {
            var name = "L." + call.Groups[1].Value;
            if (!IsSingleLiteralArgument(line, call.Index + call.Length))
            {
                result.Add(where + name + "( must start with one plain \"literal\" followed by ) or ,");
            }

            if (StaticOrConst.IsMatch(line) || IsStaticInitializer(line, call.Index))
            {
                result.Add(where + name + "( in a " + StaticFieldReason + " or const (use a => property or a method body)");
            }
        }
    }

    private static bool IsStaticInitializer(string line, int callIndex)
    {
        var match = StaticInitializer.Match(line);
        return match.Success && match.Index + match.Length <= callIndex;
    }

    private static bool IsSingleLiteralArgument(string line, int position)
    {
        if (position >= line.Length || line[position] != '"')
        {
            return false;
        }

        for (var i = position + 1; i < line.Length; i++)
        {
            if (line[i] == '\\')
            {
                i++;
            }
            else if (line[i] == '"')
            {
                return i + 1 < line.Length && (line[i + 1] == ')' || line[i + 1] == ',');
            }
        }

        return false;
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
