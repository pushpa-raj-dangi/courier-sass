export interface JobsSummary {
  enqueued: number;
  scheduled: number;
  processing: number;
  succeeded: number;
  failed: number;
  deleted: number;
}

export interface RecurringJobSummary {
  id: string;
  cron: string | null;
  nextExecution: string | null;
  lastExecution: string | null;
  lastJobState: string | null;
}

export interface RecentJobSummary {
  jobId: string;
  jobName: string;
  state: string;
  at: string;
  exceptionMessage: string | null;
}

export interface JobsOverview {
  summary: JobsSummary;
  recurringJobs: RecurringJobSummary[];
  recentJobs: RecentJobSummary[];
}
