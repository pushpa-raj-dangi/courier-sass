import { Skeleton } from "@/components/ui/skeleton";
import { cn } from "@/lib/utils";

export function StatTilesSkeleton({ count = 6, className }: { count?: number; className?: string }) {
  return (
    <div
      role="status"
      aria-live="polite"
      className={cn("grid grid-cols-2 gap-4 sm:grid-cols-3 lg:grid-cols-6", className)}
    >
      <span className="sr-only">Loading</span>
      {Array.from({ length: count }).map((_, i) => (
        <div key={i} className="rounded-lg border bg-card p-4">
          <Skeleton className="h-3 w-16" />
          <Skeleton className="mt-2 h-6 w-12" />
        </div>
      ))}
    </div>
  );
}
