"use client";

import { ErrorState } from "@/components/states/ErrorState";
import { StatTile } from "@/components/states/StatTile";
import { StatTilesSkeleton } from "@/components/states/StatTilesSkeleton";
import { usePlatformDashboard } from "@/features/platform-dashboard/hooks/usePlatformDashboard";

export default function PlatformDashboardPage() {
  const { data, isLoading, isError, refetch } = usePlatformDashboard();

  if (isLoading) {
    return (
      <div className="space-y-4">
        <h1 className="font-display text-xl font-semibold">Platform Dashboard</h1>
        <StatTilesSkeleton count={6} />
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
    <div className="space-y-4">
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
