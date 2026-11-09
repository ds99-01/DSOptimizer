using System.Collections.Generic;
using DSOptimizer.App.Gui;

namespace DSOptimizer.Tests.Windows.Support;

/// <summary>Okno „Zapisz jako” bez okna: zapamiętuje proponowane nazwy i zwraca <see cref="Answer"/> (<c>null</c> = anulowano).</summary>
internal sealed class FakeSaveFileDialog : ISaveFileDialog
{
    public FakeSaveFileDialog(string? answer)
    {
        Answer = answer;
    }

    public string? Answer { get; set; }

    public List<string> Asked { get; } = new();

    public string? AskPath(string fileName)
    {
        Asked.Add(fileName);
        return Answer;
    }
}
