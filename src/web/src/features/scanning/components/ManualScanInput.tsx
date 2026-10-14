"use client";

import { forwardRef, useState } from "react";
import { ScanLine } from "lucide-react";
import { Input } from "@/components/ui/input";
import { cn } from "@/lib/utils";

// Always-available input: hand-typed AWBs and USB/Bluetooth keyboard-wedge scanners (which
// "type" the code and press Enter).
export const ManualScanInput = forwardRef<HTMLInputElement, { disabled: boolean; onSubmit: (awb: string) => void }>(
  function ManualScanInput({ disabled, onSubmit }, ref) {
    const [value, setValue] = useState("");
    return (
      <label
        className={cn(
          "flex h-16 items-center gap-3 rounded-xl border-2 bg-card px-4 transition-colors",
          disabled ? "border-border opacity-60" : "border-primary ring-4 ring-primary/10"
        )}
      >
        <ScanLine className="h-6 w-6 shrink-0 text-primary" aria-hidden="true" />
        <Input
          ref={ref}
          type="text"
          aria-label="Scan or type AWB"
          placeholder="Scan barcode / QR or type AWB…"
          autoComplete="off"
          spellCheck={false}
          disabled={disabled}
          value={value}
          onChange={(e) => setValue(e.target.value)}
          onKeyDown={(e) => {
            if (e.key === "Enter") {
              e.preventDefault();
              const awb = value.trim().toUpperCase();
              if (awb) onSubmit(awb);
              setValue("");
            } else if (e.key === "Escape") {
              setValue("");
            }
          }}
          className="h-auto min-w-0 flex-1 border-0 bg-transparent px-0 py-0 font-mono text-lg shadow-none focus-visible:ring-0 sm:text-xl md:text-xl"
        />
        <span className="hidden text-2xs text-muted-foreground sm:block">Enter submits · Esc clears</span>
      </label>
    );
  }
);
