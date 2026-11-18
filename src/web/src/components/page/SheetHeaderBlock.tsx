import type { ReactNode } from "react";
import { SheetTitle } from "@/components/ui/sheet";
import { cn } from "@/lib/utils";

// Standard detail-Sheet header (adopted by Sheets in R2–R6). Title stays in the body face per
// DESIGN.md's Sheet-Stays-Plain Rule; `mono` is for AWB / manifest codes.
export function SheetHeaderBlock({
  eyebrow,
  title,
  mono = false,
  status,
  subtitle,
  facts,
}: {
  eyebrow: string;
  title: ReactNode;
  mono?: boolean;
  status?: ReactNode;
  subtitle?: ReactNode;
  facts?: { label: string; value: ReactNode }[];
}) {
  return (
    <div className="space-y-4 border-b border-border pb-4">
      <div className="space-y-1.5">
        <p className="font-mono text-2xs uppercase tracking-[0.12em] text-muted-foreground">{eyebrow}</p>
        <div className="flex flex-wrap items-center gap-2.5">
          <SheetTitle className={cn("text-xl", mono ? "font-mono font-medium" : "font-sans font-semibold")}>{title}</SheetTitle>
          {status}
        </div>
        {subtitle && <p className="text-sm text-muted-foreground">{subtitle}</p>}
      </div>
      {facts && facts.length > 0 && (
        <dl className="grid grid-cols-2 gap-3 sm:grid-cols-4">
          {facts.map((fact) => (
            <div key={fact.label} className="space-y-0.5">
              <dt className="text-2xs text-muted-foreground">{fact.label}</dt>
              <dd className="text-sm font-medium text-foreground">{fact.value}</dd>
            </div>
          ))}
        </dl>
      )}
    </div>
  );
}
