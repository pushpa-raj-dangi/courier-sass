"use client";

import { useRouter } from "next/navigation";
import { ChevronDown, LogOut } from "lucide-react";
import { useSession } from "@/features/auth/hooks/useSession";
import { Avatar, AvatarFallback } from "@/components/ui/avatar";
import { Skeleton } from "@/components/ui/skeleton";
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuLabel,
  DropdownMenuSeparator,
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

// No avatar image source exists in this app yet, and the menu only offers what this app can
// actually do today — dead menu items (Account/Billing/Notifications) were dropped in favor of
// a shorter, honest one.
export function UserMenu() {
  const router = useRouter();
  const { data: session, isLoading } = useSession();

  async function handleLogout() {
    await fetch("/api/auth/logout", { method: "POST" });
    router.push("/login");
  }

  if (isLoading) {
    return (
      <div className="flex items-center gap-2 py-1 pl-1 pr-2" data-testid="user-menu-skeleton">
        <Skeleton className="h-8 w-8 shrink-0 rounded-full" />
        <Skeleton className="hidden h-4 w-24 sm:block" />
      </div>
    );
  }

  if (!session) {
    return null;
  }

  return (
    <DropdownMenu>
      <DropdownMenuTrigger asChild>
        <button
          type="button"
          className="flex items-center gap-2 rounded-full py-1 pl-1 pr-2 text-sm transition-colors hover:bg-accent focus-visible:outline-none focus-visible:ring-1 focus-visible:ring-ring data-[state=open]:bg-accent"
        >
          <Avatar className="h-8 w-8 ring-1 ring-border">
            <AvatarFallback className="bg-secondary text-secondary-foreground">
              {initials(session.fullName)}
            </AvatarFallback>
          </Avatar>
          <span className="hidden font-medium text-foreground sm:inline">{session.fullName}</span>
          <ChevronDown className="h-3.5 w-3.5 text-muted-foreground" strokeWidth={2} />
        </button>
      </DropdownMenuTrigger>
      <DropdownMenuContent className="min-w-56 rounded-lg" side="bottom" align="end" sideOffset={4}>
        <DropdownMenuLabel className="p-0 font-normal">
          <div className="flex items-center gap-2 px-1 py-1.5 text-left text-sm">
            <Avatar className="h-8 w-8 ring-1 ring-border">
              <AvatarFallback className="bg-secondary text-secondary-foreground">
                {initials(session.fullName)}
              </AvatarFallback>
            </Avatar>
            <div className="grid flex-1 text-left text-sm leading-tight">
              <span className="truncate font-semibold">{session.fullName}</span>
              <span className="truncate text-xs">{session.email}</span>
            </div>
          </div>
        </DropdownMenuLabel>
        <DropdownMenuSeparator />
        <DropdownMenuItem onSelect={handleLogout} className="gap-2">
          <LogOut className="h-4 w-4" strokeWidth={2} />
          Log out
        </DropdownMenuItem>
      </DropdownMenuContent>
    </DropdownMenu>
  );
}
