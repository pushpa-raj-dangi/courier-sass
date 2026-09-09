"use client";

import { Plus } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Badge } from "@/components/ui/badge";
import { Spinner } from "@/components/ui/spinner";
import { ErrorState } from "@/components/states/ErrorState";
import { DataTable, type ColumnDef } from "@/components/data-table/DataTable";
import { usePlatformPlans } from "@/features/platform-plans/hooks/usePlatformPlans";
import { useRetirePlan } from "@/features/platform-plans/hooks/useRetirePlan";
import { usePlanPanelStore } from "@/features/platform-plans/store/planPanelStore";
import { PlanCreateSheet } from "@/features/platform-plans/PlanCreateSheet";
import type { Plan } from "@/features/platform-plans/types";

export default function PlatformPlansPage() {
  const { data, isLoading, isError, refetch } = usePlatformPlans();
  const openCreate = usePlanPanelStore((s) => s.openCreate);
  const retire = useRetirePlan();

  const columns: ColumnDef<Plan>[] = [
    { key: "name", header: "Name", render: (p) => p.name },
    { key: "price", header: "Monthly Price (NPR)", render: (p) => p.monthlyPriceNpr },
    { key: "maxBranches", header: "Max Branches", render: (p) => p.maxBranches ?? "Unlimited" },
    { key: "default", header: "Default", render: (p) => (p.isDefault ? <Badge>Default</Badge> : null) },
    {
      key: "actions",
      header: "",
      render: (p) =>
        !p.isDefault && p.isActive ? (
          <Button size="sm" variant="ghost" disabled={retire.isPending} onClick={() => retire.mutate(p.id)}>
            Retire
          </Button>
        ) : null,
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
        title="Couldn't load plans"
        description="Something went wrong fetching the plan list."
        onRetry={() => refetch()}
      />
    );
  }

  return (
    <div className="space-y-4 p-6">
      <div className="flex items-center justify-between">
        <h1 className="font-display text-xl font-semibold">Plans</h1>
        <Button size="sm" className="gap-1.5" onClick={() => openCreate()}>
          <Plus className="h-4 w-4" strokeWidth={2} />
          New Plan
        </Button>
      </div>
      <DataTable
        columns={columns}
        data={data && { items: data, page: 1, pageSize: data.length, totalCount: data.length, totalPages: 1 }}
        isLoading={false}
        emptyTitle="No plans yet"
        emptyDescription="Create a plan to start assigning it to organizations."
        onPageChange={() => {}}
        rowKey={(p) => p.id}
      />
      <PlanCreateSheet />
    </div>
  );
}
