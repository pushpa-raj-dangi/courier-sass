import type { ReactNode } from "react";
import { AppSidebar } from "@/components/app-sidebar";
import { ReauthDialog } from "@/components/layout/ReauthDialog";
import { SidebarInset, SidebarProvider, SidebarTrigger } from "@/components/ui/sidebar";
import { Separator } from "@/components/ui/separator";

export function AppShell({ children }: { children: ReactNode }) {
  return (
    <SidebarProvider>
      <AppSidebar />
      <SidebarInset>
        <header className="sticky top-0 z-10 flex h-14 shrink-0 items-center gap-2 border-b border-border bg-card px-4">
          <SidebarTrigger />
          <Separator orientation="vertical" className="h-4" />
        </header>
        <div className="flex-1 bg-background p-6">{children}</div>
      </SidebarInset>
      <ReauthDialog />
    </SidebarProvider>
  );
}
