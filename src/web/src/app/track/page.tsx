import { TrackingHeader } from "@/features/tracking/components/TrackingHeader";
import { TrackingSearchForm } from "@/features/tracking/TrackingSearchForm";

export default function TrackPage() {
  return (
    <div className="min-h-screen bg-background">
      <TrackingHeader />
      <main className="mx-auto flex w-full max-w-md flex-col gap-4 px-4 py-16">
        <span className="font-mono text-2xs uppercase tracking-[0.14em] text-muted-foreground">Track a parcel</span>
        <h1 className="font-display text-3xl font-semibold tracking-tight text-foreground">Track a parcel</h1>
        <p className="text-base text-muted-foreground">Enter the AWB number from your receipt or SMS.</p>
        <TrackingSearchForm />
        <p className="text-xs text-muted-foreground">No account needed — tracking is public for every shipment.</p>
      </main>
    </div>
  );
}
