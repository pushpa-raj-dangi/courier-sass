"use client";

import Link from "next/link";
import { SectionCard } from "@/components/page/SectionCard";
import { ErrorState } from "@/components/states/ErrorState";
import { StatusBadge } from "@/components/status-badge/StatusBadge";
import { Skeleton } from "@/components/ui/skeleton";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import { formatDate, formatRs } from "@/features/cod/lib/codFormat";
import { useMerchantShipments } from "@/features/merchant-shipments/hooks/useMerchantShipments";
import { EMPTY_MERCHANT_LIST_PARAMS } from "@/features/merchant-shipments/lib/listParams";
import { useMerchantShipmentPanelStore } from "@/features/merchant-shipments/store/merchantShipmentPanelStore";

export function RecentShipments() {
  const { data, isLoading, isError, refetch } = useMerchantShipments(EMPTY_MERCHANT_LIST_PARAMS, 7);
  const openDetail = useMerchantShipmentPanelStore((s) => s.openDetail);
  const items = data?.items ?? [];

  return (
    <SectionCard
      title="Recent shipments"
      action={
        <Link href="/merchant/shipments" className="text-sm font-medium text-primary hover:underline">
          View all
        </Link>
      }
    >
      {isLoading ? (
        <Skeleton className="h-40 w-full" />
      ) : isError ? (
        <ErrorState
          title="Couldn't load shipments"
          description="Something went wrong fetching your recent shipments."
          onRetry={() => refetch()}
        />
      ) : items.length === 0 ? (
        <p className="text-sm text-muted-foreground">No shipments yet</p>
      ) : (
        <Table>
          <TableHeader>
            <TableRow>
              <TableHead>AWB</TableHead>
              <TableHead>Recipient</TableHead>
              <TableHead>Status</TableHead>
              <TableHead className="text-right">COD</TableHead>
              <TableHead>Booked</TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            {items.map((s) => (
              <TableRow key={s.id}>
                <TableCell>
                  <button
                    type="button"
                    onClick={() => openDetail(s.awb)}
                    className="font-medium text-foreground hover:underline"
                  >
                    {s.awb}
                  </button>
                </TableCell>
                <TableCell>{s.recipientName}</TableCell>
                <TableCell>
                  <StatusBadge status={s.currentStatus} namespace="Merchant" />
                </TableCell>
                <TableCell className="text-right tabular-nums">{formatRs(s.codAmount)}</TableCell>
                <TableCell>{formatDate(s.createdAt)}</TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      )}
    </SectionCard>
  );
}
