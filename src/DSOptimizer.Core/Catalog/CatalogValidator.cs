using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using DSOptimizer.Core.Engine;
using DSOptimizer.Core.Model;

namespace DSOptimizer.Core.Catalog;

/// <summary>Reguły katalogu ze specu (sekcja 9). Zwraca wszystkie błędy naraz.</summary>
public static class CatalogValidator
{
    /// <summary>Najwięcej znaków w krótkim opisie (summary).</summary>
    public const int SummaryMaxLength = 160;

    /// <summary>Najwięcej znaków w sekcji „Co zyskasz” (benefit).</summary>
    public const int BenefitMaxLength = 220;

    private static readonly Regex IdPattern = new(@"^[a-z0-9]+(\.[a-z0-9-]+)+$", RegexOptions.CultureInvariant);

    private static readonly string[] AllowedRoots = { @"HKLM\", @"HKCU\", @"HKCR\", @"HKU\" };

    public static IReadOnlyList<ValidationError> Validate(IReadOnlyList<TweakDefinition> definitions)
    {
        if (definitions is null)
        {
            throw new ArgumentNullException(nameof(definitions));
        }

        var errors = new List<ValidationError>();
        ValidateIdentity(definitions, errors);
        foreach (var definition in definitions)
        {
            ValidateDefinition(definition, errors);
        }

        return errors;
    }

    private static void ValidateIdentity(IReadOnlyList<TweakDefinition> definitions, List<ValidationError> errors)
    {
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var definition in definitions)
        {
            if (!IdPattern.IsMatch(definition.Id))
            {
                errors.Add(new ValidationError(definition.Id, "id-format", "Id musi mieć postać 'kategoria.nazwa' (małe litery, cyfry, myślniki)."));
            }

            if (!ids.Add(definition.Id))
            {
                errors.Add(new ValidationError(definition.Id, "id-unique", "Zduplikowane id."));
            }
        }

        var aliases = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var definition in definitions)
        {
            foreach (var alias in definition.Aliases)
            {
                if (ids.Contains(alias) || !aliases.Add(alias))
                {
                    errors.Add(new ValidationError(definition.Id, "alias-unique", $"Alias '{alias}' koliduje z innym id lub aliasem."));
                }
            }
        }
    }

    private static void ValidateDefinition(TweakDefinition definition, List<ValidationError> errors)
    {
        void Add(string rule, string message) => errors.Add(new ValidationError(definition.Id, rule, message));

        if (string.IsNullOrWhiteSpace(definition.Title))
        {
            Add("required-field", "Brak tytułu.");
        }

        if (string.IsNullOrWhiteSpace(definition.Category))
        {
            Add("required-field", "Brak kategorii.");
        }

        if (string.IsNullOrWhiteSpace(definition.Summary))
        {
            Add("summary", "Brak krótkiego opisu (summary).");
        }
        else if (definition.Summary.Length > SummaryMaxLength)
        {
            Add("summary", $"summary ma {definition.Summary.Length} znaków (limit {SummaryMaxLength}).");
        }

        ValidateBenefit(definition, Add);

        if (definition.Suggested && definition.Kind != TweakKind.Tweak)
        {
            Add("suggested", "Pole suggested jest dozwolone tylko dla kind=tweak.");
        }

        if (definition.Editions is { } editions)
        {
            if (editions.Count == 0)
            {
                Add("editions", "Pole editions nie może być puste (pomiń je, by pozycja działała na wszystkich edycjach).");
            }
            else if (editions.Distinct().Count() != editions.Count)
            {
                Add("editions", "Pole editions zawiera zduplikowaną rodzinę edycji.");
            }
        }

        if (definition.MinBuild is { } min && definition.MaxBuild is { } max && min > max)
        {
            Add("build-range", "minBuild jest większy niż maxBuild.");
        }

        if (definition.Timeout is { } timeout && timeout <= TimeSpan.Zero)
        {
            Add("timeout", "timeoutSeconds musi być większe od zera.");
        }

        foreach (var entry in definition.Registry.Where(entry => !AllowedRoots.Any(root => entry.Path.StartsWith(root, StringComparison.OrdinalIgnoreCase))))
        {
            Add("registry-path", $"Ścieżka '{entry.Path}' musi zaczynać się od HKLM\\, HKCU\\, HKCR\\ lub HKU\\.");
        }

        ValidateScope(definition, Add);

        if (ScriptTexts(definition.Script).Any(text => text.IndexOf("HKCU:", StringComparison.OrdinalIgnoreCase) >= 0))
        {
            Add("script-hkcu", "Skrypt nie może używać HKCU: – użyj Registry::HKEY_USERS\\$UserSid.");
        }

        if (definition.Launch is { } launch && !LaunchTarget.IsAllowed(launch.File))
        {
            Add("launch-file", $"launch.file '{launch.File}': dozwolona jest tylko nazwa pliku .exe/.cpl/.msc z System32 (bez ścieżki) albo shell:::{{GUID}}.");
        }

        if (definition.Handler is { } handler && !ActionHandlerNames.All.Contains(handler))
        {
            Add("handler-unknown", $"handler '{handler}' nie jest znany (dozwolone: {string.Join(", ", ActionHandlerNames.All)}).");
        }

        if (definition.Kind == TweakKind.Tweak)
        {
            ValidateTweak(definition, Add);
        }
        else
        {
            ValidateAction(definition, Add);
        }
    }

    private static void ValidateBenefit(TweakDefinition definition, Action<string, string> add)
    {
        if (definition.Kind == TweakKind.Panel)
        {
            if (definition.Benefit is not null)
            {
                add("benefit", "Panel nie może mieć benefit – sekcja „Co zyskasz” jest tylko dla tweaków i akcji.");
            }
        }
        else if (string.IsNullOrWhiteSpace(definition.Benefit))
        {
            add("benefit", "Brak opisu realnego zysku (benefit).");
        }
        else if (definition.Benefit!.Length > BenefitMaxLength)
        {
            add("benefit", $"benefit ma {definition.Benefit.Length} znaków (limit {BenefitMaxLength}).");
        }
    }

    private static void ValidateScope(TweakDefinition definition, Action<string, string> add)
    {
        if (definition.Scope == TweakScope.User)
        {
            if (definition.Registry.Any(entry => !IsHkcu(entry.Path)))
            {
                add("scope", "Tweak scope=user może zmieniać tylko HKCU\\.");
            }

            if (definition.Services.Count > 0)
            {
                add("scope", "Tweak scope=user nie może zmieniać usług.");
            }
        }
        else if (definition.Registry.Any(entry => IsHkcu(entry.Path)))
        {
            add("scope", "Tweak scope=machine nie może zmieniać HKCU\\.");
        }
    }

    private static void ValidateTweak(TweakDefinition definition, Action<string, string> add)
    {
        if (definition.Launch is not null)
        {
            add("launch-shape", "Tweak nie może mieć launch – launch jest tylko dla akcji i paneli.");
        }

        if (definition.Handler is not null)
        {
            add("handler-kind", "Tweak nie może mieć handler – handler jest tylko dla akcji.");
        }

        var hasDetection = definition.Registry.Count > 0 || definition.Services.Count > 0 || definition.Script?.Detect is not null;
        if (!hasDetection)
        {
            add("tweak-detection", "Tweak musi mieć detekcję (rejestr, usługa lub script.detect).");
        }

        if (!definition.Reversible)
        {
            if (definition.Warnings.Count == 0)
            {
                add("tweak-reversible", "Nieodwracalny tweak musi mieć ostrzeżenie.");
            }

            return;
        }

        foreach (var entry in definition.Registry.Where(entry => !entry.DetectOnly && entry.Original is null))
        {
            add("tweak-reversible", $"Wpis {entry.Path}\\{entry.Name} nie ma 'original'.");
        }

        foreach (var service in definition.Services.Where(service => service.Original is null))
        {
            add("tweak-reversible", $"Usługa {service.Name} nie ma 'original'.");
        }

        if (definition.Script?.Apply is not null && definition.Script.Undo is null)
        {
            add("tweak-reversible", "Skrypt apply wymaga skryptu undo.");
        }
    }

    private static void ValidateAction(TweakDefinition definition, Action<string, string> add)
    {
        var hasApply = definition.Script?.Apply is not null;
        var hasLaunch = definition.Launch is not null;
        var hasHandler = definition.Handler is not null;
        var ways = (hasApply ? 1 : 0) + (hasLaunch ? 1 : 0) + (hasHandler ? 1 : 0);
        if (ways == 0)
        {
            add("action-shape", "Akcja/panel wymaga script.apply, launch albo handler.");
        }
        else if (ways > 1)
        {
            add("action-shape", "Akcja/panel ma dokładnie jedną drogę wykonania: script.apply, launch albo handler.");
        }

        if (hasHandler && definition.Kind == TweakKind.Panel)
        {
            add("handler-kind", "Panel nie może mieć handler – handler jest tylko dla akcji.");
        }

        if (definition.Registry.Count > 0 || definition.Services.Count > 0)
        {
            add("action-shape", "Akcja/panel nie może mieć wpisów registry/services.");
        }
    }

    private static bool IsHkcu(string path) => path.StartsWith(@"HKCU\", StringComparison.OrdinalIgnoreCase);

    private static IEnumerable<string> ScriptTexts(ScriptSet? script)
        => script is null
            ? Enumerable.Empty<string>()
            : new[] { script.Apply, script.Undo, script.Detect, script.Capture }.OfType<string>();
}
