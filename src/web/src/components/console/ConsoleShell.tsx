"use client";

import type { ReactNode } from "react";
import { SidebarInset, SidebarProvider, SidebarTrigger } from "@/components/ui/sidebar";
import { ConsoleBreadcrumb } from "./ConsoleBreadcrumb";
import { ConsoleSidebar } from "./ConsoleSidebar";
import { DualDate } from "./DualDate";
import { NotificationsButton } from "./NotificationsButton";
import type { ConsoleBrand, ConsoleNavGroup } from "./types";

export interface ConsoleShellProps {
  brand: ConsoleBrand;
  scope?: ReactNode;
  /** Already permission-filtered by the calling surface. */
  nav: ConsoleNavGroup[];
  navLoading?: boolean;
  headerStart?: ReactNode;
  headerEnd?: ReactNode;
  banners?: ReactNode;
  children: ReactNode;
}

// The one shell for the staff, merchant and platform consoles (F15 R1). Surfaces differ only in
// what they pass in; the frame, rail and header are identical so they cannot drift apart.
export function ConsoleShell({ brand, scope, nav, navLoading, headerStart, headerEnd, banners, children }: ConsoleShellProps) {
  return (
    <SidebarProvider>
      <ConsoleSidebar brand={brand} scope={scope} nav={nav} navLoading={navLoading} />
      <SidebarInset>
        {banners}
        <header className="sticky top-0 z-10 flex h-16 shrink-0 items-center gap-3 border-b border-border bg-card px-4 sm:px-6">
          <SidebarTrigger className="h-9 w-9 rounded-lg border border-input" />
          <ConsoleBreadcrumb nav={nav} />
          {headerStart}
          <div className="ml-auto flex items-center gap-2 sm:gap-3">
            <DualDate />
            <NotificationsButton />
            {headerEnd}
          </div>
        </header>
        <div className="min-w-0 flex-1 bg-background p-4 sm:p-6 lg:p-8">{children}</div>
      </SidebarInset>
    </SidebarProvider>
  );
}
