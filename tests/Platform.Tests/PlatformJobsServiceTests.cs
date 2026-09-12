using Courier.Platform.Application;
using Xunit;

namespace Courier.Platform.Tests;

public class PlatformJobsServiceTests
{
    private sealed class FakeHangfireMonitor : IHangfireMonitor
    {
        public JobsSummary Summary { get; set; } = new(0, 0, 0, 0, 0, 0);
        public List<RecurringJobSummary> RecurringJobs { get; set; } = new();
        public List<RecentJobSummary> RecentJobs { get; set; } = new();
        public (int FailedCount, int SucceededCount)? LastRecentJobsCall { get; private set; }

        public JobsSummary GetSummary() => Summary;
        public IReadOnlyList<RecurringJobSummary> GetRecurringJobs() => RecurringJobs;
        public IReadOnlyList<RecentJobSummary> GetRecentJobs(int failedCount, int succeededCount)
        {
            LastRecentJobsCall = (failedCount, succeededCount);
            return RecentJobs;
        }
    }

    [Fact]
    public async Task GetOverviewAsync_AssemblesSummaryRecurringAndRecentJobsFromTheMonitor()
    {
        var monitor = new FakeHangfireMonitor
        {
            Summary = new JobsSummary(Enqueued: 3, Scheduled: 1, Processing: 0, Succeeded: 100, Failed: 2, Deleted: 5),
            RecurringJobs = new List<RecurringJobSummary>
            {
                new("outbox-publisher", "*/10 * * * * *", DateTime.UtcNow.AddSeconds(5), DateTime.UtcNow.AddSeconds(-5), "Succeeded"),
            },
            RecentJobs = new List<RecentJobSummary>
            {
                new("job-1", "OutboxPublisherJob", "Succeeded", DateTimeOffset.UtcNow, null),
            },
        };
        var service = new PlatformJobsService(monitor);

        var overview = await service.GetOverviewAsync();

        Assert.Equal(monitor.Summary, overview.Summary);
        Assert.Equal(monitor.RecurringJobs, overview.RecurringJobs);
        Assert.Equal(monitor.RecentJobs, overview.RecentJobs);
    }

    [Fact]
    public async Task GetOverviewAsync_RequestsTop20FailedAndTop20SucceededRecentJobs()
    {
        var monitor = new FakeHangfireMonitor();
        var service = new PlatformJobsService(monitor);

        await service.GetOverviewAsync();

        Assert.Equal((20, 20), monitor.LastRecentJobsCall);
    }
}
