using Courier.Identity.Application;
using Xunit;

namespace Courier.Identity.Tests;

public class PermissionCatalogTests
{
    [Fact]
    public void All_IncludesManifestRead()
    {
        Assert.Contains(PermissionCatalog.All, p => p.Key == "manifest.read");
    }
}
