"use client";

import Link from "next/link";
import { usePathname } from "next/navigation";
import { ClipboardList, ScanLine, User } from "lucide-react";
import { cn } from "@/lib/utils";
import { OutboxStatusBar } from "./OutboxStatusBar";

const TABS = [
  { href: "/rider/tasks", label: "Run", icon: ClipboardList },
  { href: "/rider/scan", label: "Scan", icon: ScanLine },
  { href: "/rider/profile", label: "Profile", icon: User },
];

export function RiderShell({ children }: { children: React.ReactNode }) {
  const pathname = usePathname();

  return (
    <div className="flex min-h-dvh flex-col bg-background">
      <OutboxStatusBar />
      <main className="flex-1 overflow-y-auto pb-16">{children}</main>
      <nav className="fixed inset-x-0 bottom-0 flex border-t bg-background">
        {TABS.map((tab) => {
          const Icon = tab.icon;
          const active = pathname === tab.href;
          return (
            <Link
              key={tab.href}
              href={tab.href}
              aria-current={active ? "page" : undefined}
              className={cn(
                "flex min-h-16 flex-1 flex-col items-center justify-center gap-1 py-2 text-xs",
                active ? "font-semibold text-primary" : "text-muted-foreground"
              )}
            >
              <Icon className="h-6 w-6" aria-hidden="true" />
              {tab.label}
            </Link>
          );
        })}
      </nav>
    </div>
  );
}
