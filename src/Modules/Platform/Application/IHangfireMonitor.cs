namespace Courier.Platform.Application;

public interface IHangfireMonitor
{
    JobsSummary GetSummary();
    IReadOnlyList<RecurringJobSummary> GetRecurringJobs();
    IReadOnlyList<RecentJobSummary> GetRecentJobs(int failedCount, int succeededCount);
}
