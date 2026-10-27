using System;
using System.Security.Principal;
using DSOptimizer.Core.Abstractions;

namespace DSOptimizer.Windows;

/// <summary>
/// SID na nazwę konta przez LSA (<c>SecurityIdentifier.Translate</c>), bez domeny/komputera przed „\”.
/// Może blokować (kontroler domeny poza zasięgiem), więc wołać poza wątkiem UI. Bez cache (cache robi <c>ChangeHistory.Build</c>).
/// </summary>
public sealed class WindowsAccountNames : IAccountNames
{
    public string? NameOf(string sid)
    {
        try
        {
            var account = new SecurityIdentifier(sid).Translate(typeof(NTAccount)).Value;
            var slash = account.LastIndexOf('\\');
            return slash >= 0 ? account.Substring(slash + 1) : account;
        }
        catch (SystemException)
        {
            // IdentityNotMappedException, zły SID, błąd LSA/domeny: brak nazwy, wołający pokaże SID.
            return null;
        }
    }
}
