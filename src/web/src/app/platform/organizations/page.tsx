"use client";

import { useState } from "react";
import { Input } from "@/components/ui/input";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { ErrorState } from "@/components/states/ErrorState";
import { DataTable, type ColumnDef } from "@/components/data-table/DataTable";
import { Badge } from "@/components/ui/badge";
import { usePlatformOrganizations, type OrganizationStatusFilter } from "@/features/platform-organizations/hooks/usePlatformOrganizations";
import { useOrganizationPanelStore } from "@/features/platform-organizations/store/organizationPanelStore";
import { OrganizationDetailSheet } from "@/features/platform-organizations/OrganizationDetailSheet";
import { useDebouncedValue } from "@/lib/hooks/useDebouncedValue";
import type { OrganizationSummary } from "@/features/platform-organizations/types";

const PAGE_SIZE = 25;

export default function PlatformOrganizationsPage() {
  const [searchInput, setSearchInput] = useState("");
  const [status, setStatus] = useState<OrganizationStatusFilter>("all");
  const [page, setPage] = useState(1);
  const debouncedSearch = useDebouncedValue(searchInput, 300);
  const { data, isLoading, isError, refetch } = usePlatformOrganizations(debouncedSearch, status, page, PAGE_SIZE);
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
    <div className="space-y-4">
      <h1 className="font-display text-xl font-semibold">Organizations</h1>
      <div className="flex flex-wrap items-center gap-2">
        <Input
          value={searchInput}
          onChange={(e) => {
            setSearchInput(e.target.value);
            setPage(1);
          }}
          placeholder="Search by name, slug, or owner email"
          className="max-w-sm"
          aria-label="Search organizations"
        />
        <Select
          value={status}
          onValueChange={(v) => {
            setStatus(v as OrganizationStatusFilter);
            setPage(1);
          }}
        >
          <SelectTrigger className="w-48" aria-label="Filter by status">
            <SelectValue />
          </SelectTrigger>
          <SelectContent>
            <SelectItem value="all">All statuses</SelectItem>
            <SelectItem value="active">Active</SelectItem>
            <SelectItem value="suspended">Suspended</SelectItem>
            <SelectItem value="deleted">Deleted</SelectItem>
          </SelectContent>
        </Select>
      </div>
      <DataTable
        columns={columns}
        data={data}
        isLoading={isLoading}
        emptyTitle="No organizations found"
        emptyDescription="Try a different search term or status filter."
        onPageChange={setPage}
        rowKey={(o) => o.id}
      />
      <OrganizationDetailSheet />
    </div>
  );
}
