using System;
using System.Collections.Generic;

namespace DSOptimizer.Core.Abstractions;

/// <summary>Plik pomiaru Windows <c>&lt;SID&gt;_StartupInfo&lt;N&gt;.xml</c>: <see cref="Number"/> = N (rośnie z każdym rozruchem), czas zapisu pliku.</summary>
public sealed record StartupInfoFile(string Path, int Number, DateTimeOffset WrittenAt);

/// <summary>Pliki pomiaru startu, które Windows zapisuje po zalogowaniu (<c>wdi\LogFiles\StartupInfo</c>) – tylko odczyt.</summary>
public interface IStartupInfoSource
{
    /// <summary>Pliki pomiaru konta. Brak katalogu = pusta lista; brak dostępu albo błąd = <see cref="Errors.OperationException"/>.</summary>
    IReadOnlyList<StartupInfoFile> Files(string userSid);

    /// <summary>Treść pliku (XML zdekodowany z UTF-16). Brak dostępu albo błąd = <see cref="Errors.OperationException"/>.</summary>
    string Read(StartupInfoFile file);
}
