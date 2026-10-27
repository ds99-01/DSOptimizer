namespace DSOptimizer.Core.Abstractions;

/// <summary>
/// Sprawdzenie osadzonego podpisu Authenticode pliku (bez katalogów systemu). Podmiot podpisu pochodzi z łańcucha, który
/// zweryfikował system. Brak pliku = <see cref="System.IO.FileNotFoundException"/>.
/// </summary>
public interface ISignatureVerifier
{
    SignatureCheck Verify(string filePath);
}

/// <summary>Werdykt podpisu.</summary>
public enum SignatureStatus
{
    Valid,
    NotSigned,
    Invalid,
}

/// <summary>
/// Wynik sprawdzenia: status, podmiot certyfikatu podpisującego (tylko przy <see cref="SignatureStatus.Valid"/>), szczegół (np. <c>0x80096010</c>).
/// <see cref="SignerSubject"/> = <c>X509Certificate2.Subject</c> (format CryptoAPI X500) certyfikatu liścia z łańcucha, który zweryfikował
/// <c>WinVerifyTrust</c> (dane stanu tej samej weryfikacji), nigdy z osobnego odczytu pliku.
/// </summary>
public sealed record SignatureCheck(SignatureStatus Status, string? SignerSubject, string? Detail);
