"use client";

import Link from "next/link";
import { useRouter } from "next/navigation";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Skeleton } from "@/components/ui/skeleton";
import { useMyRider } from "@/features/riders/hooks/useMyRider";
import { useOutboxStatus } from "@/features/rider-app/sync/useOutboxStatus";
import { updateOutboxItem } from "@/features/rider-app/sync/outboxDb";
import { drainOutbox } from "@/features/rider-app/sync/syncManager";

export default function RiderProfilePage() {
  const { data: rider, isLoading } = useMyRider();
  const { stuckItems, refresh } = useOutboxStatus();
  const router = useRouter();

  async function handleLogout() {
    await fetch("/api/auth/logout", { method: "POST" });
    router.push("/login");
  }

  async function handleRetry(itemId: string) {
    // Flip the item back to "pending" (clearing the stale error) and kick a drain attempt. This
    // doesn't guarantee the retry succeeds — a still-permanent 4xx will mark it "stuck" again —
    // it just gives the rider a way to re-attempt without losing the queued action (CLAUDE.md §39:
    // "was COD collected?" must never go unanswered because a stuck item had no recovery path).
    await updateOutboxItem(itemId, { status: "pending", lastError: undefined });
    await refresh();
    void drainOutbox();
  }

  if (isLoading) {
    return (
      <div className="space-y-4 p-4" role="status" aria-live="polite">
        <span className="sr-only">Loading</span>
        <Skeleton className="h-6 w-24" />
        <Card>
          <CardHeader>
            <Skeleton className="h-5 w-32" />
          </CardHeader>
          <CardContent>
            <Skeleton className="h-4 w-48" />
          </CardContent>
        </Card>
      </div>
    );
  }

  return (
    <div className="space-y-4 p-4">
      <h1 className="text-xl font-semibold">Profile</h1>
      {rider && (
        <Card>
          <CardHeader><CardTitle>{rider.roleType}</CardTitle></CardHeader>
          <CardContent className="space-y-1 text-sm">
            <p className="text-muted-foreground">License: <span>{rider.licenseNumber}</span></p>
          </CardContent>
        </Card>
      )}
      {stuckItems.length > 0 && (
        <Card>
          <CardHeader><CardTitle className="text-destructive">Needs Review</CardTitle></CardHeader>
          <CardContent className="space-y-3">
            {stuckItems.map((item) => (
              <div key={item.id} className="rounded-md border border-destructive/40 p-3 text-sm">
                <p className="font-medium">{item.action.kind} &middot; shipment {item.action.shipmentId}</p>
                <p className="text-muted-foreground">{item.lastError}</p>
                <Button
                  type="button"
                  variant="outline"
                  size="sm"
                  className="mt-2"
                  onClick={() => void handleRetry(item.id)}
                >
                  Retry
                </Button>
              </div>
            ))}
          </CardContent>
        </Card>
      )}
      <Button asChild variant="outline" size="lg" className="w-full">
        <Link href="/dashboard">Back to Staff App</Link>
      </Button>
      <Button type="button" variant="outline" size="lg" className="w-full" onClick={handleLogout}>
        Log Out
      </Button>
    </div>
  );
}
