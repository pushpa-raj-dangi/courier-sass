import type { ReactNode } from "react";
import { cn } from "@/lib/utils";

/**
 * A form field's error message, wired for assistive tech: `id` so the control can point to it
 * via `aria-describedby`, `role="alert"` so screen readers announce it the moment it appears
 * (validation errors show up after a user action, not on initial render, so they need the same
 * live-region treatment as a server-error banner). Pair with `aria-invalid`/`aria-describedby`
 * on the control itself — this component only renders the message, it doesn't wire the control.
 */
export function FieldError({
  id,
  className,
  children,
}: {
  id: string;
  className?: string;
  children: ReactNode;
}) {
  return (
    <p id={id} role="alert" className={cn("text-sm text-destructive", className)}>
      {children}
    </p>
  );
}
