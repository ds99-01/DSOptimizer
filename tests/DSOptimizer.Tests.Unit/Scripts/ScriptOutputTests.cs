using System;
using DSOptimizer.Core.Scripts;
using Xunit;

namespace DSOptimizer.Tests.Unit.Scripts;

public class ScriptOutputTests
{
    [Fact]
    public void LastNonEmptyLine_SkipsBlankAndTrims()
        => Assert.Equal("Applied", ScriptOutput.LastNonEmptyLine(new[] { "a", " Applied \t", string.Empty, "   " }));

    [Fact]
    public void LastNonEmptyLine_NoContent_IsNull()
        => Assert.Null(ScriptOutput.LastNonEmptyLine(Array.Empty<string>()));
}
