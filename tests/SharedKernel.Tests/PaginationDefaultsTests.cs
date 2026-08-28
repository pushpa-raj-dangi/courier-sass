using Courier.SharedKernel;

namespace Courier.SharedKernel.Tests;

public class PaginationDefaultsTests
{
    [Fact]
    public void Normalize_NullPageAndPageSize_UsesDefaults()
    {
        var (page, pageSize) = PaginationDefaults.Normalize(null, null);

        Assert.Equal(1, page);
        Assert.Equal(PaginationDefaults.DefaultPageSize, pageSize);
    }

    [Fact]
    public void Normalize_PageBelowOne_ClampsToOne()
    {
        var (page, _) = PaginationDefaults.Normalize(0, 10);

        Assert.Equal(1, page);
    }

    [Fact]
    public void Normalize_PageSizeAboveMax_ClampsToMax()
    {
        var (_, pageSize) = PaginationDefaults.Normalize(1, 10_000);

        Assert.Equal(PaginationDefaults.MaxPageSize, pageSize);
    }

    [Fact]
    public void Normalize_PageSizeBelowOne_ClampsToOne()
    {
        var (_, pageSize) = PaginationDefaults.Normalize(1, 0);

        Assert.Equal(1, pageSize);
    }

    [Fact]
    public void Normalize_ValidValues_PassThroughUnchanged()
    {
        var (page, pageSize) = PaginationDefaults.Normalize(3, 50);

        Assert.Equal(3, page);
        Assert.Equal(50, pageSize);
    }
}

public class PagedResultTests
{
    [Fact]
    public void TotalPages_ZeroTotalCount_IsZero()
    {
        var result = new PagedResult<int>(Array.Empty<int>(), 1, 25, 0);

        Assert.Equal(0, result.TotalPages);
    }

    [Fact]
    public void TotalPages_ExactMultiple_DoesNotRoundUp()
    {
        var result = new PagedResult<int>(Array.Empty<int>(), 1, 25, 50);

        Assert.Equal(2, result.TotalPages);
    }

    [Fact]
    public void TotalPages_WithRemainder_RoundsUp()
    {
        var result = new PagedResult<int>(Array.Empty<int>(), 1, 25, 51);

        Assert.Equal(3, result.TotalPages);
    }
}
