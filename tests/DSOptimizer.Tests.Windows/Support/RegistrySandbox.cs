using System;
using Microsoft.Win32;

namespace DSOptimizer.Tests.Windows.Support;

/// <summary>Jednorazowy klucz HKCU\Software\DSOptimizer.Tests\&lt;guid&gt; — jedyne miejsce w rejestrze, które ruszają testy.</summary>
public sealed class RegistrySandbox : IDisposable
{
    private readonly string _relative = @"Software\DSOptimizer.Tests\" + Guid.NewGuid().ToString("N");

    public string HkcuPath => @"HKCU\" + _relative;

    public string HkuPath(string sid) => @"HKU\" + sid + @"\" + _relative;

    public RegistryKey OpenOrCreate() => Registry.CurrentUser.CreateSubKey(_relative, writable: true)!;

    public void Dispose() => Registry.CurrentUser.DeleteSubKeyTree(_relative, throwOnMissingSubKey: false);
}
