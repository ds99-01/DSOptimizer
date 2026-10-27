using System.Collections.Generic;
using System.IO;
using System.Text;
using DSOptimizer.Core.Repair;
using DSOptimizer.Tests.Unit.Support;

namespace DSOptimizer.Tests.Unit.Repair;

/// <summary>Próbki wyjścia DISM/SFC i wycinki CBS.log z VM 26H2 (Task 2), czytane prosto z repo, bajt w bajt.</summary>
internal static class RepairSamples
{
    public static readonly Encoding Oem852 = Encoding.GetEncoding(852);

    public static string PathOf(string name) => RepoRoot.Combine("tests", "DSOptimizer.Tests.Unit", "Repair", "Samples", name);

    public static byte[] Bytes(string name) => File.ReadAllBytes(PathOf(name));

    /// <summary>Tekst wycinka CBS.log (UTF-8, jak czyta go <c>IRepairFileSystem.ReadSharedFrom</c>).</summary>
    public static string Text(string name) => File.ReadAllText(PathOf(name), Encoding.UTF8);

    /// <summary>Linie wyjścia narzędzia po <see cref="ConsoleLineSplitter"/> (porcje po <paramref name="chunk"/> bajtów).</summary>
    public static List<string> Lines(string name, int chunk = 4096)
    {
        var splitter = new ConsoleLineSplitter(Oem852);
        var lines = new List<string>();
        splitter.Line += lines.Add;
        var bytes = Bytes(name);
        for (var offset = 0; offset < bytes.Length; offset += chunk)
        {
            var count = System.Math.Min(chunk, bytes.Length - offset);
            var part = new byte[count];
            System.Buffer.BlockCopy(bytes, offset, part, 0, count);
            splitter.Write(part, count);
        }

        splitter.Flush();
        return lines;
    }
}
