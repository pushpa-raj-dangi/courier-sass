"use client";

import { useState } from "react";
import { Download } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { DatePicker } from "@/components/ui/date-picker";
import { Label } from "@/components/ui/label";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { useBranches } from "@/features/branches/hooks/useBranches";
import { usePermission } from "@/features/auth/hooks/usePermission";

export function ExportReportsCard() {
  const [branchId, setBranchId] = useState<string>("all");
  const [from, setFrom] = useState("");
  const [to, setTo] = useState("");
  const { data: branches } = useBranches();
  const canExport = usePermission("report.export");

  if (!canExport) {
    return null;
  }

  // Same UTC-anchoring requirement as the dashboard's own date filter (Task 4) — these query
  // strings feed the identical backend DateTimeOffset parameters.
  function buildQuery(): string {
    const query = new URLSearchParams();
    if (branchId !== "all") query.set("branchId", branchId);
    if (from) query.set("from", `${from}T00:00:00Z`);
    if (to) query.set("to", `${to}T23:59:59Z`);
    const search = query.toString();
    return search ? `?${search}` : "";
  }

  return (
    <Card>
      <CardHeader>
        <CardTitle className="flex items-center gap-3 font-display text-base">
          <span className="flex h-9 w-9 items-center justify-center rounded-lg bg-muted">
            <Download className="h-4 w-4 text-muted-foreground" strokeWidth={2} />
          </span>
          Export Reports
        </CardTitle>
        <CardDescription>Download CSV reports for a branch and date range</CardDescription>
      </CardHeader>
      <CardContent className="space-y-4">
        <div className="flex flex-wrap items-end gap-3">
          <div className="space-y-1">
            <Label htmlFor="export-branch">Branch</Label>
            <Select value={branchId} onValueChange={setBranchId}>
              <SelectTrigger id="export-branch" className="w-48" aria-label="Filter by branch">
                <SelectValue placeholder="All branches" />
              </SelectTrigger>
              <SelectContent>
                <SelectItem value="all">All branches</SelectItem>
                {branches?.map((b) => (
                  <SelectItem key={b.id} value={b.id}>{b.name}</SelectItem>
                ))}
              </SelectContent>
            </Select>
          </div>
          <div className="space-y-1">
            <Label htmlFor="export-from">From</Label>
            <DatePicker id="export-from" aria-label="Export from date" value={from} onChange={setFrom} clearable />
          </div>
          <div className="space-y-1">
            <Label htmlFor="export-to">To</Label>
            <DatePicker id="export-to" aria-label="Export to date" value={to} onChange={setTo} clearable />
          </div>
        </div>
        <div className="flex flex-wrap gap-3">
          <Button asChild variant="outline">
            <a href={`/api/reports/export/shipments${buildQuery()}`}>Export Shipments (CSV)</a>
          </Button>
          <Button asChild variant="outline">
            <a href={`/api/reports/export/cod-summary${buildQuery()}`}>Export COD Summary (CSV)</a>
          </Button>
        </div>
      </CardContent>
    </Card>
  );
}
