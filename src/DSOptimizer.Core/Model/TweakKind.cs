namespace DSOptimizer.Core.Model;

/// <summary>Rodzaj pozycji katalogu.</summary>
public enum TweakKind
{
    /// <summary>Zmiana stanu systemu z detekcją i undo.</summary>
    Tweak,
    /// <summary>Jednorazowa akcja bez stanu (np. restart Explorera).</summary>
    Action,
    /// <summary>Otwarcie panelu systemowego.</summary>
    Panel,
}
