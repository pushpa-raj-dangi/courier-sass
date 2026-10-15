"use client";

import { useQuery } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";
import type { PagedResult } from "@/components/data-table/DataTable";
import type { Ndr, NdrAssignedFilter, NdrReason, NdrStatus } from "@/features/ndr/types";

export function useNdrs(
  params: { status?: NdrStatus; shipmentId?: string; assigned?: NdrAssignedFilter; reason?: NdrReason; page?: number; pageSize?: number } = {},
) {
  const { status, shipmentId, assigned, reason, page, pageSize } = params;
  const query = new URLSearchParams();
  if (status) query.set("status", status);
  if (shipmentId) query.set("shipmentId", shipmentId);
  if (assigned) query.set("assigned", assigned);
  if (reason) query.set("reason", reason);
  if (page) query.set("page", String(page));
  if (pageSize) query.set("pageSize", String(pageSize));
  const search = query.toString();

  return useQuery({
    queryKey: ["ndrs", status ?? null, shipmentId ?? null, assigned ?? null, reason ?? null, page ?? null, pageSize ?? null],
    queryFn: () => clientFetch<PagedResult<Ndr>>(`/api/ndrs${search ? `?${search}` : ""}`),
  });
}
