using System;
using System.IO;
using DSOptimizer.Windows;

namespace DSOptimizer.Tests.Windows.Support;

public sealed class TempDirectory : IDisposable
{
    public TempDirectory()
    {
        Root = Path.Combine(Path.GetTempPath(), "dso-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Root);
    }

    public string Root { get; }

    public SecureRoot AppRoot => new(Sub("app"));

    public string Sub(string name) => Path.Combine(Root, name);

    public void Dispose()
    {
        try
        {
            Directory.Delete(Root, recursive: true);
        }
        catch (IOException)
        {
            // Sprzątanie best-effort — plik może jeszcze trzymać antywirus.
        }
        catch (UnauthorizedAccessException)
        {
            // j.w.
        }
    }
}
