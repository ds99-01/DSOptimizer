using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using DSOptimizer.Tests.Unit.Support;
using Newtonsoft.Json.Linq;
using Xunit;

namespace DSOptimizer.Tests.Unit.Repo;

/// <summary>
/// Strażnik komunikatów skryptów katalogu. Polski napis w polu <c>script.*</c> (<c>apply</c>, <c>undo</c>, <c>detect</c>, <c>capture</c>)
/// pozycji z <c>src/DSOptimizer.Catalog/catalog/*.json</c> (bez <c>apps*.json</c>) musi być pierwszym argumentem <c>Get-DsoText</c>,
/// chyba że plik katalogu jest w <c>script-literals-pending.txt</c> (jeszcze nie przerobiony) albo para pozycja–literał jest
/// w <c>script-literals-allowed.txt</c> (wzorzec porównywany z wyjściem Windows). <c>Get-DsoText</c> bez literału w apostrofach
/// w tej samej linii jest błędem zawsze – wrapper zbiera teksty do tłumaczenia wyłącznie z takich literałów.
/// </summary>
public class CatalogScriptTextRulesTests
{
    private const string CatalogDirectory = "src/DSOptimizer.Catalog/catalog";

    private static readonly string RepoListDirectory = Path.Combine("tests", "DSOptimizer.Tests.Unit", "Repo");

    private static readonly string[] ScriptFields = { "apply", "undo", "detect", "capture" };

    [Fact]
    public void Flags_PolishLiteralOutsideText()
    {
        Assert.StartsWith("x:2: 'Usunięto plik.'", Assert.Single(Scan("$a = 1\nWrite-Output 'Usunięto plik.'")), StringComparison.Ordinal);
        Assert.Single(Scan("throw ('Nie znaleziono {0}' -f $x)"));
        Assert.Single(Scan("Write-Output 'Plik ''x'' nie istnieje'"));
        Assert.Empty(Scan("Write-Output 'Applied'; $k = 'HKLM:\\SOFTWARE\\X'"));
    }

    [Fact]
    public void Flags_DoubleQuotedPolish()
    {
        Assert.Single(Scan("Write-Output \"Usunięto $count plików\""));
        Assert.Single(Scan("Write-Output \"a `\" b $(if ($x) { 'Brak' } else { 'x' }) c\""));
        Assert.Single(Scan("Write-Output \"Plik: $path – usunięty\""));
        Assert.Empty(Scan("Write-Output \"Value $($x.Name) = `\"ok`\"\""));
    }

    [Fact]
    public void Ignores_Comment()
    {
        Assert.Empty(Scan("# Usunięto 'plik'\n$a = 1 # 'Brak' tu\nWrite-Output 'x#y'"));
        Assert.Single(Scan("Write-Output 'x # y'; Write-Output 'Brak'"));
    }

    [Fact]
    public void Ignores_BlockComment()
    {
        Assert.Empty(Scan("<# 'Usunięto'\n \"Brak\" #> $a = 1"));
        Assert.StartsWith("x:3:", Assert.Single(Scan("<# a\nb #>\nWrite-Output 'Brak'")), StringComparison.Ordinal);
    }

    [Fact]
    public void Ignores_TextCallLiteral()
    {
        Assert.Empty(Scan("throw (Get-DsoText 'Nie udało się: {0}' $x)"));
        Assert.Empty(Scan("Write-Output \"$(Get-DsoText 'Usunięto ''{0}''' $n)\""));
        Assert.Empty(Scan("$m = Get-DsoText   'Brak' 'x' 5"));
        Assert.Single(Scan("Get-DsoText 'Plik {0}' 'Brak'"));
    }

    [Fact]
    public void Flags_TextCallWithVariable()
    {
        Assert.Single(Scan("Get-DsoText $text"));
        Assert.Single(Scan("Get-DsoText \"Tekst $x\""));
        Assert.Single(Scan("Get-DsoText\n'Tekst'"));
        Assert.Single(Scan("get-dsotext 'Tekst'"));
        Assert.Single(Scan("function Get-DsoText { 'x' }"));
        Assert.Single(Scan("Get-DsoText 'Tekst\nw dwóch liniach'"));
        Assert.Single(CatalogScriptTextScanner.Violations("x", "x", "Get-DsoText $text", Array.Empty<AllowedLiteral>(), pending: true));
        Assert.Empty(Scan("Get-DsoTextFoo 'x'; $Get = 1"));
    }

