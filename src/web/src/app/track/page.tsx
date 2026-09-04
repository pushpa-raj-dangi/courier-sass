import { Package } from "lucide-react";
import { TrackingSearchForm } from "@/features/tracking/TrackingSearchForm";

export default function TrackPage() {
  return (
    <main className="flex min-h-screen flex-col items-center justify-center gap-6 bg-background p-4">
      <div className="flex flex-col items-center gap-2">
        <span className="flex h-11 w-11 items-center justify-center rounded-lg bg-primary">
          <Package className="h-5 w-5 text-primary-foreground" strokeWidth={2} />
        </span>
        <span className="font-display text-lg font-semibold tracking-tight">Courier Platform</span>
      </div>
      <TrackingSearchForm />
    </main>
  );
}
