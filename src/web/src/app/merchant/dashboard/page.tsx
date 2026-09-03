"use client";

import { useMerchantSession } from "@/features/merchant-auth/hooks/useMerchantSession";
import { MerchantDashboardStats } from "@/features/merchant-dashboard/components/MerchantDashboardStats";

export default function MerchantDashboardPage() {
  const { data: session } = useMerchantSession();
  return (
    <div className="space-y-6">
      <div>
        <h1 className="font-display text-2xl font-semibold tracking-tight text-foreground">
          {session ? `Welcome back, ${session.fullName}` : "Dashboard"}
        </h1>
        <p className="mt-1 text-sm text-muted-foreground">{session?.merchantDisplayName}</p>
      </div>
      <MerchantDashboardStats />
    </div>
  );
}