    [Fact]
    public void Ignores_AllowedLiteral()
    {
        var allowed = AllowedLiteral.Parse("a.b\t'Nie znaleziono'\twzorzec z wyjścia narzędzia Windows\n");
        Assert.Empty(CatalogScriptTextScanner.Violations("x", "a.b", "if ($o -match 'Nie znaleziono') { }", allowed, pending: false));
        Assert.Single(CatalogScriptTextScanner.Violations("x", "a.c", "if ($o -match 'Nie znaleziono') { }", allowed, pending: false));
        Assert.Single(CatalogScriptTextScanner.Violations("x", "a.b", "if ($o -match 'Nie znaleziono pliku') { }", allowed, pending: false));
        Assert.Empty(CatalogScriptTextScanner.Violations("x", "a.c", "Write-Output 'Brak'", allowed, pending: true));
    }

    [Fact]
    public void Flags_StaleAllowedEntry()
    {
        var allowed = AllowedLiteral.Parse("# komentarz\na.b\t'Nie znaleziono'\tpowód\na.b\t'Usunięto'\tpowód\nz.z\t'Brak'\tpowód\n");
        var scripts = new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["a.b"] = new[] { "Write-Output 'Usunięto'", "Get-DsoText 'Nie znaleziono'" },
        };

        var stale = CatalogScriptTextScanner.StaleAllowed(allowed, id => scripts.TryGetValue(id, out var list) ? list : null);

