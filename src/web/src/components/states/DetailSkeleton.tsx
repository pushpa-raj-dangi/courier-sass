import { Skeleton } from "@/components/ui/skeleton";

export function DetailSkeleton({ fieldRows = 3, fieldsPerRow = 4 }: { fieldRows?: number; fieldsPerRow?: number }) {
  return (
    <div role="status" aria-live="polite" className="space-y-6">
      <span className="sr-only">Loading</span>
      {Array.from({ length: fieldRows }).map((_, row) => (
        <div key={row} className="grid grid-cols-2 gap-4">
          {Array.from({ length: fieldsPerRow }).map((_, col) => (
            <div key={col} className="flex flex-col gap-1.5">
              <Skeleton className="h-3 w-16" />
              <Skeleton className="h-4 w-28" />
            </div>
          ))}
        </div>
      ))}
    </div>
  );
}
