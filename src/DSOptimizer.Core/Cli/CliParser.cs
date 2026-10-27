using System;
using System.Collections.Generic;
using DSOptimizer.Core.Engine;
using DSOptimizer.Core.Install;

namespace DSOptimizer.Core.Cli;

public static class CliParser
{
    public const string Usage = "DSOptimizer.exe --detect <id> | --apply <id> [--restore-point required|ifavailable|skip] | --undo <id> | --execute <id> [--catalog <plik.json>] --out <plik.json>"
        + " | DSOptimizer.exe --list-apps | --remove-app <id> [--restore-point required|ifavailable|skip] --out <plik.json>"
        + " | DSOptimizer.exe --list-install [--apps-catalog <plik.json>] | --install-apps <id>[,<id>…] [--apps-catalog <plik.json>]"
        + " | --search-apps <tekst> --out <plik.json>";

    private const string ListAppsOption = "--list-apps";
    private const string ListInstallOption = "--list-install";

    /// <summary>
    /// Pojedynczy przebieg. Wygrywa pierwszy błąd; po nim przebieg trwa tylko po to, by znaleźć --out.
    /// Kontrole po przebiegu działają wyłącznie, gdy w przebiegu nie było błędu.
    /// </summary>
    public static CliParseResult Parse(IReadOnlyList<string> args)
    {
        if (args is null)
        {
            throw new ArgumentNullException(nameof(args));
        }

        string? error = null;
        string? outPath = null;
        string? catalogPath = null;
        string? appsCatalogPath = null;
        RestorePointPolicy? restorePoint = null;
        var commands = new List<(CliCommand Command, string Id)>();

        for (var i = 0; i < args.Count; i++)
        {
            var arg = args[i];
            if (!arg.StartsWith("--", StringComparison.Ordinal))
            {
                error ??= $"Nieoczekiwany argument '{arg}'.";
                continue;
            }

            if (arg.Equals(ListAppsOption, StringComparison.OrdinalIgnoreCase) || arg.Equals(ListInstallOption, StringComparison.OrdinalIgnoreCase))
            {
                // Komendy bez wartości: następny argument nie jest ich wartością.
                commands.Add((arg.Equals(ListAppsOption, StringComparison.OrdinalIgnoreCase) ? CliCommand.ListApps : CliCommand.ListInstall, string.Empty));
                continue;
            }

            var isCommand = TryGetCommand(arg, out var command);
            var isKnownOption = isCommand
                || arg.Equals("--catalog", StringComparison.OrdinalIgnoreCase)
                || arg.Equals("--apps-catalog", StringComparison.OrdinalIgnoreCase)
                || arg.Equals("--restore-point", StringComparison.OrdinalIgnoreCase)
                || arg.Equals("--out", StringComparison.OrdinalIgnoreCase);
            if (!isKnownOption)
            {
                error ??= $"Nieznana opcja '{arg}'.";
                continue;
            }

            if (i + 1 >= args.Count || args[i + 1].StartsWith("--", StringComparison.Ordinal))
            {
                error ??= $"Opcja {arg} wymaga wartości.";
                continue;
            }

            var value = args[++i];
            if (isCommand)
            {
                commands.Add((command, value));
            }
            else if (arg.Equals("--out", StringComparison.OrdinalIgnoreCase))
            {
                if (outPath is null)
                {
                    outPath = value;
                }
                else
                {
                    error ??= "Opcja --out podana więcej niż raz.";
                }
            }
            else if (arg.Equals("--catalog", StringComparison.OrdinalIgnoreCase))
            {
                if (catalogPath is null)
                {
                    catalogPath = value;
                }
                else
                {
                    error ??= "Opcja --catalog podana więcej niż raz.";
                }
            }
            else if (arg.Equals("--apps-catalog", StringComparison.OrdinalIgnoreCase))
            {
                if (appsCatalogPath is null)
                {
                    appsCatalogPath = value;
                }
                else
                {
                    error ??= "Opcja --apps-catalog podana więcej niż raz.";
                }
            }
            else if (restorePoint is not null)
            {
                error ??= "Opcja --restore-point podana więcej niż raz.";
            }
            else if (TryGetPolicy(value, out var policy))
            {
                restorePoint = policy;
            }
            else
            {
                error ??= $"Nieznana wartość --restore-point '{value}' (dozwolone: required, ifavailable, skip).";
            }
        }

        if (error is null && commands.Count != 1)
        {
            error = "Podaj dokładnie jedno z: --detect, --apply, --undo, --execute, --list-apps, --remove-app, --list-install, --install-apps, --search-apps.";
        }

        if (error is null && outPath is null)
        {
            error = "Brak wymaganej opcji --out.";
        }

        var isAppCommand = commands.Count == 1 && commands[0].Command is CliCommand.ListApps or CliCommand.RemoveApp;
        if (error is null && restorePoint is not null && commands[0].Command is not (CliCommand.Apply or CliCommand.RemoveApp))
        {
            error = "Opcja --restore-point dotyczy tylko --apply i --remove-app.";
        }

        if (error is null && catalogPath is not null && isAppCommand)
        {
            error = "Opcja --catalog nie dotyczy --list-apps ani --remove-app.";
        }

        var isInstallCommand = commands.Count == 1 && IsInstallCommand(commands[0].Command);
        if (error is null && catalogPath is not null && isInstallCommand)
        {
            error = "Opcja --catalog nie dotyczy poleceń instalacji (listę aplikacji podmienia --apps-catalog).";
        }

        if (error is null && appsCatalogPath is not null && commands[0].Command is not (CliCommand.ListInstall or CliCommand.InstallApps))
        {
            error = "Opcja --apps-catalog dotyczy tylko --list-install i --install-apps.";
        }

        IReadOnlyList<string> installIds = Array.Empty<string>();
        if (error is null && commands[0].Command == CliCommand.InstallApps)
        {
            error = ParseInstallIds(commands[0].Id, out installIds);
        }

        if (error is null && commands[0].Command == CliCommand.SearchApps && !WingetArguments.TryValidateQuery(commands[0].Id, out var queryError))
        {
            error = "Nieprawidłowe zapytanie --search-apps: " + queryError;
        }

        if (error is not null)
        {
            return new CliParseResult { Error = error + " Składnia: " + Usage, OutPath = outPath };
        }

        return new CliParseResult
        {
            OutPath = outPath,
            Options = new CliOptions
            {
                Command = commands[0].Command,
                // Polecenia instalacji niosą wartość w InstallIds/SearchQuery, nie jako id tweaka.
                TweakId = isInstallCommand ? string.Empty : commands[0].Id,
                CatalogPath = catalogPath,
                AppsCatalogPath = appsCatalogPath,
                InstallIds = installIds,
                SearchQuery = commands[0].Command == CliCommand.SearchApps ? commands[0].Id : null,
                OutPath = outPath!,
                // --remove-app: domyślnie „required” = bez punktu przywracania seria nie rusza – z programem (spec 6.3, R15) i z samymi AppX (R20).
                RestorePointPolicy = restorePoint
                    ?? (commands[0].Command == CliCommand.RemoveApp ? RestorePointPolicy.Required : RestorePointPolicy.IfAvailable),
            },
        };
    }

