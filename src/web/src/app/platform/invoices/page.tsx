"use client";

import { useState } from "react";
import { Badge } from "@/components/ui/badge";
import { Spinner } from "@/components/ui/spinner";
import { Input } from "@/components/ui/input";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { ErrorState } from "@/components/states/ErrorState";
import { DataTable, type ColumnDef } from "@/components/data-table/DataTable";
import { usePlatformInvoices } from "@/features/platform-invoices/hooks/usePlatformInvoices";
import { useDebouncedValue } from "@/lib/hooks/useDebouncedValue";
import type { AdminInvoice } from "@/features/platform-invoices/types";

const PAGE_SIZE = 25;

export default function PlatformInvoicesPage() {
  const [searchInput, setSearchInput] = useState("");
  const [status, setStatus] = useState("all");
  const [page, setPage] = useState(1);
  const debouncedSearch = useDebouncedValue(searchInput, 300);
  const { data, isLoading, isError, refetch } = usePlatformInvoices(status, debouncedSearch, page, PAGE_SIZE);

  const columns: ColumnDef<AdminInvoice>[] = [
    { key: "organization", header: "Organization", render: (i) => i.organizationName },
    { key: "plan", header: "Plan", render: (i) => i.planName },
    { key: "amount", header: "Amount (NPR)", render: (i) => i.amountNpr },
    { key: "status", header: "Status", render: (i) => <Badge variant={i.status === "Overdue" ? "destructive" : i.status === "Paid" ? "default" : "secondary"}>{i.status}</Badge> },
    { key: "dueAt", header: "Due", render: (i) => new Date(i.dueAt).toLocaleDateString() },
  ];

  if (isError) {
    return (
      <ErrorState
        title="Couldn't load invoices"
        description="Something went wrong fetching the invoice list."
        onRetry={() => refetch()}
      />
    );
  }

  return (
    <div className="space-y-4 p-6">
      <h1 className="font-display text-xl font-semibold">Invoices</h1>
      <div className="flex flex-wrap items-center gap-2">
        <Input
          value={searchInput}
          onChange={(e) => {
            setSearchInput(e.target.value);
            setPage(1);
          }}
          placeholder="Search by organization name"
          className="max-w-sm"
          aria-label="Search invoices"
        />
        <Select
          value={status}
          onValueChange={(v) => {
            setStatus(v);
            setPage(1);
          }}
        >
          <SelectTrigger className="w-48" aria-label="Filter by status">
            <SelectValue />
          </SelectTrigger>
          <SelectContent>
            <SelectItem value="all">All statuses</SelectItem>
            <SelectItem value="Pending">Pending</SelectItem>
            <SelectItem value="Paid">Paid</SelectItem>
            <SelectItem value="Overdue">Overdue</SelectItem>
            <SelectItem value="Cancelled">Cancelled</SelectItem>
          </SelectContent>
        </Select>
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
          emptyTitle="No invoices found"
          emptyDescription="Try a different search term or status filter."
          onPageChange={setPage}
          rowKey={(i) => i.id}
        />
      )}
    </div>
  );
}
