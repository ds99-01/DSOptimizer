using System;
using System.Linq;

namespace DSOptimizer.Core.Install;

/// <summary>Jedyne miejsce składania argumentów <c>msiexec</c> instalacji (spec 3.4).</summary>
public static class MsiArguments
{
    /// <summary>
    /// <c>/i "&lt;msi&gt;" /qn /norestart /l*v "&lt;log&gt;"</c>. Obie ścieżki muszą być lokalne i bezwzględne (<c>X:\…</c>), bez
    /// cudzysłowu, znaków sterujących i bez końcowego <c>\</c> (który zamknąłby cudzysłów jako <c>\"</c>); inaczej <see cref="ArgumentException"/>.
    /// </summary>
    public static string Install(string msiPath, string logPath)
    {
        Validate(msiPath, nameof(msiPath));
        Validate(logPath, nameof(logPath));
        return "/i \"" + msiPath + "\" /qn /norestart /l*v \"" + logPath + "\"";
    }

    private static void Validate(string path, string name)
    {
        var ok = path is not null
                 && path.Length > 3
                 && IsAsciiLetter(path[0])
                 && path[1] == ':'
                 && path[2] == '\\'
                 && path[path.Length - 1] != '\\'
                 && path.IndexOf('"') < 0
                 && !path.Any(char.IsControl);
        if (!ok)
        {
            throw new ArgumentException("Ścieżka dla msiexec musi być lokalna i bezwzględna (X:\\…), bez cudzysłowu: " + path, name);
        }
    }

    private static bool IsAsciiLetter(char c) => c is (>= 'A' and <= 'Z') or (>= 'a' and <= 'z');
}
