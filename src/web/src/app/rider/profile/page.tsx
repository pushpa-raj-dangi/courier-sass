"use client";

import { useRouter } from "next/navigation";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Spinner } from "@/components/ui/spinner";
import { useMyRider } from "@/features/riders/hooks/useMyRider";
import { useOutboxStatus } from "@/features/rider-app/sync/useOutboxStatus";

export default function RiderProfilePage() {
  const { data: rider, isLoading } = useMyRider();
  const { stuckItems } = useOutboxStatus();
  const router = useRouter();

  async function handleLogout() {
    await fetch("/api/auth/logout", { method: "POST" });
    router.push("/login");
  }

  if (isLoading) {
    return (
      <div className="flex items-center justify-center p-12">
        <Spinner className="h-6 w-6" />
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
              </div>
            ))}
          </CardContent>
        </Card>
      )}
      <Button type="button" variant="outline" size="lg" className="w-full" onClick={handleLogout}>
        Log Out
      </Button>
    </div>
  );
}
