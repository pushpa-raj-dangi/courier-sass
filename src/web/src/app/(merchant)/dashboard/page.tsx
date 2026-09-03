"use client";

import { useMerchantSession } from "@/features/merchant-auth/hooks/useMerchantSession";

// Real stat tiles land in Task 15, once the Shipments (Task 8) and COD (Task 11) data-layer
// hooks this dashboard aggregates over actually exist — a greeting-only placeholder here keeps
// this task's own deliverable (the shell + nav + route protection) independently testable.
export default function MerchantDashboardPage() {
  const { data: session } = useMerchantSession();
  return (
    <div>
      <h1 className="font-display text-2xl font-semibold tracking-tight text-foreground">
        {session ? `Welcome back, ${session.fullName}` : "Dashboard"}
      </h1>
      <p className="mt-1 text-sm text-muted-foreground">{session?.merchantDisplayName}</p>
    </div>
  );
}