    /// <summary>Polecenia instalacji przez winget (Etap 4c).</summary>
    public static bool IsInstallCommand(CliCommand command)
        => command is CliCommand.ListInstall or CliCommand.InstallApps or CliCommand.SearchApps;

    /// <summary>
    /// Wartość <c>--install-apps</c>: id rozdzielone przecinkiem, bez spacji. Każde to <c>install.&lt;nazwa&gt;</c> albo
    /// <c>winget:&lt;pełny identyfikator&gt;</c> (<see cref="WingetArguments.IsValidWingetId"/>: Id obcięte „…” z <c>winget search</c> nie
    /// przechodzi, Review Focus 1). Pusty element i duplikat (bez wielkości liter) to błąd. Zwraca komunikat błędu albo <c>null</c>.
    /// </summary>
    private static string? ParseInstallIds(string value, out IReadOnlyList<string> ids)
    {
        ids = Array.Empty<string>();
        var parts = value.Split(',');
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var part in parts)
        {
            if (part.Length == 0)
            {
                return "Lista --install-apps ma pusty element (id rozdziela sam przecinek, bez spacji).";
            }

            if (part.StartsWith(InstallTarget.SearchPrefix, StringComparison.Ordinal))
            {
                if (!WingetArguments.IsValidWingetId(part.Substring(InstallTarget.SearchPrefix.Length)))
                {
                    return $"Nieprawidłowy identyfikator aplikacji '{part}': po winget: musi być pełny identyfikator pakietu, nie skrócony „…” "
                        + "ani pusty (zawęź wyszukiwanie --search-apps).";
                }
            }
            else if (!AppsInstallCatalog.IsValidId(part))
            {
                return $"Nieprawidłowy identyfikator aplikacji '{part}' (dozwolone: install.<nazwa> z listy albo winget:<identyfikator>).";
            }

            if (!seen.Add(part))
            {
                return $"Aplikacja '{part}' podana w --install-apps więcej niż raz.";
            }
        }

        ids = parts;
        return null;
    }

    private static bool TryGetCommand(string arg, out CliCommand command)
    {
        if (arg.Equals("--detect", StringComparison.OrdinalIgnoreCase))
        {
            command = CliCommand.Detect;
            return true;
        }

        if (arg.Equals("--apply", StringComparison.OrdinalIgnoreCase))
        {
            command = CliCommand.Apply;
            return true;
        }

        if (arg.Equals("--undo", StringComparison.OrdinalIgnoreCase))
        {
            command = CliCommand.Undo;
            return true;
        }

        if (arg.Equals("--execute", StringComparison.OrdinalIgnoreCase))
        {
            command = CliCommand.Execute;
            return true;
        }

        if (arg.Equals("--remove-app", StringComparison.OrdinalIgnoreCase))
        {
            command = CliCommand.RemoveApp;
            return true;
        }

        if (arg.Equals("--install-apps", StringComparison.OrdinalIgnoreCase))
        {
            command = CliCommand.InstallApps;
            return true;
        }

        if (arg.Equals("--search-apps", StringComparison.OrdinalIgnoreCase))
        {
            command = CliCommand.SearchApps;
            return true;
        }

        command = default;
        return false;
    }

    private static bool TryGetPolicy(string value, out RestorePointPolicy policy)
    {
        if (value.Equals("required", StringComparison.OrdinalIgnoreCase))
        {
            policy = RestorePointPolicy.Required;
            return true;
        }

        if (value.Equals("ifavailable", StringComparison.OrdinalIgnoreCase))
        {
            policy = RestorePointPolicy.IfAvailable;
            return true;
        }

        if (value.Equals("skip", StringComparison.OrdinalIgnoreCase))
        {
            policy = RestorePointPolicy.Skip;
            return true;
        }

        policy = default;
        return false;
    }
}
