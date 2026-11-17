"use client";

import { useMemo } from "react";
import { DataTable, type ColumnDef } from "@/components/data-table/DataTable";
import { Button } from "@/components/ui/button";
import { SectionError } from "@/components/states/SectionError";
import { StatusBadge } from "@/components/status-badge/StatusBadge";
import { formatRs } from "@/features/cod/lib/codFormat";
import { useMerchantSettlements } from "@/features/merchant-cod/hooks/useMerchantSettlements";
import { filterByPeriod, settlementTotals, type SettlementPeriod } from "@/features/merchant-cod/lib/settlementTotals";
import { formatPeriod, formatPaidOn } from "@/features/merchant-cod/lib/formatPeriod";
import { useMerchantSettlementPanelStore } from "@/features/merchant-cod/store/merchantSettlementPanelStore";
import type { MerchantSettlement } from "@/features/merchant-cod/types";

const money = "block text-right tabular-nums";

const columns = (onOpen: (id: string) => void): ColumnDef<MerchantSettlement>[] => [
  {
    key: "period",
    header: "Period",
    // A real button so keyboard and screen-reader users can open the sheet (the row click is mouse-only).
    render: (s) => (
      <Button
        type="button"
        variant="link"
        className="h-auto p-0 font-medium text-foreground"
        onClick={(e) => {
          e.stopPropagation();
          onOpen(s.id);
        }}
      >
        {formatPeriod(s.periodFrom, s.periodTo)}
      </Button>
    ),
  },
  { key: "parcels", header: "Parcels", render: (s) => <span className="tabular-nums">{s.items.length}</span> },
  { key: "cod", header: "COD collected", render: (s) => <span className={money}>{formatRs(settlementTotals(s.items).cod)}</span> },
  { key: "fees", header: "Fees", render: (s) => <span className={money}>{formatRs(settlementTotals(s.items).fees)}</span> },
  { key: "net", header: "You receive", render: (s) => <span className={`${money} font-medium`}>{formatRs(s.totalPayable)}</span> },
  { key: "status", header: "Status", render: (s) => <StatusBadge status={s.status} namespace="MerchantPayout" /> },
  { key: "paid", header: "Paid on", render: (s) => <span className="text-xs text-muted-foreground">{formatPaidOn(s.settledAt)}</span> },
];

export function SettlementsTable({ period }: { period: SettlementPeriod }) {
  const { data, isLoading, isError, refetch } = useMerchantSettlements();
  const openDetail = useMerchantSettlementPanelStore((s) => s.openDetail);
  const cols = useMemo(() => columns(openDetail), [openDetail]);

  const paged = useMemo(() => {
    if (!data) return undefined;
    const items = filterByPeriod(data, period, new Date());
    return { items, page: 1, pageSize: items.length || 1, totalCount: items.length, totalPages: 1 };
  }, [data, period]);

  if (isError) return <SectionError message="Couldn't load your settlements." onRetry={() => refetch()} />;

  return (
    <DataTable
      columns={cols}
      data={paged}
      isLoading={isLoading}
      emptyTitle="No settlements yet"
      emptyDescription="Settlements appear here once your courier issues one for a period."
      onPageChange={() => {}}
      rowKey={(s) => s.id}
      onRowClick={(s) => openDetail(s.id)}
    />
  );
}
