using System;
using DSOptimizer.Core.Scripts;
using Xunit;

namespace DSOptimizer.Tests.Unit.Scripts;

public class ScriptTextsTests
{
    [Fact]
    public void Find_ReadsLiteral()
        => Assert.Equal(new[] { "Usunięto {0} plików." }, ScriptTexts.Find("Write-Output (Get-DsoText 'Usunięto {0} plików.' $count)"));

    [Fact]
    public void Find_DoubledApostrophe()
        => Assert.Equal(new[] { "Plik 'a' i ''" }, ScriptTexts.Find("throw (Get-DsoText 'Plik ''a'' i ''''')"));

    [Fact]
    public void Find_IgnoresVariableArgument()
    {
        Assert.Empty(ScriptTexts.Find("Get-DsoText $text\nGet-DsoText \"Tekst\""));
        Assert.Equal(new[] { "Drugi" }, ScriptTexts.Find("Get-DsoText $text 'Pierwszy'\nGet-DsoText   'Drugi' 'Trzeci'"));
    }

    [Fact]
    public void Find_NoDuplicates()
    {
        var found = ScriptTexts.Find("Get-DsoText 'B'\nGet-DsoText 'A'\nGet-DsoText 'B'\nGet-DsoText 'b'");
        Assert.Equal(new[] { "B", "A", "b" }, found);
    }

    [Fact]
    public void Find_Null_Throws()
        => Assert.Throws<ArgumentNullException>(() => ScriptTexts.Find(null!));
}
