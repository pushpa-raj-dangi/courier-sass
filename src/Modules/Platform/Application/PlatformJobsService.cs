namespace Courier.Platform.Application;

public sealed record JobsSummary(long Enqueued, long Scheduled, long Processing, long Succeeded, long Failed, long Deleted);

public sealed record RecurringJobSummary(string Id, string? Cron, DateTime? NextExecution, DateTime? LastExecution, string? LastJobState);

public sealed record RecentJobSummary(string JobId, string JobName, string State, DateTimeOffset At, string? ExceptionMessage);

public sealed record JobsOverview(JobsSummary Summary, IReadOnlyList<RecurringJobSummary> RecurringJobs, IReadOnlyList<RecentJobSummary> RecentJobs);

public sealed class PlatformJobsService
{
    private readonly IHangfireMonitor _monitor;

    public PlatformJobsService(IHangfireMonitor monitor)
    {
        _monitor = monitor;
    }

    public Task<JobsOverview> GetOverviewAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(new JobsOverview(_monitor.GetSummary(), _monitor.GetRecurringJobs(), _monitor.GetRecentJobs(20, 20)));
}
