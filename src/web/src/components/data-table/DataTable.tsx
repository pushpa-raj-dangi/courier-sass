import type { ReactNode } from "react";
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui/table";
import {
  Pagination,
  PaginationContent,
  PaginationEllipsis,
  PaginationItem,
  PaginationLink,
  PaginationNext,
  PaginationPrevious,
} from "@/components/ui/pagination";
import { EmptyState } from "@/components/states/EmptyState";
import { LoadingState } from "@/components/states/LoadingState";

export interface ColumnDef<T> {
  key: string;
  header: string;
  render: (row: T) => ReactNode;
}

export interface PagedResult<T> {
  items: T[];
  page: number;
  pageSize: number;
  totalCount: number;
  totalPages: number;
}

function getPageNumbers(current: number, total: number): (number | "ellipsis")[] {
  const siblingCount = 1;
  const pages: number[] = [];
  for (let i = 1; i <= total; i++) {
    if (i === 1 || i === total || (i >= current - siblingCount && i <= current + siblingCount)) {
      pages.push(i);
    }
  }

  const result: (number | "ellipsis")[] = [];
  let previous: number | undefined;
  for (const page of pages) {
    if (previous !== undefined) {
      if (page - previous === 2) {
        result.push(previous + 1);
      } else if (page - previous > 2) {
        result.push("ellipsis");
      }
    }
    result.push(page);
    previous = page;
  }
  return result;
}

export interface DataTableProps<T> {
  columns: ColumnDef<T>[];
  data?: PagedResult<T>;
  isLoading: boolean;
  emptyTitle: string;
  emptyDescription?: string;
  onPageChange: (page: number) => void;
  rowKey: (row: T) => string;
}

export function DataTable<T>({
  columns,
  data,
  isLoading,
  emptyTitle,
  emptyDescription,
  onPageChange,
  rowKey,
}: DataTableProps<T>) {
  if (isLoading) {
    return <LoadingState rows={data?.pageSize ?? 5} />;
  }
  if (!data || data.items.length === 0) {
    return <EmptyState title={emptyTitle} description={emptyDescription} />;
  }

  return (
    <div className="space-y-4">
      <div className="overflow-hidden rounded-xl border border-border bg-muted/40">
        <Table>
          <TableHeader>
            <TableRow>
              {columns.map((col) => (
                <TableHead key={col.key}>{col.header}</TableHead>
              ))}
            </TableRow>
          </TableHeader>
          <TableBody>
            {data.items.map((row) => (
              <TableRow key={rowKey(row)}>
                {columns.map((col) => (
                  <TableCell key={col.key}>{col.render(row)}</TableCell>
                ))}
              </TableRow>
            ))}
          </TableBody>
        </Table>
      </div>
      <div className="flex items-center justify-between">
        <p className="text-sm text-muted-foreground">
          Page {data.page} of {data.totalPages} ({data.totalCount} total)
        </p>
        {data.totalPages > 1 && (
          <Pagination className="mx-0 w-auto">
            <PaginationContent>
              <PaginationItem>
                <PaginationPrevious
                  href="#"
                  aria-disabled={data.page <= 1}
                  tabIndex={data.page <= 1 ? -1 : undefined}
                  className={data.page <= 1 ? "pointer-events-none opacity-50" : undefined}
                  onClick={(e) => {
                    e.preventDefault();
                    if (data.page > 1) onPageChange(data.page - 1);
                  }}
                />
              </PaginationItem>
              {getPageNumbers(data.page, data.totalPages).map((page, index) =>
                page === "ellipsis" ? (
                  <PaginationItem key={`ellipsis-${index}`}>
                    <PaginationEllipsis />
                  </PaginationItem>
                ) : (
                  <PaginationItem key={page}>
                    <PaginationLink
                      href="#"
                      isActive={page === data.page}
                      onClick={(e) => {
                        e.preventDefault();
                        if (page !== data.page) onPageChange(page);
                      }}
                    >
                      {page}
                    </PaginationLink>
                  </PaginationItem>
                )
              )}
              <PaginationItem>
                <PaginationNext
                  href="#"
                  aria-disabled={data.page >= data.totalPages}
                  tabIndex={data.page >= data.totalPages ? -1 : undefined}
                  className={
                    data.page >= data.totalPages ? "pointer-events-none opacity-50" : undefined
                  }
                  onClick={(e) => {
                    e.preventDefault();
                    if (data.page < data.totalPages) onPageChange(data.page + 1);
                  }}
                />
              </PaginationItem>
            </PaginationContent>
          </Pagination>
        )}
      </div>
    </div>
  );
}
