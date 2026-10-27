using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using DSOptimizer.Tests.Unit.Support;
using Xunit;

namespace DSOptimizer.Tests.Unit.Repo;

public class RepoDocsLinksTests
{
    private static readonly string[] TextExtensions = { ".cs", ".xaml", ".md", ".json", ".ps1", ".psm1", ".psd1", ".yml" };

    private static readonly Regex DocsLink = new(@"docs/[A-Za-z0-9_./-]+\.md", RegexOptions.CultureInvariant);

    // Złożone, żeby sam test nie zawierał literalnych odnośników do docs.
    private const string Docs = "docs" + "/";

    private static readonly string[] SkippedDirectories = { "bin", "obj", ".git" };

    [Fact]
    public void DocsLinks_PointToListedFiles()
    {
        var missing = FindBrokenLinks(RepoRoot.Path, ListFiles(RepoRoot.Path));

        Assert.True(missing.Count == 0, "Odnośniki do nieistniejących albo nieśledzonych plików:" + Environment.NewLine + string.Join(Environment.NewLine, missing));
    }

    [Fact]
    public void FindBrokenLinks_TargetOutsideTheFileList_IsReported()
    {
        var root = Path.Combine(Path.GetTempPath(), "dso-tests", Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "docs"));
            File.WriteAllText(Path.Combine(root, "README.md"), "Zob. " + Docs + "Tracked.md i " + Docs + "Untracked.md oraz " + Docs + "missing.md." + Environment.NewLine);
            File.WriteAllText(Path.Combine(root, "docs", "Tracked.md"), "x");
            File.WriteAllText(Path.Combine(root, "docs", "Untracked.md"), "x");

            var broken = FindBrokenLinks(root, new[] { "README.md", Docs + "tracked.md" });

            Assert.Equal(2, broken.Count);
            Assert.Contains(broken, b => b.EndsWith(Docs + "Untracked.md", StringComparison.Ordinal));
            Assert.Contains(broken, b => b.EndsWith(Docs + "missing.md", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ListFiles_WithoutGit_EnumeratesDirectoryAndSkipsBuildOutput()
    {
        var root = Path.Combine(Path.GetTempPath(), "dso-tests", Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "docs"));
            Directory.CreateDirectory(Path.Combine(root, "bin"));
            File.WriteAllText(Path.Combine(root, "docs", "A.md"), "x");
            File.WriteAllText(Path.Combine(root, "bin", "B.md"), "x");

            var files = ListFiles(root);

            Assert.Equal(new[] { Docs + "A.md" }, files);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static List<string> FindBrokenLinks(string root, IReadOnlyList<string> files)
    {
        var known = new HashSet<string>(files, StringComparer.OrdinalIgnoreCase);
        var missing = new List<string>();

        foreach (var relative in files)
        {
            if (!TextExtensions.Contains(Path.GetExtension(relative), StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            var full = Path.Combine(root, relative);
            if (!File.Exists(full))
            {
                continue;
            }

            var lineNumber = 0;
            foreach (var line in File.ReadLines(full))
            {
                lineNumber++;
                foreach (Match match in DocsLink.Matches(line))
                {
                    if (!known.Contains(match.Value))
                    {
                        missing.Add($"{relative}:{lineNumber}: {match.Value}");
                    }
                }
            }
        }

        return missing;
    }

    // Jedna lista plików dla całego testu: z `git ls-files` w repozytorium, a bez .git (np. „Source code (zip)”)
    // z rekurencyjnego przeglądu katalogu bez bin, obj i .git. Ścieżki względne, z separatorem '/'.
    private static List<string> ListFiles(string root)
    {
        var gitPath = Path.Combine(root, ".git");
        return Directory.Exists(gitPath) || File.Exists(gitPath) ? GitFiles(root) : DirectoryFiles(root);
    }

    private static List<string> DirectoryFiles(string root)
    {
        var result = new List<string>();
        var pending = new Stack<string>();
        pending.Push(root);

        while (pending.Count > 0)
        {
            var directory = pending.Pop();
            foreach (var file in Directory.GetFiles(directory))
            {
                result.Add(Path.GetFullPath(file).Substring(Path.GetFullPath(root).TrimEnd('\\', '/').Length + 1).Replace('\\', '/'));
            }

            foreach (var child in Directory.GetDirectories(directory))
            {
                if (!SkippedDirectories.Contains(Path.GetFileName(child), StringComparer.OrdinalIgnoreCase))
                {
                    pending.Push(child);
                }
            }
        }

        result.Sort(StringComparer.Ordinal);
        return result;
    }

    private static List<string> GitFiles(string root)
    {
        var info = new ProcessStartInfo("git", "ls-files -z")
        {
            WorkingDirectory = root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = new UTF8Encoding(false),
        };

        using var process = Process.Start(info) ?? throw new InvalidOperationException("Nie udało się uruchomić git.");
        var errorTask = process.StandardError.ReadToEndAsync();
        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException("git ls-files zakończył się kodem " + process.ExitCode + ": " + errorTask.Result);
        }

        return output.Split(new[] { '\0' }, StringSplitOptions.RemoveEmptyEntries).Select(f => f.Replace('\\', '/')).ToList();
    }
}
