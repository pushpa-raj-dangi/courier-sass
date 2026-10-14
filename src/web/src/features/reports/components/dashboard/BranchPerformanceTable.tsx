"use client";

import { SectionCard } from "@/components/page/SectionCard";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import { useBranches } from "@/features/branches/hooks/useBranches";
import { formatRate } from "@/features/reports/lib/dashboardModel";
import type { BranchPerformance } from "@/features/reports/types";

export function BranchPerformanceTable({ data }: { data: Record<string, BranchPerformance> }) {
  const { data: branches } = useBranches();
  const rows = Object.entries(data).sort(([, a], [, b]) => b.shipmentVolume - a.shipmentVolume);
  return (
    <SectionCard title="Branch performance">
      {rows.length === 0 ? (
        <p className="py-6 text-center text-sm text-muted-foreground">No branch activity in this range.</p>
      ) : (
        <div className="overflow-x-auto">
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>Branch</TableHead>
                <TableHead className="text-right">Volume</TableHead>
                <TableHead className="text-right">Delivered</TableHead>
                <TableHead>Success rate</TableHead>
                <TableHead className="text-right">NDR</TableHead>
                <TableHead className="text-right">RTO</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {rows.map(([id, b]) => {
                const branch = branches?.find((x) => x.id === id);
                const pct = b.deliverySuccessRate === null ? null : Math.round(b.deliverySuccessRate * 100);
                return (
                  <TableRow key={id}>
                    <TableCell>
                      <div className="flex flex-col">
                        <span className="font-medium">{branch?.name ?? "Unknown branch"}</span>
                        <span className="font-mono text-2xs text-muted-foreground">{branch?.code ?? ""}</span>
                      </div>
                    </TableCell>
                    <TableCell className="text-right tabular-nums">{b.shipmentVolume}</TableCell>
                    <TableCell className="text-right tabular-nums">{b.deliverySuccesses}</TableCell>
                    <TableCell>
                      <div className="flex items-center gap-2">
                        {pct !== null && (
                          <span className="h-1.5 w-20 rounded-full bg-muted" aria-hidden="true">
                            <span data-rate-bar className="block h-1.5 rounded-full bg-status-delivered" style={{ width: `${pct}%` }} />
                          </span>
                        )}
                        <span className="text-xs tabular-nums">{formatRate(b.deliverySuccessRate)}</span>
                      </div>
                    </TableCell>
                    <TableCell className="text-right tabular-nums">{b.ndrCount}</TableCell>
                    <TableCell className="text-right tabular-nums">{b.rtoCount}</TableCell>
                  </TableRow>
                );
              })}
            </TableBody>
          </Table>
        </div>
      )}
    </SectionCard>
  );
}
