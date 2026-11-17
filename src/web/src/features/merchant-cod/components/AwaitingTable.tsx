"use client";

import { DataTable, type ColumnDef } from "@/components/data-table/DataTable";
import { SectionError } from "@/components/states/SectionError";
import { formatDate, formatRs } from "@/features/cod/lib/codFormat";
import { useMerchantAwaiting } from "@/features/merchant-cod/hooks/useMerchantAwaiting";
import type { MerchantAwaitingCollection } from "@/features/merchant-cod/types";

const money = "block text-right tabular-nums";

const COLUMNS: ColumnDef<MerchantAwaitingCollection>[] = [
  { key: "awb", header: "AWB", render: (r) => <span className="font-mono font-medium">{r.awb}</span> },
  { key: "collected", header: "Collected on", render: (r) => <span className="text-xs text-muted-foreground">{formatDate(r.collectedAt)}</span> },
  { key: "cod", header: "COD", render: (r) => <span className={money}>{formatRs(r.collectedAmount)}</span> },
  { key: "fee", header: "Fee", render: (r) => <span className={money}>{formatRs(r.courierFee)}</span> },
  { key: "net", header: "Net", render: (r) => <span className={`${money} font-medium`}>{formatRs(r.net)}</span> },
];

export function AwaitingTable({ page, onPageChange }: { page: number; onPageChange: (page: number) => void }) {
  const { data, isLoading, isError, refetch } = useMerchantAwaiting(page);

  if (isError) return <SectionError message="Couldn't load parcels awaiting settlement." onRetry={() => refetch()} />;

  return (
    <DataTable
      columns={COLUMNS}
      data={data}
      isLoading={isLoading}
      emptyTitle="Nothing awaiting settlement"
      emptyDescription="Delivered COD parcels show up here until they are included in a settlement."
      onPageChange={onPageChange}
      rowKey={(r) => r.shipmentId}
    />
  );
}
