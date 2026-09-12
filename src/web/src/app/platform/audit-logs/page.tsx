"use client";

import { useState } from "react";
import { Spinner } from "@/components/ui/spinner";
import { Input } from "@/components/ui/input";
import { DatePicker } from "@/components/ui/date-picker";
import { ErrorState } from "@/components/states/ErrorState";
import { DataTable, type ColumnDef } from "@/components/data-table/DataTable";
import { usePlatformAuditLogs } from "@/features/platform-audit-logs/hooks/usePlatformAuditLogs";
import { usePlatformAuditLogPanelStore } from "@/features/platform-audit-logs/store/platformAuditLogPanelStore";
import { PlatformAuditLogDetailSheet } from "@/features/platform-audit-logs/PlatformAuditLogDetailSheet";
import { useDebouncedValue } from "@/lib/hooks/useDebouncedValue";
import type { AuditLogRecord } from "@/features/platform-audit-logs/types";

const PAGE_SIZE = 25;

export default function PlatformAuditLogsPage() {
  const [organizationSearchInput, setOrganizationSearchInput] = useState("");
  const [searchInput, setSearchInput] = useState("");
  const [entityTypeInput, setEntityTypeInput] = useState("");
  const [dateFrom, setDateFrom] = useState("");
  const [dateTo, setDateTo] = useState("");
  const [page, setPage] = useState(1);
  const debouncedOrganizationSearch = useDebouncedValue(organizationSearchInput, 300);
  const debouncedSearch = useDebouncedValue(searchInput, 300);
  const debouncedEntityType = useDebouncedValue(entityTypeInput, 300);
  const { data, isLoading, isError, refetch } = usePlatformAuditLogs(
    debouncedOrganizationSearch, debouncedSearch, debouncedEntityType, dateFrom, dateTo, page, PAGE_SIZE
  );
  const openDetail = usePlatformAuditLogPanelStore((s) => s.openDetail);

  const columns: ColumnDef<AuditLogRecord>[] = [
    {
      key: "timestamp",
      header: "Date",
      render: (r) => (
        <button type="button" onClick={() => openDetail(r)} className="font-medium text-foreground hover:underline">
          {new Date(r.timestamp).toLocaleString()}
        </button>
      ),
    },
    { key: "organization", header: "Organization", render: (r) => r.organizationName ?? "—" },
    { key: "action", header: "Action", render: (r) => r.action },
    { key: "entity", header: "Entity", render: (r) => `${r.entityType} (${r.entityId})` },
    { key: "actor", header: "Actor", render: (r) => r.userId ?? "System" },
  ];

  if (isError) {
    return (
      <ErrorState
        title="Couldn't load audit logs"
        description="Something went wrong fetching the audit log."
        onRetry={() => refetch()}
      />
    );
  }

  return (
    <div className="space-y-4 p-6">
      <h1 className="font-display text-xl font-semibold">Audit Logs</h1>
      <div className="flex flex-wrap items-center gap-2">
        <Input
          value={organizationSearchInput}
          onChange={(e) => {
            setOrganizationSearchInput(e.target.value);
            setPage(1);
          }}
          placeholder="Search by organization name"
          className="max-w-sm"
          aria-label="Search by organization name"
        />
        <Input
          value={searchInput}
          onChange={(e) => {
            setSearchInput(e.target.value);
            setPage(1);
          }}
          placeholder="Search by action, entity type, or entity ID"
          className="max-w-sm"
          aria-label="Search audit logs"
        />
        <Input
          value={entityTypeInput}
          onChange={(e) => {
            setEntityTypeInput(e.target.value);
            setPage(1);
          }}
          placeholder="Entity type"
          className="max-w-40"
          aria-label="Filter by entity type"
        />
        <DatePicker
          value={dateFrom}
          onChange={(v) => {
            setDateFrom(v);
            setPage(1);
          }}
          placeholder="From date"
          clearable
          className="w-40"
          aria-label="Filter from date"
        />
        <DatePicker
          value={dateTo}
          onChange={(v) => {
            setDateTo(v);
            setPage(1);
          }}
          placeholder="To date"
          clearable
          className="w-40"
          aria-label="Filter to date"
        />
      </div>
      {isLoading ? (
        <div className="flex items-center justify-center p-12">
          <Spinner className="h-6 w-6" />
        </div>
      ) : (
        <DataTable
          columns={columns}
          data={data}
          isLoading={false}
          emptyTitle="No audit events found"
          emptyDescription="Try a different search term, entity type, or date range."
          onPageChange={setPage}
          rowKey={(r) => r.id}
        />
      )}
      <PlatformAuditLogDetailSheet />
    </div>
  );
}
