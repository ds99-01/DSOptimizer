namespace DSOptimizer.Core.Abstractions;

/// <summary>Zalogowany interaktywny użytkownik — cel zmian HKCU (HKEY_USERS\&lt;Sid&gt;).</summary>
public interface IUserContext
{
    string Sid { get; }

    /// <summary>false = nie znaleziono sesji interaktywnej, użyto konta bieżącego procesu.</summary>
    bool IsCertain { get; }
}
