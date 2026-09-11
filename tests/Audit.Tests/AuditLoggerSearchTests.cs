using Courier.Audit.Application;
using Courier.Audit.Domain;
using Courier.Audit.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Courier.Audit.Tests;

file class TestDbContext : DbContext
{
    public TestDbContext(DbContextOptions<TestDbContext> options) : base(options) { }
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new AuditLogConfiguration());
    }
}

file sealed class NullHttpContextAccessor : IHttpContextAccessor
{
    public HttpContext? HttpContext { get => null; set { } }
}

public class AuditLoggerSearchTests
{
    private static readonly AuditLogSearchFilter NoFilter = new(null, null, null, null, null, null, null);

    // Returns only AuditLogger (never the file-scoped TestDbContext) — a private helper's return
    // type is still part of this non-file-scoped class's member signatures, so a file-local type
    // cannot appear there (CS9051). Tests that need to reach into the DbContext directly
    // construct it inline instead, matching AuditLoggerTests.cs's existing convention.
    private static AuditLogger CreateLogger()
    {
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AuditLogger(new TestDbContext(options), new NullHttpContextAccessor());
    }

    [Fact]
    public async Task SearchAsync_ScopesToTheGivenOrganizationOnly()
    {
        var logger = CreateLogger();
        var orgA = Guid.NewGuid();
        var orgB = Guid.NewGuid();
        await logger.LogAsync(new AuditEntry(orgA, null, "shipment.created", "Shipment", "1"));
        await logger.LogAsync(new AuditEntry(orgB, null, "shipment.created", "Shipment", "2"));

        var result = await logger.SearchAsync(orgA, NoFilter, 1, 25);

        Assert.Equal(1, result.TotalCount);
        Assert.All(result.Items, r => Assert.Equal(orgA, r.OrganizationId));
    }

    [Fact]
    public async Task SearchAsync_SearchTerm_MatchesActionEntityTypeOrEntityIdCaseInsensitively()
    {
        var logger = CreateLogger();
        var orgId = Guid.NewGuid();
        await logger.LogAsync(new AuditEntry(orgId, null, "shipment.created", "Shipment", "AWB-1"));
        await logger.LogAsync(new AuditEntry(orgId, null, "cod.collected", "CodCollection", "COD-1"));

        var result = await logger.SearchAsync(orgId, NoFilter with { Search = "awb" }, 1, 25);

        Assert.Single(result.Items);
        Assert.Equal("shipment.created", result.Items[0].Action);
    }

    [Fact]
    public async Task SearchAsync_EntityTypeFilter_MatchesExactlyNotSubstring()
    {
        var logger = CreateLogger();
        var orgId = Guid.NewGuid();
        await logger.LogAsync(new AuditEntry(orgId, null, "shipment.created", "Shipment", "1"));
        await logger.LogAsync(new AuditEntry(orgId, null, "return_shipment.confirmed", "ReturnShipment", "2"));

        var result = await logger.SearchAsync(orgId, NoFilter with { EntityType = "Shipment" }, 1, 25);

        Assert.Single(result.Items);
        Assert.Equal("Shipment", result.Items[0].EntityType);
    }

    [Fact]
    public async Task SearchAsync_DateRangeFilter_ExcludesEventsOutsideRange()
    {
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var dbContext = new TestDbContext(options);
        var logger = new AuditLogger(dbContext, new NullHttpContextAccessor());
        var orgId = Guid.NewGuid();
        await logger.LogAsync(new AuditEntry(orgId, null, "shipment.created", "Shipment", "1"));
        var row = await dbContext.Set<AuditLog>().SingleAsync();
        row.Timestamp = DateTimeOffset.UtcNow.AddDays(-10);
        await dbContext.SaveChangesAsync();

        var result = await logger.SearchAsync(orgId, NoFilter with { DateFrom = DateTimeOffset.UtcNow.AddDays(-1) }, 1, 25);

        Assert.Empty(result.Items);
    }

    [Fact]
    public async Task SearchAsync_SortsNewestFirstAndPaginates()
    {
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var dbContext = new TestDbContext(options);
        var logger = new AuditLogger(dbContext, new NullHttpContextAccessor());
        var orgId = Guid.NewGuid();
        for (var i = 0; i < 3; i++)
        {
            await logger.LogAsync(new AuditEntry(orgId, null, $"action.{i}", "Shipment", i.ToString()));
        }
        // Explicit, strictly increasing timestamps — LogAsync's own DateTimeOffset.UtcNow calls
        // can land in the same tick on a fast test machine, which would make "newest first"
        // ambiguous between action.0/1/2.
        var rows = await dbContext.Set<AuditLog>().OrderBy(a => a.Action).ToListAsync();
        for (var i = 0; i < rows.Count; i++)
        {
            rows[i].Timestamp = DateTimeOffset.UtcNow.AddMinutes(-10 + i);
        }
        await dbContext.SaveChangesAsync();

        var page1 = await logger.SearchAsync(orgId, NoFilter, 1, 2);
        var page2 = await logger.SearchAsync(orgId, NoFilter, 2, 2);

        Assert.Equal(3, page1.TotalCount);
        Assert.Equal(2, page1.Items.Count);
        Assert.Single(page2.Items);
        Assert.Equal("action.2", page1.Items[0].Action);
        Assert.Equal("action.1", page1.Items[1].Action);
        Assert.Equal("action.0", page2.Items[0].Action);
    }

    [Fact]
    public async Task SearchAcrossOrganizationsAsync_NullOrganizationIds_ReturnsEveryOrganizationsEvents()
    {
        var logger = CreateLogger();
        var orgA = Guid.NewGuid();
        var orgB = Guid.NewGuid();
        await logger.LogAsync(new AuditEntry(orgA, null, "shipment.created", "Shipment", "1"));
        await logger.LogAsync(new AuditEntry(orgB, null, "shipment.created", "Shipment", "2"));

        var result = await logger.SearchAcrossOrganizationsAsync(null, NoFilter, 1, 25);

        Assert.Equal(2, result.TotalCount);
    }

    [Fact]
    public async Task SearchAcrossOrganizationsAsync_WithOrganizationIdSet_RestrictsToThoseOrganizationsOnly()
    {
        var logger = CreateLogger();
        var orgA = Guid.NewGuid();
        var orgB = Guid.NewGuid();
        await logger.LogAsync(new AuditEntry(orgA, null, "shipment.created", "Shipment", "1"));
        await logger.LogAsync(new AuditEntry(orgB, null, "shipment.created", "Shipment", "2"));

        var result = await logger.SearchAcrossOrganizationsAsync(new HashSet<Guid> { orgA }, NoFilter, 1, 25);

        Assert.Single(result.Items);
        Assert.Equal(orgA, result.Items[0].OrganizationId);
    }

    [Fact]
    public async Task SearchAcrossOrganizationsAsync_WithEmptyOrganizationIdSet_ReturnsNothing()
    {
        var logger = CreateLogger();
        var orgId = Guid.NewGuid();
        await logger.LogAsync(new AuditEntry(orgId, null, "shipment.created", "Shipment", "1"));

        var result = await logger.SearchAcrossOrganizationsAsync(new HashSet<Guid>(), NoFilter, 1, 25);

        Assert.Empty(result.Items);
    }
}
