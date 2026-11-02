using System;
using System.Collections.Generic;
using System.Text;

namespace DSOptimizer.Core.Autostart;

/// <summary>
/// Plik wykonywalny pozycji. <see cref="Definite"/> = ścieżka w pełni rozwinięta zmiennymi konta, zakorzeniona (<c>X:\…</c>, <c>\\…</c>)
/// i jednoznaczna, więc <see cref="Probe"/> mówi coś o tym pliku; inaczej plik jest nieustalony („nie ustalono pliku”, nigdy „nie istnieje”).
/// </summary>
public sealed record CommandTarget(string? FilePath, FileProbe Probe, bool Definite, bool IsInterpreter, string? Arguments);

/// <summary>Rozbiór polecenia Run i celu skrótu na plik i argumenty, jak <c>CreateProcess</c>, ze zmiennymi zalogowanego konta.</summary>
public static class AutostartCommand
{
    private const string PowerShell = "powershell.exe";
    private const string Pwsh = "pwsh.exe";

    private static readonly char[] Separators = { '\\', '/' };

    private static readonly char[] PathMarks = { '\\', '/', ':' };

    private static readonly string[] InterpreterNames =
    {
        "cmd.exe", PowerShell, Pwsh, "wscript.exe", "cscript.exe", "mshta.exe", "rundll32.exe", "regsvr32.exe",
    };

    /// <summary>Interpretery i hosty: ścieżka procesu nie wystarcza do powiązania pomiaru, liczy się cała linia poleceń.</summary>
    public static IReadOnlyCollection<string> Interpreters => InterpreterNames;

    /// <summary>
    /// Plik polecenia Run. W cudzysłowie – do drugiego cudzysłowu. Bez cudzysłowu i bez białych znaków – całość. Taki plik bez rozszerzenia:
    /// najpierw jak podany, potem z dopisanym <c>.exe</c> (jak <c>CreateProcess</c>); wygrywa pierwszy istniejący, a gdy nie ma żadnego, plik
    /// jest nieustalony – nie wiadomo, co uruchomiłby Windows. Bez cudzysłowu ze spacjami –
    /// najpierw sama nazwa interpretera w pierwszym tokenie, potem prefiksy kończące się przed kolejnymi spacjami (od najkrótszego) i całość,
    /// kandydat bez rozszerzenia tylko z dopisanym <c>.exe</c> (jak <c>CreateProcess</c>, więc prefiks w rodzaju <c>C:\Program Files</c>
    /// nigdy nie trafia w katalog); wygrywa pierwszy istniejący, a brak kandydata = plik nieustalony. <paramref name="probe"/> zwraca
    /// <see cref="FileProbe.Exists"/> tylko dla istniejącego pliku – katalog to nie <see cref="FileProbe.Exists"/>.
    /// </summary>
    public static CommandTarget Parse(string? command, UserEnvironment environment, Func<string, FileProbe> probe)
    {
        RequireServices(environment, probe);
        var text = command?.Trim();
        if (string.IsNullOrEmpty(text))
        {
            return Undetermined(null);
        }

        if (text![0] == '"')
        {
            var close = text.IndexOf('"', 1);
            var file = close < 0 ? text.Substring(1) : text.Substring(1, close - 1);
            var arguments = close < 0 ? null : ArgumentsOf(text.Substring(close + 1));
            return file.Trim().Length == 0 ? Undetermined(arguments) : Resolve(file, arguments, environment, probe);
        }

        var firstSpace = IndexOfWhiteSpace(text, 0);
        if (firstSpace < 0)
        {
            return Resolve(text, null, environment, probe);
        }

        var firstToken = text.Substring(0, firstSpace);
        if (IsBareName(firstToken) && InterpreterName(firstToken) is { } interpreter)
        {
            return BareInterpreter(interpreter, ArgumentsOf(text.Substring(firstSpace)), environment);
        }

        for (var end = firstSpace; end >= 0; end = IndexOfWhiteSpace(text, SkipWhiteSpace(text, end)))
        {
            if (TryExisting(text.Substring(0, end), environment, probe, out var found))
            {
                return Found(found, ArgumentsOf(text.Substring(end)));
            }
        }

        return TryExisting(text, environment, probe, out var whole) ? Found(whole, null) : Undetermined(null);
    }

