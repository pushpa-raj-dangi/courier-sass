"use client";

import { Spinner } from "@/components/ui/spinner";
import { ErrorState } from "@/components/states/ErrorState";
import { usePlatformDashboard } from "@/features/platform-dashboard/hooks/usePlatformDashboard";

function StatTile({ label, value }: { label: string; value: string | number }) {
  return (
    <div className="rounded-lg border bg-card p-4">
      <div className="text-sm text-muted-foreground">{label}</div>
      <div className="mt-1 text-2xl font-semibold">{value}</div>
    </div>
  );
}

export default function PlatformDashboardPage() {
  const { data, isLoading, isError, refetch } = usePlatformDashboard();

  if (isLoading) {
    return (
      <div className="flex items-center justify-center p-12">
        <Spinner className="h-6 w-6" />
      </div>
    );
  }
  if (isError || !data) {
    return (
      <ErrorState
        title="Couldn't load the dashboard"
        description="Something went wrong fetching platform metrics."
        onRetry={() => refetch()}
      />
    );
  }

  return (
    <div className="space-y-4 p-6">
      <h1 className="font-display text-xl font-semibold">Platform Dashboard</h1>
      <div className="grid grid-cols-2 gap-4 sm:grid-cols-3 lg:grid-cols-6">
        <StatTile label="Organizations" value={data.totalOrganizations} />
        <StatTile label="Active" value={data.activeOrganizations} />
        <StatTile label="Suspended" value={data.suspendedOrganizations} />
        <StatTile label="Deleted" value={data.deletedOrganizations} />
        <StatTile label="MRR (NPR)" value={data.monthlyRecurringRevenueNpr} />
        <StatTile label="Overdue Invoices" value={data.overdueInvoiceCount} />
      </div>
    </div>
  );
}
