using DriverCompass.VersionProviders.Lenovo;
using DriverCompass.VersionProviders.Packages;

namespace DriverCompass.VersionProviders.Tests;

public class LenovoPackageTests
{
    private const string StandardCommand = "n32li04w.exe /VERYSILENT /DIR=%PACKAGEPATH% /EXTRACT=\"YES\"";

    private static LenovoCatalogParser.LenovoDescriptor Read(string xml)
    {
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(xml));
        return LenovoCatalogParser.ReadDescriptor(stream);
    }

    private static string Fixture => File.ReadAllText(Fixtures.PathOf("lenovo-installable.xml"));

    [Fact]
    public void StandardDescriptor_IsInstallable_AndBecomesAPackage()
    {
        var descriptor = Read(Fixture);

        Assert.True(descriptor.IsInstallable);
        Assert.Equal("n32li04w", Assert.Single(descriptor.Entries).PackageId);

        var package = LenovoCatalogParser.ToPackage(descriptor, new Uri("https://download.lenovo.com/pccbbs/mobiles/n32li04w_2_.xml"));
        Assert.NotNull(package);
        Assert.Equal("https://download.lenovo.com/pccbbs/mobiles/n32li04w.exe", package.DownloadUri.AbsoluteUri);
        Assert.Equal("217E9072A1E3253C514C6EAEF679A2007CE26CF035E9611F6F185708B830C2C1", package.Sha256);
        Assert.Equal(PackageExtraction.LenovoInno, package.Extraction);
        Assert.Equal("n32li04w.exe", package.InstallerFileName);
        Assert.Equal("30.100.2129.8", package.Version);
        Assert.Equal(["Lenovo"], package.Signers);
    }

    [Theory]
    [InlineData(StandardCommand + " &amp; calc.exe")]
    [InlineData("setup.exe /s /e")]
    [InlineData("other.exe /VERYSILENT /DIR=%PACKAGEPATH% /EXTRACT=\"YES\"")]
    [InlineData("n32li04w.exe /VERYSILENT /DIR=C:\\Windows /EXTRACT=\"YES\"")]
    [InlineData("cmd.exe /c " + StandardCommand)]
    public void OtherExtractCommands_AreNotInstallable(string command)
    {
        var descriptor = Read(Fixture.Replace(StandardCommand, command, StringComparison.Ordinal));

        Assert.False(descriptor.IsInstallable);
        Assert.Null(Assert.Single(descriptor.Entries).PackageId);
        Assert.Null(LenovoCatalogParser.ToPackage(descriptor, new Uri("https://download.lenovo.com/pccbbs/mobiles/x.xml")));
    }

    [Fact]
    public void BrokenHash_OrForeignDescriptorHost_AreNotInstallable()
    {
        Assert.False(Read(Fixture.Replace("217E9072A1E3253C514C6EAEF679A2007CE26CF035E9611F6F185708B830C2C1", "1234", StringComparison.Ordinal)).IsInstallable);
        Assert.Null(LenovoCatalogParser.ToPackage(Read(Fixture), new Uri("https://evil.example.com/pccbbs/x.xml")));
    }
}
