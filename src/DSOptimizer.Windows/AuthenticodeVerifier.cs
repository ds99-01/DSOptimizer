using System;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;
using DSOptimizer.Core.Abstractions;
using DSOptimizer.Core.Localization;
using DSOptimizer.Windows.Native;

namespace DSOptimizer.Windows;

/// <summary>
/// Osadzony podpis Authenticode pliku przez <c>WinVerifyTrust</c> (<c>WINTRUST_ACTION_GENERIC_VERIFY_V2</c>, <c>WTD_CHOICE_FILE</c>,
/// bez UI, <c>WTD_REVOKE_NONE</c> – bez sprawdzania odwołań poza domyślnym zachowaniem Windows; bez katalogów systemu, więc plik
/// podpisany tylko katalogiem, jak <c>notepad.exe</c>, to <see cref="SignatureStatus.NotSigned"/>). Wynik 0 = <see cref="SignatureStatus.Valid"/>
/// z podmiotem certyfikatu podpisującego; <c>TRUST_E_NOSIGNATURE</c>, <c>TRUST_E_SUBJECT_FORM_UNKNOWN</c>, <c>TRUST_E_PROVIDER_UNKNOWN</c> =
/// <see cref="SignatureStatus.NotSigned"/>; każdy inny kod = <see cref="SignatureStatus.Invalid"/> ze szczegółem <c>0x%08X</c>
/// (np. <c>0x80096010</c> = zmieniony plik). Stan weryfikacji jest zawsze zamykany (<c>WTD_STATEACTION_CLOSE</c>).
/// </summary>
public sealed class AuthenticodeVerifier : ISignatureVerifier
{
    private static readonly IntPtr NoUiWindow = new(-1);

    public SignatureCheck Verify(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            throw new ArgumentException("Pusta ścieżka pliku do sprawdzenia podpisu.", nameof(filePath));
        }

        var full = Path.GetFullPath(filePath);
        if (!File.Exists(full))
        {
            throw new FileNotFoundException(L.T("Nie ma pliku do sprawdzenia podpisu."), full);
        }

        var path = Marshal.StringToHGlobalUni(full);
        var fileInfo = IntPtr.Zero;
        try
        {
            fileInfo = Marshal.AllocHGlobal(Marshal.SizeOf<NativeMethods.WinTrustFileInfo>());
            Marshal.StructureToPtr(
                new NativeMethods.WinTrustFileInfo
                {
                    StructSize = (uint)Marshal.SizeOf<NativeMethods.WinTrustFileInfo>(),
                    FilePath = path,
                },
                fileInfo,
                fDeleteOld: false);

            var data = new NativeMethods.WinTrustData
            {
                StructSize = (uint)Marshal.SizeOf<NativeMethods.WinTrustData>(),
                UiChoice = NativeMethods.WtdUiNone,
                RevocationChecks = NativeMethods.WtdRevokeNone,
                UnionChoice = NativeMethods.WtdChoiceFile,
                FileInfo = fileInfo,
                StateAction = NativeMethods.WtdStateActionVerify,
            };
            var action = NativeMethods.WintrustActionGenericVerifyV2;
            var result = NativeMethods.WinVerifyTrust(NoUiWindow, ref action, ref data);
            try
            {
                return result switch
                {
                    0 => Signed(data.StateData),
                    NativeMethods.TrustENoSignature
                        or NativeMethods.TrustESubjectFormUnknown
                        or NativeMethods.TrustEProviderUnknown => new SignatureCheck(SignatureStatus.NotSigned, null, Hex(result)),
                    _ => new SignatureCheck(SignatureStatus.Invalid, null, Hex(result)),
                };
            }
            finally
            {
                // Zamknięcie stanu zwalnia dane weryfikacji; jego kod nie zmienia werdyktu, który już zapadł.
                data.StateAction = NativeMethods.WtdStateActionClose;
                _ = NativeMethods.WinVerifyTrust(NoUiWindow, ref action, ref data);
            }
        }
        finally
        {
            if (fileInfo != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(fileInfo);
            }

            Marshal.FreeHGlobal(path);
        }
    }

    /// <summary>
    /// Podmiot podpisu do reguły wydawcy (<c>RustDeskSource.IsTrustedSigner</c>) pochodzi wyłącznie z łańcucha, który przed chwilą
    /// zweryfikował <c>WinVerifyTrust</c>: dane stanu tej weryfikacji → pierwszy podpisujący → <c>pasCertChain[0]</c> (certyfikat liścia) →
    /// <see cref="X509Certificate2.Subject"/> (format CryptoAPI X500, wartości ze znakami specjalnymi w cudzysłowie – ten format zakłada
    /// parser reguły). Nigdy osobny odczyt pliku (inny plik albo inny podpis niż zweryfikowany). Brak łańcucha mimo wyniku 0 =
    /// <see cref="SignatureStatus.Invalid"/>. Wołane przed <c>WTD_STATEACTION_CLOSE</c>; <see cref="X509Certificate2"/> duplikuje kontekst.
    /// </summary>
    private static SignatureCheck Signed(IntPtr stateData)
    {
        var providerData = NativeMethods.WTHelperProvDataFromStateData(stateData);
        var signer = providerData == IntPtr.Zero
            ? IntPtr.Zero
            : NativeMethods.WTHelperGetProvSignerFromChain(providerData, 0, counterSigner: false, 0);
        if (signer == IntPtr.Zero)
        {
            return new SignatureCheck(SignatureStatus.Invalid, null, L.T("brak danych podpisującego"));
        }

        var signerData = Marshal.PtrToStructure<NativeMethods.CryptProviderSigner>(signer);
        if (signerData.CertChainCount == 0 || signerData.CertChain == IntPtr.Zero)
        {
            return new SignatureCheck(SignatureStatus.Invalid, null, L.T("brak łańcucha certyfikatów"));
        }

        var leaf = Marshal.PtrToStructure<NativeMethods.CryptProviderCert>(signerData.CertChain);
        if (leaf.Certificate == IntPtr.Zero)
        {
            return new SignatureCheck(SignatureStatus.Invalid, null, L.T("brak certyfikatu podpisującego"));
        }

        using var certificate = new X509Certificate2(leaf.Certificate);
        return new SignatureCheck(SignatureStatus.Valid, certificate.Subject, null);
    }

    private static string Hex(int result) => "0x" + unchecked((uint)result).ToString("X8", CultureInfo.InvariantCulture);
}
