using System;
using System.Collections.Generic;
using System.Linq;

namespace DSOptimizer.Core.Apps;

/// <summary>Grupa na liście: sugerowane śmieci, pozostałe albo chronione (widoczne bez pola wyboru).</summary>
public enum AppGroup
{
    Suggested,
    Other,
    Protected,
}

/// <summary>Czy wiersz można zaznaczyć do usunięcia; w przeciwnym razie powód blokady jest w <see cref="AppRow.BlockedReason"/>.</summary>
public enum AppRemovability
{
    Removable,
    Protected,
    OtherAccount,
    UserUncertain,
    NoSafeCommand,
}

/// <summary>Wiersz listy aplikacji. <see cref="Plan"/> jest wyliczany tylko dla programów Win32 (także chronionych, do panelu szczegółów).</summary>
public sealed record AppRow(
    InstalledApp App,
    AppGroup Group,
    string? BloatGroup,
    string? Summary,
    string? Warning,
    string? CleanupUrl,
    AppRemovability Removability,
    string? BlockedReason,
    UninstallPlan? Plan)
{
    /// <summary>
    /// Jedyna reguła „można zaznaczyć do usunięcia” (strona, potwierdzenie, seria): usuwalny, spoza grupy chronionej i z danymi do
    /// usunięcia (pakiet AppX albo plan deinstalacji programu).
    /// </summary>
    public bool CanRemove => Removability == AppRemovability.Removable
        && Group != AppGroup.Protected
        && (App.Kind == AppKind.Store ? App.Appx is not null : Plan is not null);
}

/// <summary>Klasyfikacja zainstalowanych aplikacji: ukrywanie, ochrona, śmieci i możliwość usunięcia.</summary>
public static class AppClassifier
{
    private static readonly string[] HiddenReleaseTypes = { "Update", "Hotfix", "Security Update" };

    /// <summary>
    /// Klasyfikuje aplikacje (ukryte pomija, kolejność wejścia zostaje). Ochrona wygrywa z każdą regułą śmieci; pierwszy pasujący
    /// <see cref="BloatEntry"/> wyznacza grupę. Gdy powodów blokady jest kilka: Protected, OtherAccount, UserUncertain, NoSafeCommand.
    /// </summary>
    public static IReadOnlyList<AppRow> Classify(
        IReadOnlyList<InstalledApp> apps,
        AppsBloatCatalog catalog,
        string userSid,
        bool userIsCertain,
        string processSid,
        Func<string, string?> accountName,
        Func<string, bool> fileExists,
        string system32)
    {
        if (apps is null)
        {
            throw new ArgumentNullException(nameof(apps));
        }

        if (catalog is null)
        {
            throw new ArgumentNullException(nameof(catalog));
        }

        return apps
            .Where(app => !IsHidden(app))
            .Select(app => ClassifyOne(app, catalog, userIsCertain, processSid, accountName, fileExists, system32))
            .ToList();
    }

    /// <summary>Ukryte całkiem: frameworki, zasoby i pakiety systemowe AppX; składniki systemu, aktualizacje i wpisy bez polecenia Win32.</summary>
    public static bool IsHidden(InstalledApp app)
    {
        if (app is null)
        {
            throw new ArgumentNullException(nameof(app));
        }

        if (app.Appx is { } appx)
        {
            return appx.IsFramework
                || appx.IsResourcePackage
                || appx.NonRemovable
                || string.Equals(appx.SignatureKind, "System", StringComparison.OrdinalIgnoreCase);
        }

        if (app.Win32 is { } win32)
        {
            return win32.SystemComponent
                || !string.IsNullOrWhiteSpace(win32.ParentKeyName)
                || HiddenReleaseTypes.Contains(win32.ReleaseType?.Trim(), StringComparer.OrdinalIgnoreCase)
                || (string.IsNullOrWhiteSpace(win32.UninstallString) && string.IsNullOrWhiteSpace(win32.QuietUninstallString));
        }

        return true;
    }

    private static AppRow ClassifyOne(
        InstalledApp app,
        AppsBloatCatalog catalog,
        bool userIsCertain,
        string processSid,
        Func<string, string?> accountName,
        Func<string, bool> fileExists,
        string system32)
    {
        UninstallPlan? plan = null;
        string? planReason = null;
        if (app.Win32 is not null)
        {
            (plan, planReason) = UninstallCommand.Plan(app.Win32, fileExists, system32);
        }

        var guard = catalog.Protected.FirstOrDefault(entry => entry.Match.Matches(app));
        if (guard is not null)
        {
            return new AppRow(app, AppGroup.Protected, null, null, null, null, AppRemovability.Protected, guard.Reason, plan);
        }

        var entry = catalog.Entries.FirstOrDefault(candidate => candidate.Match.Matches(app));
        var (removability, reason) = Removability(app, userIsCertain, processSid, accountName, planReason);
        if (entry is null)
        {
            return new AppRow(app, AppGroup.Other, null, null, null, null, removability, reason, plan);
        }

        var group = entry.Suggested ? AppGroup.Suggested : AppGroup.Other;
        return new AppRow(app, group, entry.Group, entry.Summary, entry.Warning, entry.CleanupUrl, removability, reason, plan);
    }

    private static (AppRemovability Removability, string? Reason) Removability(
        InstalledApp app,
        bool userIsCertain,
        string processSid,
        Func<string, string?> accountName,
        string? planReason)
    {
        var perUser = app.Kind == AppKind.Program && app.Scope == AppScope.User;
        if (perUser && IsForeignAccount(app.OwnerSid, processSid))
        {
            return (AppRemovability.OtherAccount, AppsText.OtherAccount(app.OwnerSid is null ? null : accountName(app.OwnerSid)));
        }

        if (!userIsCertain && (app.Kind == AppKind.Store || perUser))
        {
            return (AppRemovability.UserUncertain, AppsText.UserUncertain);
        }

        return planReason is not null
            ? (AppRemovability.NoSafeCommand, planReason)
            : (AppRemovability.Removable, null);
    }

    // Program per użytkownik jest „własny”, gdy należy do konta procesu: deinstalator biegnie na tokenie właściciela wpisu,
    // więc nie ma znaczenia, kto jest zalogowany.
    private static bool IsForeignAccount(string? ownerSid, string processSid)
        => !string.Equals(ownerSid, processSid, StringComparison.OrdinalIgnoreCase);
}
