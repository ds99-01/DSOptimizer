using System;
using System.IO;

namespace DSOptimizer.Tests.Unit.Support;

/// <summary>
/// Korzeń repozytorium (katalog z <c>DSOptimizer.sln</c>), szukany od katalogu testów w górę. Testy czytają docs/ i katalog
/// bezpośrednio z repo, a nie z kopii w bin (kopie <c>PreserveNewest</c> trzymały usunięte i przemianowane pliki).
/// </summary>
public static class RepoRoot
{
    private static readonly Lazy<string> Found = new(Find);

    public static string Path => Found.Value;

    public static string Combine(params string[] parts)
    {
        var all = new string[parts.Length + 1];
        all[0] = Path;
        Array.Copy(parts, 0, all, 1, parts.Length);
        return System.IO.Path.Combine(all);
    }

    private static string Find()
    {
        var directory = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(System.IO.Path.Combine(directory.FullName, "DSOptimizer.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Nie znaleziono korzenia repozytorium (DSOptimizer.sln) powyżej " + AppDomain.CurrentDomain.BaseDirectory);
    }
}
