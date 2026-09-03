"use client";

import * as React from "react";
import { Store } from "lucide-react";
import { MerchantNavMain } from "@/components/merchant-layout/MerchantNavMain";
import { useMerchantSession } from "@/features/merchant-auth/hooks/useMerchantSession";
import {
  Sidebar,
  SidebarContent,
  SidebarHeader,
  SidebarMenu,
  SidebarMenuItem,
  SidebarRail,
  useSidebar,
} from "@/components/ui/sidebar";

// No BranchSwitcher-equivalent — merchants have no branch concept. The brand lockup shows the
// merchant's own display name in place of the staff shell's organization name.
function MerchantSidebarBrand() {
  const { state } = useSidebar();
  const { data: session } = useMerchantSession();
  return (
    <SidebarMenu>
      <SidebarMenuItem className="flex items-center gap-2 px-2 py-1.5">
        <span className="flex h-7 w-7 shrink-0 items-center justify-center rounded-md bg-sidebar-primary">
          <Store className="h-4 w-4 text-sidebar-primary-foreground" strokeWidth={2} />
        </span>
        {state === "expanded" && (
          <span className="truncate font-display text-base font-semibold tracking-tight">
            {session?.merchantDisplayName ?? "Merchant Portal"}
          </span>
        )}
      </SidebarMenuItem>
    </SidebarMenu>
  );
}

export function MerchantAppSidebar(props: React.ComponentProps<typeof Sidebar>) {
  const { isMobile, setOpenMobile } = useSidebar();

  return (
    <Sidebar collapsible="icon" {...props}>
      <SidebarHeader>
        <MerchantSidebarBrand />
      </SidebarHeader>
      <SidebarContent>
        <MerchantNavMain onNavigate={isMobile ? () => setOpenMobile(false) : undefined} />
      </SidebarContent>
      <SidebarRail />
    </Sidebar>
  );
}
