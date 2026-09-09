"use client";

import { Badge } from "@/components/ui/badge";
import { Spinner } from "@/components/ui/spinner";
import { ErrorState } from "@/components/states/ErrorState";
import { DataTable, type ColumnDef } from "@/components/data-table/DataTable";
import { usePlatformInvoices } from "@/features/platform-invoices/hooks/usePlatformInvoices";
import type { AdminInvoice } from "@/features/platform-invoices/types";

export default function PlatformInvoicesPage() {
  const { data, isLoading, isError, refetch } = usePlatformInvoices();

  const columns: ColumnDef<AdminInvoice>[] = [
    { key: "organization", header: "Organization", render: (i) => i.organizationName },
    { key: "plan", header: "Plan", render: (i) => i.planName },
    { key: "amount", header: "Amount (NPR)", render: (i) => i.amountNpr },
    { key: "status", header: "Status", render: (i) => <Badge variant={i.status === "Overdue" ? "destructive" : i.status === "Paid" ? "default" : "secondary"}>{i.status}</Badge> },
    { key: "dueAt", header: "Due", render: (i) => new Date(i.dueAt).toLocaleDateString() },
  ];

  if (isLoading) {
    return (
      <div className="flex items-center justify-center p-12">
        <Spinner className="h-6 w-6" />
      </div>
    );
  }
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
      <DataTable
        columns={columns}
        data={data && { items: data, page: 1, pageSize: data.length, totalCount: data.length, totalPages: 1 }}
        isLoading={false}
        emptyTitle="No invoices yet"
        emptyDescription="Invoices appear here once a paid plan's billing cycle generates one."
        onPageChange={() => {}}
        rowKey={(i) => i.id}
      />
    </div>
  );
}
