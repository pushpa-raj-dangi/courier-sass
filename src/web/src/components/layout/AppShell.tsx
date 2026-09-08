import type { ReactNode } from "react";
import { AppSidebar } from "@/components/app-sidebar";
import { HeaderSearch } from "@/components/layout/HeaderSearch";
import { ReauthDialog } from "@/components/layout/ReauthDialog";
import { ThemeToggle } from "@/components/layout/ThemeToggle";
import { UserMenu } from "@/components/layout/UserMenu";
import { SidebarInset, SidebarProvider, SidebarTrigger } from "@/components/ui/sidebar";
import { Separator } from "@/components/ui/separator";

export function AppShell({ children }: { children: ReactNode }) {
  return (
    <SidebarProvider>
      <AppSidebar />
      <SidebarInset>
        <header className="sticky top-0 z-10 flex h-16 shrink-0 items-center gap-3 border-b border-border bg-card px-4 sm:px-6">
          <SidebarTrigger className="h-9 w-9 rounded-lg border border-input" />
          <HeaderSearch />
          <div className="ml-auto flex items-center gap-3">
            <ThemeToggle />
            <Separator orientation="vertical" className="h-6" />
            <UserMenu />
          </div>
        </header>
        <div className="flex-1 bg-background p-6">{children}</div>
      </SidebarInset>
      <ReauthDialog />
    </SidebarProvider>
  );
}
