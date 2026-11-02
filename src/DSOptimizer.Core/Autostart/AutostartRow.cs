namespace DSOptimizer.Core.Autostart;

/// <summary>Grupa listy: brak pliku wygrywa ze stanem, stan nieznany jest w grupie włączonych.</summary>
public enum AutostartGroup
{
    Enabled,
    Disabled,
    MissingFile,
}

/// <summary>Powód „tylko do odczytu” (pierwszy pasujący w <see cref="AutostartClassifier.Classify"/>); <see cref="None"/> = można przełączać.</summary>
public enum AutostartBlocker
{
    None,
    Protected,
    UserUncertain,
    UserHiveMissing,
    Unreadable,
    UnknownFormat,
    TaskStateNotSupported,
    WriteFromZeroNotMeasured,
    NoName,
}

/// <summary>Pozycja autostartu po ocenie: wpis z bazy (null = bez oceny) i powód blokady.</summary>
public sealed record AutostartRow(AutostartEntry Entry, AutostartKnownEntry? Known, AutostartBlocker Blocker)
{
    public string Id => Entry.Id;

    public AutostartState State => Entry.Switch.State;

    public AutostartVerdict Verdict => Known?.Verdict ?? AutostartVerdict.None;

    public bool MissingFile => Entry.FileStatus == AutostartFileStatus.Missing;

    public AutostartGroup Group => MissingFile
        ? AutostartGroup.MissingFile
        : State == AutostartState.Disabled ? AutostartGroup.Disabled : AutostartGroup.Enabled;

    /// <summary>
    /// Jedyna reguła pola wyboru (strona, potwierdzenie, seria, CLI): bez blokady, nie chroniona, format zapisywalny i znany stan. Werdykt,
    /// format i stan sprawdzane jeszcze raz obok <see cref="Blocker"/> – obrona w głąb na wypadek wiersza złożonego poza klasyfikatorem.
    /// </summary>
    public bool CanToggle => Blocker == AutostartBlocker.None
        && Verdict != AutostartVerdict.Protected
        && Entry.Switch.Format is AutostartSwitchFormat.Valid or AutostartSwitchFormat.Absent
        && State != AutostartState.Unknown;

    /// <summary>Sugestia wyłączenia (zaznacza ją dopiero „Zaznacz sugerowane”): włączona, przełączalna i z bazy „można wyłączyć” albo bez pliku.</summary>
    public bool Suggested => State == AutostartState.Enabled && CanToggle && (Verdict == AutostartVerdict.CanDisable || MissingFile);

    /// <summary>Tekst powodu blokady; null, gdy bez blokady.</summary>
    public string? BlockedReason => AutostartText.Blocked(Blocker);
}
