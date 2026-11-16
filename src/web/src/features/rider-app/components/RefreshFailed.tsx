import { Button } from "@/components/ui/button";

/** A failed background refetch: the last loaded data stays on screen, with a way to try again. */
export function RefreshFailed({ onRetry }: { onRetry: () => void }) {
  return (
    <div role="status" className="flex items-center justify-between gap-3 rounded-lg border border-border bg-muted px-3 py-2">
      <p className="text-xs font-medium text-foreground">Couldn&apos;t refresh · showing what was last loaded</p>
      <Button type="button" variant="outline" size="sm" className="h-9 shrink-0" onClick={onRetry}>
        Retry
      </Button>
    </div>
  );
}
