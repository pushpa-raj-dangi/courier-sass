"use client";

import { Button } from "@/components/ui/button";
import { Spinner } from "@/components/ui/spinner";
import { formatRs } from "@/features/cod/lib/codFormat";
import type { BookingFigures } from "@/features/merchant-shipments/lib/bookingFigures";

interface Props {
  figures: BookingFigures | null;
  isQuoting: boolean;
  isSubmitting: boolean;
  onCancel: () => void;
}

function Figure({ label, value, strong }: { label: string; value: string; strong?: boolean }) {
  return (
    <div className="flex flex-col gap-0.5">
      <dt className="text-2xs text-muted-foreground">{label}</dt>
      <dd className={strong ? "font-semibold tabular-nums text-foreground" : "tabular-nums text-foreground"}>{value}</dd>
    </div>
  );
}

/** Sticky footer: server-computed figures (display only) above Cancel and the primary Book button. */
export function BookingPriceFooter({ figures, isQuoting, isSubmitting, onCancel }: Props) {
  const money = (n: number | null | undefined) => (n == null ? "—" : formatRs(n));
  return (
    <div className="sticky bottom-0 space-y-3 border-t border-border bg-background px-6 py-4">
      <dl className="grid grid-cols-4 gap-2 text-sm" aria-live="polite" aria-atomic="true">
        <Figure label="Delivery" value={money(figures?.delivery)} />
        <Figure label="Insurance" value={money(figures?.insurance)} />
        <Figure label="Your charge" value={money(figures?.charge)} />
        <Figure label="You receive" value={money(figures?.receive)} strong />
      </dl>
      <div className="flex justify-end gap-2">
        <Button type="button" variant="outline" onClick={onCancel}>Cancel</Button>
        <Button type="submit" disabled={isSubmitting || !figures || isQuoting}>
          {isSubmitting ? <Spinner className="h-4 w-4" /> : figures ? `Book for ${formatRs(figures.charge)}` : "Book shipment"}
        </Button>
      </div>
    </div>
  );
}
