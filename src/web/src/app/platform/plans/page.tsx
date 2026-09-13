"use client";

import { useState } from "react";
import { Plus } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Badge } from "@/components/ui/badge";
import { Input } from "@/components/ui/input";
import { ErrorState } from "@/components/states/ErrorState";
import { DataTable, type ColumnDef } from "@/components/data-table/DataTable";
import { usePlatformPlans } from "@/features/platform-plans/hooks/usePlatformPlans";
import { useRetirePlan } from "@/features/platform-plans/hooks/useRetirePlan";
import { usePlanPanelStore } from "@/features/platform-plans/store/planPanelStore";
import { PlanCreateSheet } from "@/features/platform-plans/PlanCreateSheet";
import { useDebouncedValue } from "@/lib/hooks/useDebouncedValue";
import type { Plan } from "@/features/platform-plans/types";

const PAGE_SIZE = 25;

export default function PlatformPlansPage() {
  const [searchInput, setSearchInput] = useState("");
  const [page, setPage] = useState(1);
  const debouncedSearch = useDebouncedValue(searchInput, 300);
  const { data, isLoading, isError, refetch } = usePlatformPlans(debouncedSearch, page, PAGE_SIZE);
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
      <Input
        value={searchInput}
        onChange={(e) => {
          setSearchInput(e.target.value);
          setPage(1);
        }}
        placeholder="Search by name or slug"
        className="max-w-sm"
        aria-label="Search plans"
      />
      <DataTable
        columns={columns}
        data={data}
        isLoading={isLoading}
        emptyTitle="No plans found"
        emptyDescription="Try a different search term, or create a plan to start assigning it to organizations."
        onPageChange={setPage}
        rowKey={(p) => p.id}
      />
      <PlanCreateSheet />
    </div>
  );
}
