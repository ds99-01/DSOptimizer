using System.Diagnostics.CodeAnalysis;

namespace DSOptimizer.Core.Model;

[SuppressMessage("Naming", "CA1720:Identifiers should not contain type names", Justification = "Nazwy typów wymusza format JSON katalogu.")]
public enum RegistryValueType
{
    String,
    ExpandString,
    DWord,
    QWord,
    MultiString,
    Binary,
}
