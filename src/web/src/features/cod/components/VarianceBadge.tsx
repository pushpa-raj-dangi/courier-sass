import { describeVariance } from "@/features/cod/lib/codFormat";

export function VarianceBadge({ variance }: { variance: number }) {
  const v = describeVariance(variance);
  return (
    <span className="inline-flex items-center gap-1.5 rounded bg-muted px-2 py-0.5 text-xs font-medium text-foreground">
      <span className="h-2 w-2 rounded-full" style={{ backgroundColor: `hsl(var(--${v.token}))` }} aria-hidden="true" />
      {v.label}
    </span>
  );
}
