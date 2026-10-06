"use client";

import { useEffect, useRef, useState } from "react";
import { Search } from "lucide-react";
import { Button } from "@/components/ui/button";
import { DatePicker } from "@/components/ui/date-picker";
import { useBranches } from "@/features/branches/hooks/useBranches";
import { useMerchants } from "@/features/shipments/hooks/useMerchants";
import { useServiceTypes } from "@/features/shipments/hooks/useServiceTypes";
import { hasActiveFilters, type BookedRange, type ShipmentListParams } from "@/features/shipments/lib/listParams";
import { FilterChip } from "./FilterChip";

const BOOKED_OPTIONS: { value: Exclude<BookedRange, "">; label: string }[] = [
  { value: "today", label: "Today" },
  { value: "7d", label: "Last 7 days" },
  { value: "30d", label: "Last 30 days" },
  { value: "custom", label: "Custom range" },
];

export function ShipmentFilterBar({
  params,
  onChange,
  onClear,
}: {
  params: ShipmentListParams;
  onChange: (patch: Partial<ShipmentListParams>) => void;
  onClear: () => void;
}) {
  const { data: branches } = useBranches();
  const { data: merchants } = useMerchants();
  const { data: serviceTypes } = useServiceTypes();
  const [searchText, setSearchText] = useState(params.q);

  // Last q this component pushed to the URL; a params.q that differs came from elsewhere.
  const lastPushedRef = useRef(params.q);
  const timerRef = useRef<ReturnType<typeof setTimeout> | null>(null);
  // The timer fires later, so it must call the latest onChange (it closes over current params).
  const onChangeRef = useRef(onChange);
  onChangeRef.current = onChange;

  function clearTimer() {
    if (timerRef.current) clearTimeout(timerRef.current);
    timerRef.current = null;
  }

  // URL → input only when the change came from elsewhere (Clear filters, back button),
  // never when it is just our own write catching up (that would drop newer keystrokes).
  useEffect(() => {
    if (params.q !== lastPushedRef.current) {
      clearTimer();
      lastPushedRef.current = params.q;
      setSearchText(params.q);
    }
  }, [params.q]);
  useEffect(() => clearTimer, []);

  // Input → URL, debounced by an owned timer so retyping text after an external reset is always pushed.
  function handleSearchChange(value: string) {
    setSearchText(value);
    clearTimer();
    if (value === lastPushedRef.current) return;
    timerRef.current = setTimeout(() => {
      timerRef.current = null;
      lastPushedRef.current = value;
      onChangeRef.current({ q: value });
    }, 300);
  }

  return (
    <div role="toolbar" aria-label="Shipment filters" className="flex flex-wrap items-center gap-2">
      <label className="flex h-9 w-full items-center gap-2 rounded-md border border-input bg-card px-2.5 text-[13px] text-muted-foreground sm:w-72">
        <Search className="h-4 w-4 shrink-0" aria-hidden="true" />
        <input
          type="search"
          aria-label="Filter shipments"
          placeholder="Filter by AWB, name or phone"
          value={searchText}
          onChange={(e) => handleSearchChange(e.target.value)}
          className="min-w-0 flex-1 bg-transparent text-foreground outline-none placeholder:text-muted-foreground"
        />
      </label>
      <FilterChip
        label="Branch"
        value={params.branchId}
        onChange={(branchId) => onChange({ branchId })}
        options={(branches ?? []).map((b) => ({ value: b.id, label: b.name }))}
      />
      <FilterChip
        label="Merchant"
        value={params.merchantId}
        onChange={(merchantId) => onChange({ merchantId })}
        options={(merchants ?? []).map((m) => ({ value: m.id, label: m.displayName }))}
      />
      <FilterChip
        label="Service"
        value={params.serviceTypeId}
        onChange={(serviceTypeId) => onChange({ serviceTypeId })}
        options={(serviceTypes ?? []).map((s) => ({ value: s.id, label: s.name }))}
      />
      <FilterChip
        label="Booked"
        value={params.booked}
        onChange={(booked) => onChange({ booked: booked as BookedRange, from: "", to: "" })}
        options={BOOKED_OPTIONS}
      />
      {params.booked === "custom" && (
        <div className="flex items-center gap-2">
          <DatePicker id="shipments-from" aria-label="Booked from" value={params.from} onChange={(from) => onChange({ from })} clearable />
          <DatePicker id="shipments-to" aria-label="Booked to" value={params.to} onChange={(to) => onChange({ to })} clearable />
        </div>
      )}
      {hasActiveFilters(params) && (
        <Button type="button" variant="ghost" size="sm" className="h-9" onClick={onClear}>
          Clear filters
        </Button>
      )}
    </div>
  );
}
