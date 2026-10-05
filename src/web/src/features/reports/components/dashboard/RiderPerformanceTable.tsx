"use client";

import { SectionCard } from "@/components/page/SectionCard";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import { usePeopleNames } from "@/features/shipments/hooks/usePeopleNames";
import { formatRate } from "@/features/reports/lib/dashboardModel";
import type { RiderPerformance } from "@/features/reports/types";

export function RiderPerformanceTable({ data }: { data: RiderPerformance[] }) {
  const names = usePeopleNames();
  const top = [...data].sort((a, b) => b.successes - a.successes).slice(0, 5);
  return (
    <SectionCard title="Rider performance" description="Top 5 by deliveries">
      {top.length === 0 ? (
        <p className="py-6 text-center text-sm text-muted-foreground">No rider activity in this range.</p>
      ) : (
        <div className="overflow-x-auto">
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>Rider</TableHead>
                <TableHead className="text-right">Attempts</TableHead>
                <TableHead className="text-right">Delivered</TableHead>
                <TableHead className="text-right">Success rate</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {top.map((r) => {
                const name = names.riderName(r.riderId);
                return (
                  <TableRow key={r.riderId}>
                    <TableCell>{name === "Rider" ? `Rider ${r.riderId.slice(0, 6)}` : name}</TableCell>
                    <TableCell className="text-right tabular-nums">{r.attempts}</TableCell>
                    <TableCell className="text-right tabular-nums">{r.successes}</TableCell>
                    <TableCell className="text-right tabular-nums">{formatRate(r.successRate)}</TableCell>
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
