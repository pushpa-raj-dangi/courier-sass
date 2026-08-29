"use client";

import { useRouter } from "next/navigation";
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
      <span className="font-display text-lg font-semibold tracking-tight">Courier Platform</span>
      <div className="flex items-center gap-4">
        <BranchSwitcher />
        {session && (
          <DropdownMenu>
            <DropdownMenuTrigger asChild>
              <button aria-label="User menu">
                <Avatar>
                  <AvatarFallback>{initials(session.fullName)}</AvatarFallback>
                </Avatar>
              </button>
            </DropdownMenuTrigger>
            <DropdownMenuContent align="end">
              <DropdownMenuItem onSelect={handleLogout}>Log out</DropdownMenuItem>
            </DropdownMenuContent>
          </DropdownMenu>
        )}
      </div>
    </header>
  );
}
