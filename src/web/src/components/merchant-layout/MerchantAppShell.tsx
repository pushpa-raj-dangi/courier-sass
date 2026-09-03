import type { ReactNode } from "react";
import { MerchantAppSidebar } from "@/components/merchant-layout/MerchantAppSidebar";
import { MerchantUserMenu } from "@/components/merchant-layout/MerchantUserMenu";
import { ThemeToggle } from "@/components/layout/ThemeToggle";
import { SidebarInset, SidebarProvider, SidebarTrigger } from "@/components/ui/sidebar";
import { Separator } from "@/components/ui/separator";

// Deliberately not the staff AppShell — that component's HeaderSearch and ReauthDialog are
// staff-specific (search indexes staff-only entities; ReauthDialog reacts to the staff session's
// own refresh-failure signal) and out of this phase's scope. Reuses the same shadcn
// Sidebar/SidebarProvider primitives, own nav catalog and own user menu.
export function MerchantAppShell({ children }: { children: ReactNode }) {
  return (
    <SidebarProvider>
      <MerchantAppSidebar />
      <SidebarInset>
        <header className="sticky top-0 z-10 flex h-16 shrink-0 items-center gap-3 border-b border-border bg-card px-4 sm:px-6">
          <SidebarTrigger className="h-10 w-10 rounded-lg border border-input" />
          <div className="ml-auto flex items-center gap-3">
            <ThemeToggle />
            <Separator orientation="vertical" className="h-6" />
            <MerchantUserMenu />
          </div>
        </header>
        <div className="flex-1 bg-background p-6">{children}</div>
      </SidebarInset>
    </SidebarProvider>
  );
}
