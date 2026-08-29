"use client";

import { useRouter } from "next/navigation";
import { Package, LogOut } from "lucide-react";
import { useSession } from "@/features/auth/hooks/useSession";
import { BranchSwitcher } from "@/components/layout/BranchSwitcher";
import { Avatar, AvatarFallback } from "@/components/ui/avatar";
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuTrigger,
} from "@/components/ui/dropdown-menu";

function initials(fullName: string): string {
  return fullName
    .split(" ")
    .map((part) => part[0])
    .slice(0, 2)
    .join("")
    .toUpperCase();
}

export function TopNav() {
  const router = useRouter();
  const { data: session } = useSession();

  async function handleLogout() {
    await fetch("/api/auth/logout", { method: "POST" });
    router.push("/login");
  }

  return (
    <header className="flex h-14 items-center justify-between border-b border-border bg-card px-4">
      <div className="flex items-center gap-2.5">
        <span className="flex h-7 w-7 items-center justify-center rounded-md bg-primary">
          <Package className="h-4 w-4 text-primary-foreground" strokeWidth={2} />
        </span>
        <span className="font-display text-lg font-semibold tracking-tight">Courier Platform</span>
      </div>
      <div className="flex items-center gap-4">
        <BranchSwitcher />
        {session && (
          <DropdownMenu>
            <DropdownMenuTrigger asChild>
              <button aria-label="User menu" className="rounded-full focus-visible:outline-none focus-visible:ring-1 focus-visible:ring-ring">
                <Avatar className="ring-1 ring-border">
                  <AvatarFallback className="bg-secondary text-secondary-foreground">
                    {initials(session.fullName)}
                  </AvatarFallback>
                </Avatar>
              </button>
            </DropdownMenuTrigger>
            <DropdownMenuContent align="end">
              <DropdownMenuItem onSelect={handleLogout} className="gap-2">
                <LogOut className="h-4 w-4" strokeWidth={2} />
                Log out
              </DropdownMenuItem>
            </DropdownMenuContent>
          </DropdownMenu>
        )}
      </div>
    </header>
  );
}
