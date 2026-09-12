"use client";

import { useQuery } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";
import type { PagedResult } from "@/components/data-table/DataTable";
import type { AuditLogRecord } from "@/features/platform-audit-logs/types";

export function usePlatformAuditLogs(
  organizationSearch: string, search: string, entityType: string, dateFrom: string, dateTo: string, page: number, pageSize: number
) {
  const params = new URLSearchParams({ page: String(page), pageSize: String(pageSize) });
  if (organizationSearch) {
    params.set("organizationSearch", organizationSearch);
  }
  if (search) {
    params.set("search", search);
  }
  if (entityType) {
    params.set("entityType", entityType);
  }
  if (dateFrom) {
    // dateFrom/dateTo come from DatePicker as "yyyy-MM-dd" — expanded to the local calendar
    // day's start/end (in the browser's own timezone, since that's what toLocaleString()
    // displays to the user) and converted to the equivalent UTC instant, so the backend's
    // inclusive DateTimeOffset range covers the whole selected day as the user sees it.
    const [y, m, d] = dateFrom.split("-").map(Number);
    params.set("dateFrom", new Date(y, m - 1, d, 0, 0, 0).toISOString());
  }
  if (dateTo) {
    const [y, m, d] = dateTo.split("-").map(Number);
    params.set("dateTo", new Date(y, m - 1, d, 23, 59, 59).toISOString());
  }

  return useQuery({
    queryKey: ["platform-audit-logs", "list", organizationSearch, search, entityType, dateFrom, dateTo, page, pageSize],
    queryFn: () => clientFetch<PagedResult<AuditLogRecord>>(`/api/platform/audit-logs?${params.toString()}`),
  });
}
