"use client";

import { Spinner } from "@/components/ui/spinner";
import { ErrorState } from "@/components/states/ErrorState";
import { DataTable, type ColumnDef } from "@/components/data-table/DataTable";
import { Badge } from "@/components/ui/badge";
import { usePlatformOrganizations } from "@/features/platform-organizations/hooks/usePlatformOrganizations";
import { useOrganizationPanelStore } from "@/features/platform-organizations/store/organizationPanelStore";
import { OrganizationDetailSheet } from "@/features/platform-organizations/OrganizationDetailSheet";
import type { OrganizationSummary } from "@/features/platform-organizations/types";

export default function PlatformOrganizationsPage() {
  const { data, isLoading, isError, refetch } = usePlatformOrganizations();
  const openDetail = useOrganizationPanelStore((s) => s.openDetail);

  const columns: ColumnDef<OrganizationSummary>[] = [
    {
      key: "name",
      header: "Organization",
      render: (o) => (
        <button type="button" onClick={() => openDetail(o.id)} className="font-medium text-foreground hover:underline">
          {o.name}
        </button>
      ),
    },
    { key: "plan", header: "Plan", render: (o) => o.planName ?? "—" },
    { key: "branches", header: "Branches", render: (o) => o.branchCount },
    {
      key: "status",
      header: "Status",
      render: (o) => (
        <Badge variant={o.isDeleted ? "destructive" : o.isActive ? "default" : "secondary"}>
          {o.isDeleted ? "Deleted" : o.isActive ? "Active" : "Suspended"}
        </Badge>
      ),
    },
  ];

  if (isLoading) {
    return (
      <div className="flex items-center justify-center p-12">
        <Spinner className="h-6 w-6" />
      </div>
    );
  }

  if (isError) {
    return (
      <ErrorState
        title="Couldn't load organizations"
        description="Something went wrong fetching the organization list."
        onRetry={() => refetch()}
      />
    );
  }

  return (
    <div className="space-y-4 p-6">
      <h1 className="font-display text-xl font-semibold">Organizations</h1>
      <DataTable
        columns={columns}
        data={data && { items: data, page: 1, pageSize: data.length, totalCount: data.length, totalPages: 1 }}
        isLoading={false}
        emptyTitle="No organizations yet"
        emptyDescription="Organizations appear here once they self-register."
        onPageChange={() => {}}
        rowKey={(o) => o.id}
      />
      <OrganizationDetailSheet />
    </div>
  );
}
