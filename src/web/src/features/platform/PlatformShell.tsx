"use client";

import type { ReactNode } from "react";
import { ShieldCheck } from "lucide-react";
import { ConsoleShell } from "@/components/console/ConsoleShell";
import { ThemeToggle } from "@/components/layout/ThemeToggle";
import { Separator } from "@/components/ui/separator";
import { PLATFORM_NAV } from "./platformNav";
import { PlatformUserMenu } from "./PlatformUserMenu";

export function PlatformShell({ children }: { children: ReactNode }) {
  return (
    <ConsoleShell
      brand={{ icon: ShieldCheck, name: "Platform Console", subtitle: "SaaS operator" }}
      nav={PLATFORM_NAV}
      headerEnd={
        <>
          <ThemeToggle />
          <Separator orientation="vertical" className="h-6" />
          <PlatformUserMenu />
        </>
      }
    >
      {children}
    </ConsoleShell>
  );
}
