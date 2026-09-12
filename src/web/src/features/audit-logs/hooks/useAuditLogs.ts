"use client";

import { useQuery } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";
import type { PagedResult } from "@/components/data-table/DataTable";
import type { AuditLogRecord } from "@/features/audit-logs/types";

export function useAuditLogs(search: string, entityType: string, dateFrom: string, dateTo: string, page: number, pageSize: number) {
  const params = new URLSearchParams({ page: String(page), pageSize: String(pageSize) });
  if (search) {
    params.set("search", search);
  }
  if (entityType) {
    params.set("entityType", entityType);
  }
  if (dateFrom) {
    // dateFrom/dateTo come from DatePicker as "yyyy-MM-dd" — expanded to UTC day boundaries so
    // the backend's inclusive DateTimeOffset range covers the whole selected day rather than
    // excluding most of the end date.
    params.set("dateFrom", `${dateFrom}T00:00:00Z`);
  }
  if (dateTo) {
    params.set("dateTo", `${dateTo}T23:59:59Z`);
  }

  return useQuery({
    queryKey: ["audit-logs", "list", search, entityType, dateFrom, dateTo, page, pageSize],
    queryFn: () => clientFetch<PagedResult<AuditLogRecord>>(`/api/audit-logs?${params.toString()}`),
  });
}
