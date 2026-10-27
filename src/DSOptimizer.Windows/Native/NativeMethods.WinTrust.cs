using System;
using System.Runtime.InteropServices;

namespace DSOptimizer.Windows.Native;

/// <summary>
/// wintrust: weryfikacja osadzonego podpisu Authenticode (<c>AuthenticodeVerifier</c>) i odczyt certyfikatu podpisującego z danych
/// stanu tej samej weryfikacji (<c>WTHelperProvDataFromStateData</c> → <c>WTHelperGetProvSignerFromChain</c>).
/// </summary>
internal static partial class NativeMethods
{
    internal const uint WtdUiNone = 2;
    internal const uint WtdRevokeNone = 0;
    internal const uint WtdChoiceFile = 1;
    internal const uint WtdStateActionVerify = 1;
    internal const uint WtdStateActionClose = 2;

    internal const int TrustENoSignature = unchecked((int)0x800B0100);
    internal const int TrustESubjectFormUnknown = unchecked((int)0x800B0003);
    internal const int TrustEProviderUnknown = unchecked((int)0x800B0001);

    /// <summary><c>WINTRUST_ACTION_GENERIC_VERIFY_V2</c>: polityka Authenticode.</summary>
    internal static readonly Guid WintrustActionGenericVerifyV2 = new("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");

    [DllImport("wintrust.dll", ExactSpelling = true)]
    internal static extern int WinVerifyTrust(IntPtr hwnd, ref Guid actionId, ref WinTrustData data);

    [DllImport("wintrust.dll", ExactSpelling = true)]
    internal static extern IntPtr WTHelperProvDataFromStateData(IntPtr stateData);

    [DllImport("wintrust.dll", ExactSpelling = true)]
    internal static extern IntPtr WTHelperGetProvSignerFromChain(
        IntPtr providerData,
        uint signerIndex,
        [MarshalAs(UnmanagedType.Bool)] bool counterSigner,
        uint counterSignerIndex);

    /// <summary><c>WINTRUST_FILE_INFO</c>.</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct WinTrustFileInfo
    {
        public uint StructSize;
        public IntPtr FilePath;
        public IntPtr FileHandle;
        public IntPtr KnownSubject;
    }

    /// <summary><c>WINTRUST_DATA</c> (unia = wskaźnik na <see cref="WinTrustFileInfo"/>).</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct WinTrustData
    {
        public uint StructSize;
        public IntPtr PolicyCallbackData;
        public IntPtr SipClientData;
        public uint UiChoice;
        public uint RevocationChecks;
        public uint UnionChoice;
        public IntPtr FileInfo;
        public uint StateAction;
        public IntPtr StateData;
        public IntPtr UrlReference;
        public uint ProviderFlags;
        public uint UiContext;
        public IntPtr SignatureSettings;
    }

    /// <summary>Początek <c>CRYPT_PROVIDER_SGNR</c> (do <c>pasCertChain</c>).</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct CryptProviderSigner
    {
        public uint StructSize;
        public uint VerifyAsOfLow;
        public uint VerifyAsOfHigh;
        public uint CertChainCount;
        public IntPtr CertChain;
    }

    /// <summary>Początek <c>CRYPT_PROVIDER_CERT</c> (do <c>pCert</c>).</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct CryptProviderCert
    {
        public uint StructSize;
        public IntPtr Certificate;
    }
}