    /// <summary>Cel skrótu <c>.lnk</c>. Skrót MSI „advertised” albo pusty cel = plik nieustalony.</summary>
    public static CommandTarget FromShortcut(string? rawTarget, string? arguments, bool advertised, UserEnvironment environment, Func<string, FileProbe> probe)
    {
        RequireServices(environment, probe);
        var trimmedArguments = ArgumentsOf(arguments);
        if (advertised || string.IsNullOrWhiteSpace(rawTarget))
        {
            return Undetermined(trimmedArguments);
        }

        return ResolvePath(rawTarget!.Trim(), trimmedArguments, environment, probe);
    }

    /// <summary>
    /// Linia poleceń do porównania z <c>CommandLine</c> pomiaru Windows: bez cudzysłowów wokół pierwszego tokenu, zmienne konta rozwinięte,
    /// białe znaki zwinięte i obcięte, małe litery.
    /// </summary>
    public static string NormalizeCommandLine(string? commandLine, UserEnvironment environment)
    {
        if (environment is null)
        {
            throw new ArgumentNullException(nameof(environment));
        }

        var text = commandLine?.Trim();
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        if (text![0] == '"')
        {
            var close = text.IndexOf('"', 1);
            if (close > 0)
            {
                text = text.Substring(1, close - 1) + text.Substring(close + 1);
            }
        }

        environment.TryExpand(text, out var expanded);
        var builder = new StringBuilder(expanded.Length);
        foreach (var c in expanded)
        {
            if (!char.IsWhiteSpace(c))
            {
                builder.Append(c);
            }
            else if (builder.Length > 0 && builder[builder.Length - 1] != ' ')
            {
                builder.Append(' ');
            }
        }

        return builder.ToString().TrimEnd().ToLowerInvariant();
    }

    private static CommandTarget Resolve(string file, string? arguments, UserEnvironment environment, Func<string, FileProbe> probe)
    {
        if (IsBareName(file))
        {
            return InterpreterName(file) is { } interpreter
                ? BareInterpreter(interpreter, arguments, environment)
                : new CommandTarget(file, FileProbe.Unknown, Definite: false, IsInterpreter: false, arguments);
        }

        return ResolvePath(file, arguments, environment, probe, appendsExe: true);
    }

    /// <summary>
    /// <paramref name="appendsExe"/> = polecenie Run: plik bez rozszerzenia, którego nie ma, sprawdzany też z <c>.exe</c>; gdy i tego nie ma,
    /// plik jest nieustalony, nigdy <see cref="FileProbe.Missing"/>.
    /// </summary>
    private static CommandTarget ResolvePath(string file, string? arguments, UserEnvironment environment, Func<string, FileProbe> probe, bool appendsExe = false)
    {
        var complete = environment.TryExpand(file, out var expanded);
        var interpreter = IsInterpreterFile(expanded);
        if (!complete || !IsRooted(expanded))
        {
            return new CommandTarget(expanded, FileProbe.Unknown, Definite: false, interpreter, arguments);
        }

        var found = probe(expanded);
        if (!appendsExe || found == FileProbe.Exists || HasExtension(expanded))
        {
            return new CommandTarget(expanded, found, Definite: true, interpreter, arguments);
        }

        return TryExistingFile(expanded, probe, out var exe)
            ? Found(exe, arguments)
            : new CommandTarget(expanded, FileProbe.Unknown, Definite: false, interpreter, arguments);
    }

