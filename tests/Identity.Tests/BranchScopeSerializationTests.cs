using System.Text.Json;
using Courier.Identity.Domain;
using Xunit;

namespace Courier.Identity.Tests;

public class BranchScopeSerializationTests
{
    [Fact]
    public void BranchScope_SerializesAsString() =>
        Assert.Equal("\"SpecificBranches\"", JsonSerializer.Serialize(BranchScope.SpecificBranches));
}
