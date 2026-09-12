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
    params.set("dateFrom", `${dateFrom}T00:00:00Z`);
  }
  if (dateTo) {
    params.set("dateTo", `${dateTo}T23:59:59Z`);
  }

  return useQuery({
    queryKey: ["platform-audit-logs", "list", organizationSearch, search, entityType, dateFrom, dateTo, page, pageSize],
    queryFn: () => clientFetch<PagedResult<AuditLogRecord>>(`/api/platform/audit-logs?${params.toString()}`),
  });
}
