using Courier.Platform.Application;
using Hangfire;
using Hangfire.Common;
using Hangfire.Storage;

namespace Courier.Platform.Infrastructure;

public sealed class HangfireMonitor : IHangfireMonitor
{
    public JobsSummary GetSummary()
    {
        var stats = JobStorage.Current.GetMonitoringApi().GetStatistics();
        return new JobsSummary(stats.Enqueued, stats.Scheduled, stats.Processing, stats.Succeeded, stats.Failed, stats.Deleted);
    }

    public IReadOnlyList<RecurringJobSummary> GetRecurringJobs()
    {
        using var connection = JobStorage.Current.GetConnection();
        return connection.GetRecurringJobs()
            .Select(r => new RecurringJobSummary(r.Id, r.Cron, r.NextExecution, r.LastExecution, r.LastJobState))
            .OrderBy(r => r.Id, StringComparer.Ordinal)
            .ToList();
    }

    public IReadOnlyList<RecentJobSummary> GetRecentJobs(int failedCount, int succeededCount)
    {
        var monitor = JobStorage.Current.GetMonitoringApi();

        var failed = monitor.FailedJobs(0, failedCount)
            .Select(kv => new RecentJobSummary(kv.Key, JobName(kv.Value.Job), "Failed",
                new DateTimeOffset(kv.Value.FailedAt ?? DateTime.UtcNow, TimeSpan.Zero), kv.Value.ExceptionMessage));

        var succeeded = monitor.SucceededJobs(0, succeededCount)
            .Select(kv => new RecentJobSummary(kv.Key, JobName(kv.Value.Job), "Succeeded",
                new DateTimeOffset(kv.Value.SucceededAt ?? DateTime.UtcNow, TimeSpan.Zero), null));

        return failed.Concat(succeeded).OrderByDescending(j => j.At).ToList();
    }

    // Job.Type can throw JobLoadException when the stored job's assembly-qualified type name can
    // no longer be resolved (e.g. a renamed/removed job class still referenced by old job data)
    // — the Hangfire dashboard itself guards this same access for the same reason.
    private static string JobName(Job? job)
    {
        try
        {
            return job?.Type.Name ?? "Unknown";
        }
        catch (JobLoadException)
        {
            return "Unknown";
        }
    }
}
