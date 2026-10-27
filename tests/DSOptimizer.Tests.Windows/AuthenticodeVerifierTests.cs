using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using DSOptimizer.Core.Abstractions;
using DSOptimizer.Core.Install;
using DSOptimizer.Tests.Windows.Support;
using DSOptimizer.Windows;
using Xunit;

namespace DSOptimizer.Tests.Windows;

/// <summary>
/// Podpis Authenticode przez WinVerifyTrust: pliki systemowe tylko czytane, kopie i pliki testowe w <c>%TEMP%\dso-tests\&lt;guid&gt;</c>.
/// Na hoście (2026-10-24, <c>Get-AuthenticodeSignature</c>): <c>notepad.exe</c> = podpis katalogowy, <c>msedge.exe</c> i <c>MRT.exe</c> =
/// osadzony Authenticode Microsoftu.
/// </summary>
[Trait("Category", "Windows")]
public class AuthenticodeVerifierTests
{
    [Fact]
    public void Notepad_CatalogOnly_NotSigned()
    {
        var notepad = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "notepad.exe");

        var check = new AuthenticodeVerifier().Verify(notepad);

        Assert.Equal(SignatureStatus.NotSigned, check.Status);
        Assert.Null(check.SignerSubject);
    }

    [Fact]
    public void EmbeddedMicrosoft_ValidButNotPurslane()
    {
        var file = EmbeddedSignedSystemFile();

        var check = new AuthenticodeVerifier().Verify(file);

        Assert.Equal(SignatureStatus.Valid, check.Status);
        Assert.Contains("O=Microsoft Corporation", check.SignerSubject, StringComparison.Ordinal);
        Assert.False(RustDeskSource.IsTrustedSigner(check.SignerSubject));

        // Niezależny odczyt certyfikatu podpisującego z pliku (tylko porównanie w teście): podmiot liścia, nie urzędu.
        using var signer = new X509Certificate2(X509Certificate.CreateFromSignedFile(file));
        Assert.Equal(signer.Subject, check.SignerSubject);
    }

    [Fact]
    public void UnsignedTempFile_NotValid()
    {
        using var temp = new TempDirectory();
        var path = temp.Sub("plain.msi");
        var bytes = new byte[64 * 1024];
        new Random(7).NextBytes(bytes);
        File.WriteAllBytes(path, bytes);

        var check = new AuthenticodeVerifier().Verify(path);

        Assert.NotEqual(SignatureStatus.Valid, check.Status);
        Assert.Null(check.SignerSubject);
    }

    [Fact]
    public void TamperedCopy_Invalid()
    {
        using var temp = new TempDirectory();
        var copy = temp.Sub("tampered.exe");
        File.Copy(EmbeddedSignedSystemFile(), copy);
        using (var stream = new FileStream(copy, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            stream.Position = stream.Length / 2;
            var original = stream.ReadByte();
            stream.Position = stream.Length / 2;
            stream.WriteByte(unchecked((byte)(original ^ 0xFF)));
        }

        var check = new AuthenticodeVerifier().Verify(copy);

        Assert.Equal(new SignatureCheck(SignatureStatus.Invalid, null, "0x80096010"), check);
    }

    [Fact]
    public void IntactCopy_Valid()
    {
        using var temp = new TempDirectory();
        var copy = temp.Sub("copy.exe");
        File.Copy(EmbeddedSignedSystemFile(), copy);

        var check = new AuthenticodeVerifier().Verify(copy);

        Assert.Equal(SignatureStatus.Valid, check.Status);
        Assert.Null(check.Detail);
    }

    [Fact]
    public void MissingFile_Throws()
    {
        using var temp = new TempDirectory();

        Assert.Throws<FileNotFoundException>(() => new AuthenticodeVerifier().Verify(temp.Sub("brak.msi")));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void EmptyPath_ArgumentException(string path)
        => Assert.Throws<ArgumentException>(() => new AuthenticodeVerifier().Verify(path));

    /// <summary>
    /// Plik z osadzonym podpisem Microsoftu: pierwszy istniejący z listy (deterministycznie). <c>notepad.exe</c> się nie nadaje –
    /// na hoście ma tylko podpis katalogowy.
    /// </summary>
    private static string EmbeddedSignedSystemFile()
    {
        var candidates = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), @"Microsoft\Edge\Application\msedge.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), @"System32\MRT.exe"),
        };
        var found = candidates.FirstOrDefault(File.Exists);
        if (found is null)
        {
            Assert.Fail("Brak pliku z osadzonym podpisem Microsoftu do testu: " + string.Join(", ", candidates));
        }

        return found!;
    }
}
