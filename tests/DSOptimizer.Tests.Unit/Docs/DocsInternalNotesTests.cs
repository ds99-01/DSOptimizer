using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using DSOptimizer.Tests.Unit.Support;
using Xunit;

namespace DSOptimizer.Tests.Unit.Docs;

/// <summary>
/// Strażnik P11: publiczne dokumenty (docs/tweaks, docs/apps, docs/prep, docs/en) nie pokazują znaczników procesu
/// (hash, HEAD, gałąź, numer specu, Task N, K1/K2, fake/mock, v3.x, osoba autora), a usunięcie notatki nie gubi zastrzeżeń
/// o tym, czego nie sprawdzono (<c>docs-caveats-baseline.txt</c>: liczba linii z zastrzeżeniem w pliku nie może spaść).
/// Wyjątki: <c>docs-internal-notes-allowed.txt</c> (ścieżka TAB fragment TAB powód; nieaktualny wpis = błąd).
/// </summary>
public class DocsInternalNotesTests
{
    private static readonly string DirectoryInRepo = Path.Combine("tests", "DSOptimizer.Tests.Unit", "Docs");

    private static readonly string[] DocRoots = { "docs/tweaks", "docs/apps", "docs/prep", "docs/en" };

    private static readonly Regex Url = new(@"https?://\S+", RegexOptions.CultureInvariant);

    private static readonly Regex[] NotePatterns = new[]
    {
        @"\bHEAD\b",
        @"(?:gałęzi|gałąź|branch) `",
        @"report\.json",
        @"\.gitignore",
        @"\b[Ss]pec(?:u)? \d",
        @"Review Focus",
        @"zrodla/",
        @"\bTask(?:a|u|owi|iem)? \d+",
        @"\bCARRY\b",
        @"\bK[12]\b",
        @"\b(?:fake|mock|no-op)\b",
        @"\bv3\.[\dx]",
        @"\bPester (?:\d|zaliczy|passed)",
        @"\bthe author\b|\bauthor's\b|\bautor(?:a|em|owi)?\b",
        @"\b(?:Stage|Etap\w*) \d",
        @"\b[Pp]arti[aię]\w* C?\d",
        @"\b[Rr]ounds? C?\d",
        @"dso-" + "explore|_arch" + "iwum", // złożone z części, żeby sam test nie wyglądał jak notatka
        @"(?<![0-9A-Za-z{_.-])(?=[0-9a-f]*[a-f])(?=[0-9a-f]*[0-9])[0-9a-f]{7,40}(?![0-9A-Za-z_-])",
    }.Select(p => new Regex(p, RegexOptions.CultureInvariant)).ToArray();

