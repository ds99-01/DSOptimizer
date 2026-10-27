namespace DSOptimizer.Core.Gui;

/// <summary>Operacja, którą okno może zlecić silnikowi.</summary>
public enum GuiOperation
{
    Apply,
    Undo,
    Execute,

    /// <summary>Usunięcie aplikacji ze strony „Aplikacje” (osobna seria, bez cofania).</summary>
    RemoveApps,

    /// <summary>Instalacja aplikacji przez winget ze strony „Instalacja” (osobna seria, bez punktu przywracania i bez cofania).</summary>
    InstallApps,
}