        Assert.Equal(2, stale.Count);
        Assert.Contains(stale, s => s.IndexOf("'Nie znaleziono'", StringComparison.Ordinal) >= 0);
        Assert.Contains(stale, s => s.IndexOf("z.z", StringComparison.Ordinal) >= 0);
    }

    [Fact]
    public void CatalogScripts_NoPolishOutsideTextCalls()
    {
        var allowed = LoadAllowed();
        var pending = LoadPending();
        var catalog = LoadCatalogScripts();
        var violations = new List<string>();
        foreach (var script in catalog)
        {
            violations.AddRange(CatalogScriptTextScanner.Violations(script.Where, script.Id, script.Text, allowed, pending.Contains(script.File)));
        }

        violations.AddRange(CatalogScriptTextScanner.StaleAllowed(
            allowed,
            id => catalog.Any(s => s.Id == id) ? catalog.Where(s => s.Id == id).Select(s => s.Text).ToList() : null));
        Assert.True(violations.Count == 0, string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void Pending_EntriesExist()
    {
        var pending = LoadPending();
        var catalog = LoadCatalogScripts();
        var problems = new List<string>();
        foreach (var file in pending.Distinct(StringComparer.Ordinal))
        {
            if (!catalog.Any(s => s.File == file) && !File.Exists(RepoRoot.Combine(CatalogDirectory.Replace('/', Path.DirectorySeparatorChar), file)))
            {
                problems.Add("Brak pliku katalogu: " + file);
                continue;
            }

            // Plik bez polskich napisów poza Get-DsoText jest już przerobiony – jego linia ma zniknąć z listy.
            if (!catalog.Where(s => s.File == file).Any(s => CatalogScriptTextScanner.Violations(s.Where, s.Id, s.Text, Array.Empty<AllowedLiteral>(), pending: false).Count > 0))
            {
                problems.Add("Plik bez polskich napisów poza Get-DsoText – usuń go z script-literals-pending.txt: " + file);
            }
        }

        problems.AddRange(pending.GroupBy(p => p, StringComparer.Ordinal).Where(g => g.Count() > 1).Select(g => "Powtórzony wpis: " + g.Key));
        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }

    private static IReadOnlyList<string> Scan(string script) =>
        CatalogScriptTextScanner.Violations("x", "x", script, Array.Empty<AllowedLiteral>(), pending: false);

    private static IReadOnlyList<AllowedLiteral> LoadAllowed() =>
        AllowedLiteral.Parse(File.ReadAllText(RepoRoot.Combine(RepoListDirectory, "script-literals-allowed.txt"), Encoding.UTF8));

    private static List<string> LoadPending() =>
        File.ReadAllText(RepoRoot.Combine(RepoListDirectory, "script-literals-pending.txt"), Encoding.UTF8)
            .Split('\n')
            .Select(l => l.Trim())
            .Where(l => l.Length > 0 && !l.StartsWith("#", StringComparison.Ordinal))
            .ToList();

    /// <summary>Wszystkie pola <c>script.*</c> pozycji katalogu (tablica linii łączona <c>\n</c>, jak w <c>TweakParser</c>).</summary>
    private static List<CatalogScript> LoadCatalogScripts()
    {
        var result = new List<CatalogScript>();
        var files = Directory.EnumerateFiles(RepoRoot.Combine(CatalogDirectory.Replace('/', Path.DirectorySeparatorChar)), "*.json")
            .Select(Path.GetFileName)
            .Where(n => !n.StartsWith("apps", StringComparison.Ordinal))
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();
        Assert.NotEmpty(files);
        foreach (var file in files)
        {
            var path = RepoRoot.Combine(CatalogDirectory.Replace('/', Path.DirectorySeparatorChar), file);
            foreach (var item in JArray.Parse(File.ReadAllText(path, Encoding.UTF8)).OfType<JObject>())
            {
                var id = (string?)item["id"] ?? string.Empty;
                if (item["script"] is not JObject script)
                {
                    continue;
                }

                foreach (var field in ScriptFields)
                {
                    var text = script[field] switch
                    {
                        JValue { Type: JTokenType.String } value => (string)value!,
                        JArray lines => string.Join("\n", lines.Select(l => (string)l!)),
                        _ => null,
                    };
                    if (text is not null)
                    {
                        result.Add(new CatalogScript(file, id, CatalogDirectory + "/" + file + "#" + id + ".script." + field, text));
                    }
                }
            }
        }

        return result;
    }

    private sealed class CatalogScript
    {
        public CatalogScript(string file, string id, string where, string text)
        {
            File = file;
            Id = id;
            Where = where;
            Text = text;
        }

        public string File { get; }

        public string Id { get; }

        public string Where { get; }

        public string Text { get; }
    }
}

/// <summary>Napis znaleziony w skrypcie: linia (od 1), napis jak w źródle (z cudzysłowami), treść i czy to literał <c>Get-DsoText</c>.</summary>
public sealed class ScriptLiteral
{
    public ScriptLiteral(int line, string raw, string text, bool isTextCall)
    {
        Line = line;
        Raw = raw;
        Text = text;
        IsTextCall = isTextCall;
    }

    public int Line { get; }

    public string Raw { get; }

    /// <summary>Treść bez cudzysłowów; w napisie w cudzysłowach podwyrażenie <c>$(…)</c> = <c>{}</c> (jego napisy są osobnymi literałami).</summary>
    public string Text { get; }

    public bool IsTextCall { get; }
}

/// <summary>
/// Uproszczony leksyk PowerShella (tylko do testów): napisy w apostrofach (podwojony apostrof = apostrof; klasa apostrofów jak
/// w PowerShellu: <c>'</c>, <c>‘</c>, <c>’</c>, <c>‚</c>, <c>‛</c>), w cudzysłowach (<c>"</c>, <c>“</c>, <c>”</c>, <c>„</c>; <c>`</c> chroni
/// następny znak, <c>$(…)</c> czytane jako kod), here-stringi <c>@'…'@</c>/<c>@"…"@</c>, komentarze <c>#</c> (na początku tokenu)
/// do końca linii i <c>&lt;# … #&gt;</c>. Polski = <see cref="PolishLiteralScanner.IsPolish"/> (ta sama lista słów co strażnik C#).
/// Czego nie łapie: polskiego tekstu bez cudzysłowów (argument-słowo <c>Write-Output Gotowe</c>) i <c>Get-DsoText</c> wywołanego
/// przez <c>&amp;</c>/alias/zmienną.
/// </summary>
public sealed class CatalogScriptTextScanner
{
    private const string TextCall = "Get-DsoText";

    private const string SingleQuotes = "'‘’‚‛";

    private const string DoubleQuotes = "\"“”„";

    private readonly string _s;
    private readonly List<ScriptLiteral> _found = new();
    private readonly List<string> _callProblems = new();
    private int _i;
    private int _line = 1;

    private CatalogScriptTextScanner(string script)
    {
        _s = script;
    }

    /// <summary>Naruszenia w jednym skrypcie (<c>miejsce:linia: …</c>): błędne wywołania <c>Get-DsoText</c> zawsze, polskie napisy – gdy plik nie jest w <c>pending</c>.</summary>
    public static IReadOnlyList<string> Violations(string where, string id, string script, IEnumerable<AllowedLiteral> allowed, bool pending)
    {
        var scanner = Lex(script);
        var result = scanner._callProblems.Select(p => where + ":" + p).ToList();
        if (!pending)
        {
            var here = new HashSet<string>(allowed.Where(a => string.Equals(a.Path, id, StringComparison.Ordinal)).Select(a => a.Literal), StringComparer.Ordinal);
            result.AddRange(PolishHits(scanner).Where(l => !here.Contains(l.Raw)).Select(l => where + ":" + l.Line + ": " + l.Raw));
        }

        return result;
    }

    /// <summary>Wyjątki (<c>id pozycji</c> w polu ścieżki), których literału nie ma wśród polskich napisów skryptów pozycji albo pozycji nie ma.</summary>
    public static IReadOnlyList<string> StaleAllowed(IEnumerable<AllowedLiteral> allowed, Func<string, IReadOnlyList<string>?> scriptsOf)
    {
        var result = new List<string>();
        foreach (var group in allowed.GroupBy(a => a.Path, StringComparer.Ordinal))
        {
            var scripts = scriptsOf(group.Key) ?? Array.Empty<string>();
            var present = new HashSet<string>(scripts.SelectMany(s => PolishHits(Lex(s))).Select(l => l.Raw), StringComparer.Ordinal);
            foreach (var entry in group.Where(e => !present.Contains(e.Literal)))
            {
                result.Add("Nieaktualny wyjątek (brak literału w skryptach pozycji): " + entry.Path + "\t" + entry.Literal);
            }
        }

        return result;
    }

    private static IEnumerable<ScriptLiteral> PolishHits(CatalogScriptTextScanner scanner) =>
        scanner._found.Where(l => !l.IsTextCall && PolishLiteralScanner.IsPolish(l.Text));

    private static CatalogScriptTextScanner Lex(string script)
    {
        if (script is null)
        {
            throw new ArgumentNullException(nameof(script));
        }

        var scanner = new CatalogScriptTextScanner(script);
        scanner.Code(inSubexpression: false);
        return scanner;
    }

    private char At(int offset) => _i + offset < _s.Length ? _s[_i + offset] : '\0';

    private void Advance()
    {
        if (_s[_i] == '\n')
        {
            _line++;
        }

        _i++;
    }

    /// <summary>Kod do końca skryptu albo (w <c>$(…)</c>) do zamykającego <c>)</c>, którego nie zjada.</summary>
    private void Code(bool inSubexpression)
    {
        var parens = 0;
        var textCallPending = false;
        while (_i < _s.Length)
        {
            var c = _s[_i];
            if (c == '<' && At(1) == '#')
            {
                while (_i < _s.Length && !(_s[_i] == '#' && At(1) == '>'))
                {
                    Advance();
                }

                _i = Math.Min(_i + 2, _s.Length);
                continue;
            }

            if (c == '#' && AtTokenStart())
            {
                while (_i < _s.Length && _s[_i] != '\n')
                {
                    _i++;
                }

                continue;
            }

            if (c == '@' && (SingleQuotes.IndexOf(At(1)) >= 0 || DoubleQuotes.IndexOf(At(1)) >= 0) && (At(2) == '\n' || (At(2) == '\r' && At(3) == '\n')))
            {
                ReadHereString();
                continue;
            }

            if (SingleQuotes.IndexOf(c) >= 0)
            {
                ReadSingle(textCallPending);
                textCallPending = false;
                continue;
            }

            if (DoubleQuotes.IndexOf(c) >= 0)
            {
                ReadDouble();
                continue;
            }

            if (inSubexpression)
            {
                if (c == '(')
                {
                    parens++;
                }
                else if (c == ')')
                {
                    if (parens == 0)
                    {
                        return;
                    }

                    parens--;
                }
            }

            if (AtWordStart() && string.Compare(_s, _i, TextCall, 0, TextCall.Length, StringComparison.OrdinalIgnoreCase) == 0 && !IsWordChar(At(TextCall.Length)))
            {
                if (string.CompareOrdinal(_s, _i, TextCall, 0, TextCall.Length) != 0)
                {
                    _callProblems.Add(_line + ": " + TextCall + " must be written exactly as " + TextCall);
                }

                _i += TextCall.Length;
                var line = _line;
                while (_i < _s.Length && (_s[_i] == ' ' || _s[_i] == '\t'))
                {
                    _i++;
                }

                if (_i < _s.Length && _s[_i] == '\'')
                {
                    textCallPending = true;
                }
                else
                {
                    _callProblems.Add(line + ": " + TextCall + " needs a single-quoted literal as the first argument");
                }

                continue;
            }

            Advance();
        }
    }

    private bool AtTokenStart() => _i == 0 || char.IsWhiteSpace(_s[_i - 1]) || ";{}()|&=,".IndexOf(_s[_i - 1]) >= 0;

    private bool AtWordStart() => _i == 0 || !(IsWordChar(_s[_i - 1]) || _s[_i - 1] == '$');

    private static bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c == '_' || c == '-';

    private void ReadSingle(bool isTextCall)
    {
        var start = _i;
        var line = _line;
        var text = new StringBuilder();
        _i++;
        while (_i < _s.Length)
        {
            var c = _s[_i];
            if (SingleQuotes.IndexOf(c) >= 0)
            {
                if (SingleQuotes.IndexOf(At(1)) >= 0)
                {
                    text.Append(At(1));
                    _i += 2;
                    continue;
                }

                _i++;
                break;
            }

            text.Append(c);
            Advance();
        }

        if (isTextCall && _line != line)
        {
            _callProblems.Add(line + ": " + TextCall + " literal must fit in one line");
        }

        _found.Add(new ScriptLiteral(line, _s.Substring(start, _i - start), text.ToString(), isTextCall));
    }

    private void ReadDouble()
    {
        var start = _i;
        var line = _line;
        var text = new StringBuilder();
        _i++;
        while (_i < _s.Length)
        {
            var c = _s[_i];
            if (c == '`')
            {
                text.Append(At(1));
                _i++;
                if (_i < _s.Length)
                {
                    Advance();
                }

                continue;
            }

            if (DoubleQuotes.IndexOf(c) >= 0)
            {
                if (DoubleQuotes.IndexOf(At(1)) >= 0)
                {
                    text.Append(At(1));
                    _i += 2;
                    continue;
                }

                _i++;
                break;
            }

            if (c == '$' && At(1) == '(')
            {
                _i += 2;
                text.Append("{}");
                Code(inSubexpression: true);
                if (_i < _s.Length)
                {
                    _i++;
                }

                continue;
            }

            text.Append(c);
            Advance();
        }

        _found.Add(new ScriptLiteral(line, _s.Substring(start, _i - start), text.ToString(), isTextCall: false));
    }

    private void ReadHereString()
    {
        var start = _i;
        var line = _line;
        var quote = _s[_i + 1];
        var single = SingleQuotes.IndexOf(quote) >= 0;
        var quotes = single ? SingleQuotes : DoubleQuotes;
        _i += 2;
        var text = new StringBuilder();
        while (_i < _s.Length)
        {
            if (_s[_i] == '\n' && quotes.IndexOf(At(1)) >= 0 && At(2) == '@')
            {
                Advance();
                _i += 2;
                break;
            }

            text.Append(_s[_i]);
            Advance();
        }

        _found.Add(new ScriptLiteral(line, _s.Substring(start, _i - start), text.ToString(), isTextCall: false));
    }
}