    /// <summary>
    /// Sama nazwa interpretera: plik systemowy z <c>%SystemRoot%\System32</c> (PowerShell 5.1 z <c>WindowsPowerShell\v1.0</c>), bez
    /// sprawdzania istnienia. <c>pwsh.exe</c> nie leży w stałym miejscu, więc zostaje nieustalony.
    /// </summary>
    private static CommandTarget BareInterpreter(string name, string? arguments, UserEnvironment environment)
    {
        var systemRoot = environment.Get("SystemRoot");
        if (string.Equals(name, Pwsh, StringComparison.OrdinalIgnoreCase) || systemRoot is null || !IsRooted(systemRoot))
        {
            return new CommandTarget(name, FileProbe.Unknown, Definite: false, IsInterpreter: true, arguments);
        }

        var folder = string.Equals(name, PowerShell, StringComparison.OrdinalIgnoreCase) ? @"\System32\WindowsPowerShell\v1.0\" : @"\System32\";
        return new CommandTarget(systemRoot.TrimEnd('\\') + folder + name, FileProbe.Exists, Definite: true, IsInterpreter: true, arguments);
    }

    private static bool TryExisting(string candidate, UserEnvironment environment, Func<string, FileProbe> probe, out string path)
    {
        path = string.Empty;
        return environment.TryExpand(candidate, out var expanded) && IsRooted(expanded) && TryExistingFile(expanded, probe, out path);
    }

    // Bez rozszerzenia tylko z dopisanym .exe, jak CreateProcess: sam prefiks bez rozszerzenia to zwykle katalog (C:\Program Files).
    private static bool TryExistingFile(string expanded, Func<string, FileProbe> probe, out string path)
    {
        var file = HasExtension(expanded) ? expanded : expanded + ".exe";
        path = probe(file) == FileProbe.Exists ? file : string.Empty;
        return path.Length > 0;
    }

    private static CommandTarget Found(string path, string? arguments)
        => new(path, FileProbe.Exists, Definite: true, IsInterpreterFile(path), arguments);

    private static CommandTarget Undetermined(string? arguments)
        => new(null, FileProbe.Unknown, Definite: false, IsInterpreter: false, arguments);

    private static string? InterpreterName(string token)
    {
        foreach (var name in InterpreterNames)
        {
            if (string.Equals(name, token, StringComparison.OrdinalIgnoreCase))
            {
                return token;
            }

            if (!HasExtension(token) && string.Equals(name, token + ".exe", StringComparison.OrdinalIgnoreCase))
            {
                return token + ".exe";
            }
        }

        return null;
    }

    private static bool IsInterpreterFile(string path)
        => Array.Exists(InterpreterNames, n => string.Equals(n, FileName(path), StringComparison.OrdinalIgnoreCase));

    private static bool IsBareName(string file) => file.IndexOfAny(PathMarks) < 0;

    private static bool IsRooted(string path)
        => path.StartsWith(@"\\", StringComparison.Ordinal)
           || (path.Length >= 3 && IsAsciiLetter(path[0]) && path[1] == ':' && (path[2] == '\\' || path[2] == '/'));

    private static bool IsAsciiLetter(char c) => (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z');

    private static bool HasExtension(string path) => FileName(path).IndexOf('.') >= 0;

    // Bez Path.GetFileName: w .NET Framework rzuca dla znaków niedozwolonych w ścieżce, a polecenie pochodzi z rejestru.
    private static string FileName(string path) => path.Substring(path.LastIndexOfAny(Separators) + 1);

    private static string? ArgumentsOf(string? rest)
    {
        var trimmed = rest?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }

    private static int IndexOfWhiteSpace(string text, int start)
    {
        for (var i = start; i < text.Length; i++)
        {
            if (char.IsWhiteSpace(text[i]))
            {
                return i;
            }
        }

        return -1;
    }

    private static int SkipWhiteSpace(string text, int start)
    {
        var i = start;
        while (i < text.Length && char.IsWhiteSpace(text[i]))
        {
            i++;
        }

        return i;
    }

    private static void RequireServices(UserEnvironment environment, Func<string, FileProbe> probe)
    {
        if (environment is null)
        {
            throw new ArgumentNullException(nameof(environment));
        }

        if (probe is null)
        {
            throw new ArgumentNullException(nameof(probe));
        }
    }
}