    private static readonly Regex CaveatPattern = new(
        @"nie ?(sprawdz|testowan|potwierdz|zmierz|mierz|przećwicz)|not (checked|tested|confirmed|measured|verified|exercised)|(wasn't|weren't|isn't|aren't|hasn't|haven't) (been )?(checked|tested|confirmed|measured|verified)|unverified|untested|do potwierdzenia|to be confirmed|nie ?wykonan|not done|niepotwierdz",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>Wzorce wewnętrznych notatek znalezione w linii (adresy https:// są pomijane); pusta lista = linia czysta.</summary>
    internal static IReadOnlyList<string> FindNotes(string line)
    {
        var text = Url.Replace(line, " ");
        return NotePatterns.Where(p => p.IsMatch(text)).Select(p => p.ToString()).ToList();
    }

    [Theory]
    [InlineData("build 26300.9457, HEAD `da78bd7`")]
    [InlineData("na gałęzi `etap4b-1`")]
    [InlineData("(Task 9 partii)")]
    [InlineData("eksploracja K2")]
    [InlineData("testy na fake'ach")]
    [InlineData("zob. `docs/zrodla/a.md`")]
    [InlineData("spec 3.3 point 4")]
    [InlineData("Review Focus 1")]
    [InlineData("to be confirmed by the author")]
    [InlineData("decyzja autora")]
    [InlineData("Pester zaliczył 140")]
    [InlineData("w v3.2")]
    [InlineData("CARRY z C3")]
    [InlineData("round C4")]
    [InlineData("zapis w `report.json`")]
    [InlineData("commit 90fe2ae")]
    public void FindNotes_FindsInternalNote(string line) => Assert.NotEmpty(FindNotes(line));

    [Theory]
    [InlineData("Test na VM 2026-10-21, build 26300.9457: Skipped.")]
    [InlineData("`HKU\\<SID>\\Software`")]
    [InlineData("{A8A91A66-3A7D-4424-8D24-04E180695C7A}")]
    [InlineData("https://raw.githubusercontent.com/ChrisTitusTech/winutil/144d51675190ffcb2553b7ba5313f8d602c8ecf9/config/feature.json")]
    [InlineData("kod `0x80004004`")]
    [InlineData("Run all of the round-trip checks first.")]
    [InlineData("`vm-run-20261019-002807`")]
    public void FindNotes_LeavesOrdinaryText(string line) => Assert.Empty(FindNotes(line));

    /// <summary>Linia bez fragmentów dozwolonych dla pliku: wyjątek zwalnia tylko ten fragment, nie całą linię.</summary>
    internal static string WithoutAllowed(string line, string path, IEnumerable<(string Path, string Fragment)> allowed) =>
        allowed.Where(a => a.Path == path).Aggregate(line, (current, a) => current.Replace(a.Fragment, " "));

    [Fact]
    public void Allowed_ExemptsOnlyTheFragment()
    {
        var allowed = new[] { ("docs/x.md", "WinUtil `config/feature.json` @ `144d516`") };
        Assert.Empty(FindNotes(WithoutAllowed("- WinUtil `config/feature.json` @ `144d516` (MIT)", "docs/x.md", allowed)));
        Assert.NotEmpty(FindNotes(WithoutAllowed("- WinUtil `config/feature.json` @ `144d516` (MIT), HEAD `fd320ed`", "docs/x.md", allowed)));
        Assert.NotEmpty(FindNotes(WithoutAllowed("- WinUtil `config/feature.json` @ `144d516` (MIT)", "docs/y.md", allowed)));
    }

    [Theory]
    [InlineData("**Smoke: do potwierdzenia przez autora**. Nie wykonany.")]
    [InlineData("to be confirmed")]
    public void CaveatPattern_CountsConfirmationAndNotDone(string line) => Assert.Matches(CaveatPattern, line);

    [Theory]
    [InlineData("none")]
    public void Batch_HasNoInternalNotes(string batch)
    {
        // batch = prefiks ścieżki względem repo (z ukośnikami /), np. "docs/tweaks/disk." albo "docs/en/apps/"; "none" = kontrola, nic nie filtruje.
        var allowed = LoadAllowed();
        var files = DocFiles().Where(f => f.Key.StartsWith(batch, StringComparison.Ordinal)).ToList();
        if (batch != "none")
        {
            Assert.NotEmpty(files);
        }

        var violations = new List<string>();
        foreach (var file in files)
        {
            var lines = File.ReadAllLines(file.Value, Encoding.UTF8);
            for (var i = 0; i < lines.Length; i++)
            {
                var notes = FindNotes(WithoutAllowed(lines[i], file.Key, allowed));
                if (notes.Count == 0)
                {
                    continue;
                }

                violations.Add($"{file.Key}:{i + 1}: {lines[i].Trim()}  [{string.Join(" | ", notes)}]");
            }
        }

        Assert.True(violations.Count == 0, "Wewnętrzne notatki w dokumentach:\n" + string.Join("\n", violations));
    }

    [Fact]
    public void Allowed_EntriesAreStillNeeded()
    {
        var files = DocFiles();
        var stale = new List<string>();
        foreach (var entry in LoadAllowed())
        {
            if (!files.TryGetValue(entry.Path, out var full)
                || !File.ReadAllLines(full, Encoding.UTF8).Any(l => l.Contains(entry.Fragment) && FindNotes(l.Replace(entry.Fragment, " ")).Count < FindNotes(l).Count))
            {
                stale.Add(entry.Path + " -> " + entry.Fragment);
            }
        }

        Assert.True(stale.Count == 0, "Nieaktualne wpisy docs-internal-notes-allowed.txt:\n" + string.Join("\n", stale));
    }

    [Fact]
    public void Caveats_AreNotLost()
    {
        var baseline = LoadBaseline();
        var files = DocFiles();
        var lost = new List<string>();
        foreach (var entry in baseline)
        {
            var now = files.TryGetValue(entry.Key, out var full) ? CountCaveats(full) : 0;
            if (now < entry.Value)
            {
                lost.Add($"{entry.Key}: zastrzeżeń jest {now}, w bazie {entry.Value}");
            }
        }

        Assert.True(lost.Count == 0, "Zgubione zastrzeżenia o tym, czego nie sprawdzono:\n" + string.Join("\n", lost));
    }

    internal static int CountCaveats(string fullPath) =>
        File.ReadAllLines(fullPath, Encoding.UTF8).Count(l => CaveatPattern.IsMatch(l));

    /// <summary>Wszystkie .md w docs/tweaks, docs/apps, docs/prep i docs/en: ścieżka względem repo (z /) -> ścieżka pełna.</summary>
    private static SortedDictionary<string, string> DocFiles()
    {
        var result = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var root in DocRoots)
        {
            var dir = RepoRoot.Combine(root.Split('/'));
            if (!Directory.Exists(dir))
            {
                continue;
            }

            foreach (var file in Directory.EnumerateFiles(dir, "*.md", SearchOption.AllDirectories))
            {
                var relative = file.Substring(RepoRoot.Path.Length).TrimStart('\\', '/').Replace('\\', '/');
                result[relative] = file;
            }
        }

        return result;
    }

    private static List<(string Path, string Fragment)> LoadAllowed() =>
        ReadList("docs-internal-notes-allowed.txt")
            .Select(l => l.Split('\t'))
            .Select(p => p.Length >= 3 && p[2].Trim().Length > 0
                ? (p[0], p[1])
                : throw new InvalidOperationException("Wpis wymaga ścieżki, fragmentu i powodu (TAB): " + string.Join("\t", p)))
            .ToList();

    private static Dictionary<string, int> LoadBaseline() =>
        ReadList("docs-caveats-baseline.txt")
            .Select(l => l.Split('\t'))
            .ToDictionary(p => p[0], p => int.Parse(p[1], System.Globalization.CultureInfo.InvariantCulture), StringComparer.Ordinal);

    private static IEnumerable<string> ReadList(string name) =>
        File.ReadAllText(RepoRoot.Combine(DirectoryInRepo, name), Encoding.UTF8)
            .Split('\n')
            .Select(l => l.TrimEnd('\r'))
            .Where(l => l.Trim().Length > 0 && !l.TrimStart().StartsWith("#", StringComparison.Ordinal));
}