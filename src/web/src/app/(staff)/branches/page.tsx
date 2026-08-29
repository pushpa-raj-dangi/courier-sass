"use client";

import { Plus } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Spinner } from "@/components/ui/spinner";
import { ErrorState } from "@/components/states/ErrorState";
import { DataTable, type ColumnDef } from "@/components/data-table/DataTable";
import { usePermission } from "@/features/auth/hooks/usePermission";
import { useBranches } from "@/features/branches/hooks/useBranches";
import { useBranchPanelStore } from "@/features/branches/store/branchPanelStore";
import type { Branch } from "@/features/branches/types";

export default function BranchesPage() {
  const { data, isLoading, isError, refetch } = useBranches();
  const canCreate = usePermission("branch.create");
  const openCreate = useBranchPanelStore((s) => s.openCreate);
  const openDetail = useBranchPanelStore((s) => s.openDetail);

  const columns: ColumnDef<Branch>[] = [
    { key: "code", header: "Code", render: (b) => b.code },
    {
      key: "name",
      header: "Name",
      render: (b) => (
        <button
          type="button"
          onClick={() => openDetail(b.id)}
          className="font-medium text-foreground hover:underline"
        >
          {b.name}
        </button>
      ),
    },
    { key: "type", header: "Type", render: (b) => b.type },
    { key: "active", header: "Active", render: (b) => (b.isActive ? "Yes" : "No") },
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
        title="Couldn't load branches"
        description="Something went wrong fetching the branch list."
        onRetry={() => refetch()}
      />
    );
  }

  return (
    <div className="space-y-4">
      <div className="flex items-center justify-between">
        <h1 className="font-display text-xl font-semibold">Branches</h1>
        {canCreate && (
          <Button size="sm" className="gap-1.5" onClick={() => openCreate()}>
            <Plus className="h-4 w-4" strokeWidth={2} />
            New Branch
          </Button>
        )}
      </div>
      <DataTable
        columns={columns}
        data={
          data && {
            items: data,
            page: 1,
            pageSize: data.length,
            totalCount: data.length,
            totalPages: 1,
          }
        }
        isLoading={false}
        emptyTitle="No branches yet"
        emptyDescription="Create your organization's first branch to get started."
        onPageChange={() => {}}
        rowKey={(b) => b.id}
      />
    </div>
  );
}
