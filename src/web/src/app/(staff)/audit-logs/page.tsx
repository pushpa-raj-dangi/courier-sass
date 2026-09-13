"use client";

import { useState } from "react";
import { Input } from "@/components/ui/input";
import { DatePicker } from "@/components/ui/date-picker";
import { ErrorState } from "@/components/states/ErrorState";
import { DataTable, type ColumnDef } from "@/components/data-table/DataTable";
import { useAuditLogs } from "@/features/audit-logs/hooks/useAuditLogs";
import { useAuditLogPanelStore } from "@/features/audit-logs/store/auditLogPanelStore";
import { AuditLogDetailSheet } from "@/features/audit-logs/AuditLogDetailSheet";
import { useDebouncedValue } from "@/lib/hooks/useDebouncedValue";
import type { AuditLogRecord } from "@/features/audit-logs/types";

const PAGE_SIZE = 25;

export default function AuditLogsPage() {
  const [searchInput, setSearchInput] = useState("");
  const [entityTypeInput, setEntityTypeInput] = useState("");
  const [dateFrom, setDateFrom] = useState("");
  const [dateTo, setDateTo] = useState("");
  const [page, setPage] = useState(1);
  const debouncedSearch = useDebouncedValue(searchInput, 300);
  const debouncedEntityType = useDebouncedValue(entityTypeInput, 300);
  const { data, isLoading, isError, refetch } = useAuditLogs(debouncedSearch, debouncedEntityType, dateFrom, dateTo, page, PAGE_SIZE);
  const openDetail = useAuditLogPanelStore((s) => s.openDetail);

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
      <DataTable
        columns={columns}
        data={data}
        isLoading={isLoading}
        emptyTitle="No audit events found"
        emptyDescription="Try a different search term, entity type, or date range."
        onPageChange={setPage}
        rowKey={(r) => r.id}
      />
      <AuditLogDetailSheet />
    </div>
  );
}
