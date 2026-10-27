using System.Collections.Generic;
using DSOptimizer.Core.Model;

namespace DSOptimizer.Core.Abstractions;

/// <summary>Dostęp do rejestru (widok 64-bit). Ścieżki: HKLM\, HKU\, HKCR\ — HKCU mapuje wcześniej OperationFactory.</summary>
public interface IRegistry
{
    RegistryReadResult Read(string path, string name);

    /// <summary>Zapisuje wartość z typem; <see cref="RegValue.Absent"/> usuwa wartość. Błędy: OperationException.</summary>
    void Write(string path, string name, RegValue value);

    /// <summary>
    /// Czy klucz istnieje (Found/NotFound/AccessDenied/Error). Błędy dostępu i wejścia/wyjścia to statusy, nie wyjątki;
    /// rzuca tylko <see cref="System.ArgumentException"/> przy nieobsługiwanym korzeniu ścieżki (błąd programisty).
    /// </summary>
    ReadStatus KeyStatus(string path);

    /// <summary>
    /// Nazwy bezpośrednich podkluczy klucza (bez podkluczy głębszych poziomów), w kolejności zwróconej przez system. Brak klucza =
    /// pusta lista. Brak dostępu albo błąd wejścia/wyjścia = <see cref="Errors.OperationException"/>; nieobsługiwany korzeń ścieżki =
    /// <see cref="System.ArgumentException"/>.
    /// </summary>
    IReadOnlyList<string> SubKeyNames(string path);

    /// <summary>
    /// Usuwa klucz, jeśli nie ma wartości (łącznie z domyślną) ani podkluczy. True = usunięty albo go nie było;
    /// false = klucz niepusty (także gdy podklucz pojawił się w trakcie usuwania). Błędy: OperationException.
    /// </summary>
    bool DeleteKeyIfEmpty(string path);

    /// <summary>
    /// Usuwa klucz razem z podkluczami i wartościami. Brak klucza = nic do zrobienia. Ścieżka musi wskazywać co najmniej
    /// podklucz drugiego poziomu (nigdy sam korzeń ani jego bezpośrednie dziecko). Błędy: OperationException.
    /// </summary>
    void DeleteKeyTree(string path);
}
