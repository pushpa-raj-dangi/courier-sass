import type { PagedResult } from "@/components/data-table/DataTable";

/** One page of an array the API serves whole (manifests, returns), in DataTable's shape. */
export function toClientPage<T>(items: T[], page: number, pageSize: number): PagedResult<T> {
  const totalPages = Math.max(1, Math.ceil(items.length / pageSize));
  const current = Math.min(Math.max(1, page), totalPages);
  return {
    items: items.slice((current - 1) * pageSize, current * pageSize),
    page: current,
    pageSize,
    totalCount: items.length,
    totalPages,
  };
}
