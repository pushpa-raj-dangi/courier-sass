"use client";

import { DatePicker } from "@/components/ui/date-picker";
import { Tabs, TabsList, TabsTrigger } from "@/components/ui/tabs";
import type { DashboardRange } from "@/features/reports/lib/dashboardModel";

const OPTIONS: { value: DashboardRange["preset"]; label: string }[] = [
  { value: "today", label: "Today" },
  { value: "7d", label: "7 days" },
  { value: "30d", label: "30 days" },
  { value: "custom", label: "Custom" },
];

export function DashboardRangeControl({ value, onChange }: { value: DashboardRange; onChange: (r: DashboardRange) => void }) {
  return (
    <div className="flex flex-wrap items-center gap-2">
      <Tabs value={value.preset} onValueChange={(preset) => onChange({ preset: preset as DashboardRange["preset"], from: "", to: "" })}>
        <TabsList aria-label="Date range">
          {OPTIONS.map((o) => (
            <TabsTrigger key={o.value} value={o.value}>
              {o.label}
            </TabsTrigger>
          ))}
        </TabsList>
      </Tabs>
      {value.preset === "custom" && (
        <>
          <DatePicker id="dashboard-from" aria-label="From date" value={value.from} onChange={(from) => onChange({ ...value, from })} clearable />
          <DatePicker id="dashboard-to" aria-label="To date" value={value.to} onChange={(to) => onChange({ ...value, to })} clearable />
        </>
      )}
    </div>
  );
}
