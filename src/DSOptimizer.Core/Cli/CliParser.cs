using System;
using System.Collections.Generic;
using DSOptimizer.Core.Autostart;
using DSOptimizer.Core.Engine;
using DSOptimizer.Core.Install;
using DSOptimizer.Core.Localization;

namespace DSOptimizer.Core.Cli;

public static class CliParser
{
    public static string Usage => L.T("DSOptimizer.exe --detect <id> | --apply <id> [--restore-point required|ifavailable|skip] | --undo <id> | --execute <id> [--catalog <plik.json>] --out <plik.json> | DSOptimizer.exe --list-apps | --remove-app <id> [--restore-point required|ifavailable|skip] --out <plik.json> | DSOptimizer.exe --list-install [--apps-catalog <plik.json>] | --install-apps <id>[,<id>…] [--apps-catalog <plik.json>] | --search-apps <tekst> --out <plik.json> | DSOptimizer.exe --list-autostart | --set-autostart <id> disabled|enabled | --undo-autostart <id> --out <plik.json> [--lang pl|en]");

    private const string ListAppsOption = "--list-apps";
    private const string ListInstallOption = "--list-install";
    private const string ListAutostartOption = "--list-autostart";
    private const string LangOption = "--lang";

    /// <summary>
    /// Wartość pierwszego <c>--lang</c> (nazwa opcji bez wielkości liter, wartość <c>pl</c>/<c>en</c> przez
    /// <see cref="UiLanguageCodes.TryParse"/>); <c>null</c>, gdy opcji brak albo jej wartość jest zła lub nie ma jej wcale.
    /// </summary>
    public static UiLanguage? FindLanguage(IReadOnlyList<string> args)
    {
        if (args is null)
        {
            throw new ArgumentNullException(nameof(args));
        }

        for (var i = 0; i < args.Count; i++)
        {
            if (!args[i].Equals(LangOption, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            return i + 1 < args.Count && UiLanguageCodes.TryParse(args[i + 1], out var language) ? language : null;
        }

        return null;
    }

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
        var langSeen = false;
        var commands = new List<(CliCommand Command, string Id, AutostartState? Target)>();

        for (var i = 0; i < args.Count; i++)
        {
            var arg = args[i];
            if (!arg.StartsWith("--", StringComparison.Ordinal))
            {
                error ??= L.F("Nieoczekiwany argument '{0}'.", arg);
                continue;
            }

            if (TryGetValuelessCommand(arg, out var valueless))
            {
                // Komendy bez wartości: następny argument nie jest ich wartością.
                commands.Add((valueless, string.Empty, null));
                continue;
            }

            var isCommand = TryGetCommand(arg, out var command);
            var isKnownOption = isCommand
                || arg.Equals("--catalog", StringComparison.OrdinalIgnoreCase)
                || arg.Equals("--apps-catalog", StringComparison.OrdinalIgnoreCase)
                || arg.Equals("--restore-point", StringComparison.OrdinalIgnoreCase)
                || arg.Equals(LangOption, StringComparison.OrdinalIgnoreCase)
                || arg.Equals("--out", StringComparison.OrdinalIgnoreCase);
            if (!isKnownOption)
            {
                error ??= L.F("Nieznana opcja '{0}'.", arg);
                continue;
            }

            if (i + 1 >= args.Count || args[i + 1].StartsWith("--", StringComparison.Ordinal))
            {
                error ??= L.F("Opcja {0} wymaga wartości.", arg);
                continue;
            }

            var value = args[++i];
            if (isCommand)
            {
                AutostartState? target = null;
                if (command == CliCommand.SetAutostart)
                {
                    // Druga wartość --set-autostart: stan docelowy (bez wielkości liter); brak albo inny = błąd składni.
                    if (i + 1 >= args.Count || args[i + 1].StartsWith("--", StringComparison.Ordinal))
                    {
                        error ??= L.T("Opcja --set-autostart wymaga stanu po id (disabled albo enabled).");
                    }
                    else if (TryGetAutostartState(args[++i], out var state))
                    {
                        target = state;
                    }
                    else
                    {
                        error ??= L.F("Nieznany stan --set-autostart '{0}' (dozwolone: disabled, enabled).", args[i]);
                    }
                }

                commands.Add((command, value, target));
            }
            else if (arg.Equals("--out", StringComparison.OrdinalIgnoreCase))
            {
                if (outPath is null)
                {
                    outPath = value;
                }
                else
                {
                    error ??= L.T("Opcja --out podana więcej niż raz.");
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
                    error ??= L.T("Opcja --catalog podana więcej niż raz.");
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
                    error ??= L.T("Opcja --apps-catalog podana więcej niż raz.");
                }
            }
            else if (arg.Equals(LangOption, StringComparison.OrdinalIgnoreCase))
            {
                if (langSeen)
                {
                    error ??= L.T("Opcja --lang podana więcej niż raz.");
                }
                else if (UiLanguageCodes.TryParse(value, out _))
                {
                    langSeen = true;
                }
                else
                {
                    langSeen = true;
                    error ??= L.F("Nieznany język '{0}' w --lang (dozwolone: pl, en).", value);
                }
            }
            else if (restorePoint is not null)
            {
                error ??= L.T("Opcja --restore-point podana więcej niż raz.");
            }
            else if (TryGetPolicy(value, out var policy))
            {
                restorePoint = policy;
            }
            else
            {
                error ??= L.F("Nieznana wartość --restore-point '{0}' (dozwolone: required, ifavailable, skip).", value);
            }
        }

        if (error is null && commands.Count != 1)
        {
            error = L.T("Podaj dokładnie jedno z: --detect, --apply, --undo, --execute, --list-apps, --remove-app, --list-install, --install-apps, --search-apps, --list-autostart, --set-autostart, --undo-autostart.");
        }

        if (error is null && outPath is null)
        {
            error = L.T("Brak wymaganej opcji --out.");
        }

        var isAppCommand = commands.Count == 1 && commands[0].Command is CliCommand.ListApps or CliCommand.RemoveApp;
        if (error is null && restorePoint is not null && commands[0].Command is not (CliCommand.Apply or CliCommand.RemoveApp))
        {
            error = L.T("Opcja --restore-point dotyczy tylko --apply i --remove-app.");
        }

        if (error is null && catalogPath is not null && isAppCommand)
        {
            error = L.T("Opcja --catalog nie dotyczy --list-apps ani --remove-app.");
        }

        var isInstallCommand = commands.Count == 1 && IsInstallCommand(commands[0].Command);
        if (error is null && catalogPath is not null && isInstallCommand)
        {
            error = L.T("Opcja --catalog nie dotyczy poleceń instalacji (listę aplikacji podmienia --apps-catalog).");
        }

        if (error is null && catalogPath is not null && IsAutostartCommand(commands[0].Command))
        {
            error = L.T("Opcja --catalog nie dotyczy poleceń autostartu.");
        }

        if (error is null && appsCatalogPath is not null && commands[0].Command is not (CliCommand.ListInstall or CliCommand.InstallApps))
        {
            error = L.T("Opcja --apps-catalog dotyczy tylko --list-install i --install-apps.");
        }

        IReadOnlyList<string> installIds = Array.Empty<string>();
        if (error is null && commands[0].Command == CliCommand.InstallApps)
        {
            error = ParseInstallIds(commands[0].Id, out installIds);
        }

        if (error is null && commands[0].Command == CliCommand.SearchApps && !WingetArguments.TryValidateQuery(commands[0].Id, out var queryError))
        {
            error = L.F("Nieprawidłowe zapytanie --search-apps: {0}", queryError);
        }

        if (error is not null)
        {
            return new CliParseResult { Error = L.F("{0} Składnia: {1}", error, Usage), OutPath = outPath };
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
                AutostartTarget = commands[0].Target,
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

    /// <summary>Polecenia autostartu: lista, przełączenie jednej pozycji i cofnięcie jej aktywnej zmiany.</summary>
    public static bool IsAutostartCommand(CliCommand command)
        => command is CliCommand.ListAutostart or CliCommand.SetAutostart or CliCommand.UndoAutostart;

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
                return L.T("Lista --install-apps ma pusty element (id rozdziela sam przecinek, bez spacji).");
            }

            if (part.StartsWith(InstallTarget.SearchPrefix, StringComparison.Ordinal))
            {
                if (!WingetArguments.IsValidWingetId(part.Substring(InstallTarget.SearchPrefix.Length)))
                {
                    return L.F("Nieprawidłowy identyfikator aplikacji '{0}': po winget: musi być pełny identyfikator pakietu, nie skrócony „…” ani pusty (zawęź wyszukiwanie --search-apps).", part);
                }
            }
            else if (!AppsInstallCatalog.IsValidId(part))
            {
                return L.F("Nieprawidłowy identyfikator aplikacji '{0}' (dozwolone: install.<nazwa> z listy albo winget:<identyfikator>).", part);
            }

            if (!seen.Add(part))
            {
                return L.F("Aplikacja '{0}' podana w --install-apps więcej niż raz.", part);
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

        if (arg.Equals("--set-autostart", StringComparison.OrdinalIgnoreCase))
        {
            command = CliCommand.SetAutostart;
            return true;
        }

        if (arg.Equals("--undo-autostart", StringComparison.OrdinalIgnoreCase))
        {
            command = CliCommand.UndoAutostart;
            return true;
        }

        command = default;
        return false;
    }

    private static bool TryGetValuelessCommand(string arg, out CliCommand command)
    {
        if (arg.Equals(ListAppsOption, StringComparison.OrdinalIgnoreCase))
        {
            command = CliCommand.ListApps;
            return true;
        }

        if (arg.Equals(ListInstallOption, StringComparison.OrdinalIgnoreCase))
        {
            command = CliCommand.ListInstall;
            return true;
        }

        if (arg.Equals(ListAutostartOption, StringComparison.OrdinalIgnoreCase))
        {
            command = CliCommand.ListAutostart;
            return true;
        }

        command = default;
        return false;
    }

    private static bool TryGetAutostartState(string value, out AutostartState state)
    {
        if (value.Equals("disabled", StringComparison.OrdinalIgnoreCase))
        {
            state = AutostartState.Disabled;
            return true;
        }

        if (value.Equals("enabled", StringComparison.OrdinalIgnoreCase))
        {
            state = AutostartState.Enabled;
            return true;
        }

        state = default;
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
