"use client";

import type { ReactNode } from "react";
import { Store } from "lucide-react";
import { ConsoleShell } from "@/components/console/ConsoleShell";
import { ThemeToggle } from "@/components/layout/ThemeToggle";
import { MerchantUserMenu } from "@/components/merchant-layout/MerchantUserMenu";
import { useMerchantNavGroups } from "@/components/merchant-layout/useMerchantNavGroups";
import { Separator } from "@/components/ui/separator";
import { useMerchantSession } from "@/features/merchant-auth/hooks/useMerchantSession";

// Merchants have no branch concept (no scope slot) and no staff search index (no headerStart);
// ReauthDialog/CommandPalette stay staff-only.
export function MerchantAppShell({ children }: { children: ReactNode }) {
  const { data: session, isLoading } = useMerchantSession();
  const { groups, isLoading: navLoading } = useMerchantNavGroups();
  return (
    <ConsoleShell
      brand={{ icon: Store, name: session?.merchantDisplayName ?? "Merchant Portal", subtitle: "Merchant portal", isLoading }}
      nav={groups}
      navLoading={navLoading}
      headerEnd={
        <>
          <ThemeToggle />
          <Separator orientation="vertical" className="h-6" />
          <MerchantUserMenu />
        </>
      }
    >
      {children}
    </ConsoleShell>
  );
}
