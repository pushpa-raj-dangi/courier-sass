"use client";

import * as React from "react";
import { Package } from "lucide-react";
import { NavMain } from "@/components/nav-main";
import { BranchSwitcher } from "@/components/layout/BranchSwitcher";
import { useSession } from "@/features/auth/hooks/useSession";
import {
  Sidebar,
  SidebarContent,
  SidebarHeader,
  SidebarMenu,
  SidebarMenuItem,
  SidebarRail,
  useSidebar,
} from "@/components/ui/sidebar";

// No team-switching concept exists in this app (a staff user belongs to exactly one
// organization) — the block's TeamSwitcher is replaced by a brand lockup showing that one
// organization's real name (falling back to the generic product name only until the session
// loads), with BranchSwitcher (this app's real "which scope am I acting in" control) underneath.
function SidebarBrand() {
  const { state } = useSidebar();
  const { data: session } = useSession();
  return (
    <SidebarMenu>
      <SidebarMenuItem className="flex items-center gap-2 px-2 py-1.5">
        <span className="flex h-7 w-7 shrink-0 items-center justify-center rounded-md bg-sidebar-primary">
          <Package className="h-4 w-4 text-sidebar-primary-foreground" strokeWidth={2} />
        </span>
        {state === "expanded" && (
          <span className="truncate font-display text-base font-semibold tracking-tight">
            {session?.organizationName ?? "Courier Platform"}
          </span>
        )}
      </SidebarMenuItem>
      {state === "expanded" && (
        <SidebarMenuItem className="px-2">
          <BranchSwitcher />
        </SidebarMenuItem>
      )}
    </SidebarMenu>
  );
}

export function AppSidebar(props: React.ComponentProps<typeof Sidebar>) {
  const { isMobile, setOpenMobile } = useSidebar();

  return (
    <Sidebar collapsible="icon" {...props}>
      <SidebarHeader>
        <SidebarBrand />
      </SidebarHeader>
      <SidebarContent>
        <NavMain onNavigate={isMobile ? () => setOpenMobile(false) : undefined} />
      </SidebarContent>
      <SidebarRail />
    </Sidebar>
  );
}
