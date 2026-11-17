"use client";

import { cn } from "@/lib/utils";
import { serviceTypeHint } from "@/features/merchant-shipments/lib/bookingFigures";
import type { ServiceType } from "@/features/merchant-shipments/types";

interface Props {
  serviceTypes: ServiceType[] | undefined;
  value: string | undefined;
  onChange: (id: string) => void;
  invalid?: boolean;
  describedBy?: string;
}

/** Radio-card group (no shadcn RadioGroup in this repo): native radios, visually hidden, in labelled cards. */
export function ServiceTypeCards({ serviceTypes, value, onChange, invalid, describedBy }: Props) {
  return (
    <div
      role="radiogroup"
      aria-label="Service type"
      aria-invalid={invalid || undefined}
      aria-describedby={describedBy}
      className="grid grid-cols-1 gap-2 sm:grid-cols-2"
    >
      {serviceTypes?.map((s) => {
        const hint = serviceTypeHint(s.code);
        const selected = value === s.id;
        return (
          <label
            key={s.id}
            className={cn(
              "flex cursor-pointer flex-col gap-0.5 rounded-lg border bg-card p-3 transition-colors focus-within:ring-2 focus-within:ring-ring",
              selected ? "border-primary bg-primary/5" : "border-border hover:bg-muted/50",
            )}
          >
            <input
              type="radio"
              name="serviceTypeId"
              value={s.id}
              checked={selected}
              onChange={() => onChange(s.id)}
              className="sr-only"
            />
            <span className="text-sm font-medium text-foreground">{s.name}</span>
            {hint && <span className="text-2xs text-muted-foreground">{hint}</span>}
          </label>
        );
      })}
    </div>
  );
}
