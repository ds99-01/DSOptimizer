using System.Collections.Generic;
using System.IO;
using System.Text;
using DSOptimizer.Core.Repair;
using DSOptimizer.Tests.Unit.Support;

namespace DSOptimizer.Tests.Unit.Install;

/// <summary>
/// Próbki wyjścia winget z VM 26H2 Pro (pomiar wstępny Etapu 4c, stdout przekierowany potokiem), czytane prosto z repo,
/// bajt w bajt, i dzielone na linie tak jak w programie: <see cref="ConsoleLineSplitter"/> z UTF-8 bez BOM.
/// </summary>
internal static class InstallSamples
{
    public static string PathOf(string name) => RepoRoot.Combine("tests", "DSOptimizer.Tests.Unit", "Install", "Samples", name);

    public static List<string> Lines(string name)
    {
        var splitter = new ConsoleLineSplitter(new UTF8Encoding(false));
        var lines = new List<string>();
        splitter.Line += lines.Add;
        var bytes = File.ReadAllBytes(PathOf(name));
        splitter.Write(bytes, bytes.Length);
        splitter.Flush();
        return lines;
    }
}
