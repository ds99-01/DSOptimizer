using System.Globalization;
using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;
using DSOptimizer.Core.Storage;
using DSOptimizer.Tests.Windows.Support;
using DSOptimizer.Windows;
using Xunit;

namespace DSOptimizer.Tests.Windows;

[Trait("Category", "Windows")]
public class ProtectedFileReaderTests
{
    private const long Limit = 1024 * 1024;

    [Fact]
    public void TryRead_ProtectedAdminFile_ReturnsBytesAndFileTime()
    {
        using var temp = new TempDirectory();
        var path = HistoryFiles.WriteJournal(temp.Sub("src"), "PC-01");

        var read = ProtectedFileReader.TryRead(path, Limit);

        Assert.NotNull(read);
        Assert.Null(read!.Rejection);
        Assert.Null(read.Detail);
        Assert.Equal(File.ReadAllBytes(path), read.Bytes);
        Assert.Equal(File.GetLastWriteTimeUtc(path), read.LastWriteUtc);
    }

    [Fact]
    public void TryRead_Missing_ReturnsNull()
    {
        using var temp = new TempDirectory();

        Assert.Null(ProtectedFileReader.TryRead(temp.Sub("journal.json"), Limit));
        Assert.Null(ProtectedFileReader.TryRead(temp.Sub(@"missing\journal.json"), Limit));
    }

    [Fact]
    public void TryRead_ForeignOwner_UntrustedAcl()
    {
        using var temp = new TempDirectory();
        var path = HistoryFiles.WriteJournal(temp.Sub("src"), "PC-01");
        var security = File.GetAccessControl(path);
        security.SetOwner(WindowsIdentity.GetCurrent().User!);
        File.SetAccessControl(path, security);

        AssertRejected(ProtectedFileReader.TryRead(path, Limit), SalvageRejection.UntrustedAcl);
    }

    [Fact]
    public void TryRead_UsersReadAce_UntrustedAcl()
    {
        using var temp = new TempDirectory();
        var path = HistoryFiles.WriteJournal(temp.Sub("src"), "PC-01");
        AddUsersRead(path);

        AssertRejected(ProtectedFileReader.TryRead(path, Limit), SalvageRejection.UntrustedAcl);
    }

    [Fact]
    public void TryRead_UnprotectedAdminOnlyDacl_UntrustedAcl()
    {
        using var temp = new TempDirectory();
        var folder = temp.Sub("legacy");
        LegacyAcl.Create(folder);
        var path = Path.Combine(folder, "journal.json");
        File.WriteAllBytes(path, new byte[] { 0x7B, 0x7D });
        Assert.True(AclTrust.IsTrustedFile(RawDescriptors.OfFile(path)));

        AssertRejected(ProtectedFileReader.TryRead(path, Limit), SalvageRejection.UntrustedAcl);
    }

    [Fact]
    public void TryRead_HardLinked_MultipleLinks()
    {
        using var temp = new TempDirectory();
        var path = HistoryFiles.WriteJournal(temp.Sub("src"), "PC-01");
        FileLinks.HardLink(temp.Sub("second.json"), path);

        AssertRejected(ProtectedFileReader.TryRead(path, Limit), SalvageRejection.MultipleLinks);
    }

    [Fact]
    public void TryRead_SymbolicLink_NotRegularFile()
    {
        using var temp = new TempDirectory();
        var target = HistoryFiles.WriteJournal(temp.Sub("src"), "PC-01");
        Directory.CreateDirectory(temp.Sub("link"));
        var link = temp.Sub(@"link\journal.json");
        FileLinks.FileSymlink(link, target);

        AssertRejected(ProtectedFileReader.TryRead(link, Limit), SalvageRejection.NotRegularFile);
    }

    [Fact]
    public void TryRead_JunctionNamedLikeFile_NotRegularFile()
    {
        using var temp = new TempDirectory();
        Directory.CreateDirectory(temp.Sub("target"));
        var link = temp.Sub("journal.json");
        Junction.Create(link, temp.Sub("target"));

        AssertRejected(ProtectedFileReader.TryRead(link, Limit), SalvageRejection.NotRegularFile);
    }

    [Fact]
    public void TryRead_ParentJunction_NotRegularFile()
    {
        using var temp = new TempDirectory();
        HistoryFiles.WriteJournal(temp.Sub("src"), "PC-01");
        Junction.Create(temp.Sub("link"), temp.Sub("src"));

        AssertRejected(ProtectedFileReader.TryRead(temp.Sub(@"link\journal\journal.json"), Limit), SalvageRejection.NotRegularFile);
    }

    [Fact]
    public void TryRead_OversizedWithUsersAce_UntrustedAclNotTooLarge()
    {
        using var temp = new TempDirectory();
        var path = HistoryFiles.WriteJournal(temp.Sub("src"), "PC-01");
        AddUsersRead(path);

        AssertRejected(ProtectedFileReader.TryRead(path, new FileInfo(path).Length - 1), SalvageRejection.UntrustedAcl);
    }

    [Fact]
    public void TryRead_Directory_NotRegularFile()
    {
        using var temp = new TempDirectory();
        var path = temp.Sub("journal.json");
        Directory.CreateDirectory(path);

        AssertRejected(ProtectedFileReader.TryRead(path, Limit), SalvageRejection.NotRegularFile);
    }

    [Fact]
    public void TryRead_TooLarge_TooLarge()
    {
        using var temp = new TempDirectory();
        var path = HistoryFiles.WriteJournal(temp.Sub("src"), "PC-01");
        var size = new FileInfo(path).Length;

        var read = ProtectedFileReader.TryRead(path, size - 1);

        AssertRejected(read, SalvageRejection.TooLarge);
        Assert.Equal(size.ToString(CultureInfo.InvariantCulture), read!.Detail);
    }

    [Fact]
    public void TryRead_OpenForWriteByOther_OpenFailed()
    {
        using var temp = new TempDirectory();
        var path = HistoryFiles.WriteJournal(temp.Sub("src"), "PC-01");
        using var other = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite);

        var read = ProtectedFileReader.TryRead(path, Limit);

        AssertRejected(read, SalvageRejection.OpenFailed);
        Assert.False(string.IsNullOrWhiteSpace(read!.Detail));
        Assert.True(read.Transient);
    }

    [Fact]
    public void TryRead_UsersReadAce_NotTransient()
    {
        using var temp = new TempDirectory();
        var path = HistoryFiles.WriteJournal(temp.Sub("src"), "PC-01");
        AddUsersRead(path);

        var read = ProtectedFileReader.TryRead(path, Limit);

        AssertRejected(read, SalvageRejection.UntrustedAcl);
        Assert.False(read!.Transient);
    }

    [Fact]
    public void TryRead_OpenForReadByOther_StillRead()
    {
        using var temp = new TempDirectory();
        var path = HistoryFiles.WriteJournal(temp.Sub("src"), "PC-01");
        var expected = File.ReadAllBytes(path);
        using var other = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);

        var read = ProtectedFileReader.TryRead(path, Limit);

        Assert.NotNull(read);
        Assert.Null(read!.Rejection);
        Assert.Equal(expected, read.Bytes);
    }

    internal static void AddUsersRead(string path)
    {
        var security = File.GetAccessControl(path);
        security.AddAccessRule(new FileSystemAccessRule(
            new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null), FileSystemRights.Read, AccessControlType.Allow));
        File.SetAccessControl(path, security);
    }

    private static void AssertRejected(ProtectedFileRead? read, SalvageRejection expected)
    {
        Assert.NotNull(read);
        Assert.Equal(expected, read!.Rejection);
        Assert.Null(read.Bytes);
    }
}
