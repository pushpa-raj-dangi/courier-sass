"use client";

import { useQuery } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";
import type { PagedResult } from "@/components/data-table/DataTable";
import type { AdminUser } from "@/features/admin/types";

export function useAdminUsers(params: { page?: number; pageSize?: number } = {}) {
  const { page, pageSize } = params;
  const query = new URLSearchParams();
  if (page) query.set("page", String(page));
  if (pageSize) query.set("pageSize", String(pageSize));
  const search = query.toString();

  return useQuery({
    queryKey: ["admin", "users", page ?? null, pageSize ?? null],
    queryFn: () => clientFetch<PagedResult<AdminUser>>(`/api/users${search ? `?${search}` : ""}`),
  });
}
