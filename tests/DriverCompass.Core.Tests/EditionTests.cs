using DriverCompass.Core.Installation;
using DriverCompass.Core.Models;

namespace DriverCompass.Core.Tests;

public class EditionTests
{
    [Fact]
    public void Tests_run_against_the_standard_edition()
    {
        // The Store edition is only built with -p:DriverCompassEdition=Store; tests always use the standard one.
        Assert.Equal("Standard", AppInfo.Edition);
        Assert.False(AppInfo.IsStoreEdition);
    }

    [Fact]
    public void Store_edition_install_log_is_empty()
    {
        Assert.Empty(new NoInstallation().ReadAll());
    }

    [Fact]
    public async Task Store_edition_never_installs_anything()
    {
        var request = new InstallRequest { RequestId = Guid.NewGuid(), Action = InstallAction.Install };

        await Assert.ThrowsAsync<InvalidOperationException>(() => new NoInstallation().RunAsync(request, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Store_edition_never_reports_an_update()
    {
        Assert.Null(await new NoAppUpdateChecker().CheckAsync("0.0.1", TestContext.Current.CancellationToken));
    }
}
