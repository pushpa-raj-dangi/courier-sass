import type { ReactNode } from "react";
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui/table";
import { Button } from "@/components/ui/button";
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
      <div className="flex items-center justify-between">
        <p className="text-sm text-muted-foreground">
          Page {data.page} of {data.totalPages} ({data.totalCount} total)
        </p>
        <div className="flex gap-2">
          <Button
            variant="outline"
            size="sm"
            disabled={data.page <= 1}
            onClick={() => onPageChange(data.page - 1)}
          >
            Previous
          </Button>
          <Button
            variant="outline"
            size="sm"
            disabled={data.page >= data.totalPages}
            onClick={() => onPageChange(data.page + 1)}
          >
            Next
          </Button>
        </div>
      </div>
    </div>
  );
}
