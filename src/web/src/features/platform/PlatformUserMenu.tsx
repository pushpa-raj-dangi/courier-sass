"use client";

import { useRouter } from "next/navigation";
import { ChevronDown, LogOut, ShieldCheck } from "lucide-react";
import { Button } from "@/components/ui/button";
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuLabel,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from "@/components/ui/dropdown-menu";

export function PlatformUserMenu() {
  const router = useRouter();

  async function handleSignOut() {
    await fetch("/api/platform-auth/logout", { method: "POST" });
    router.push("/platform-login");
  }

  return (
    <DropdownMenu>
      <DropdownMenuTrigger asChild>
        <Button variant="ghost" className="gap-2 rounded-full px-2" aria-label="Platform admin">
          <ShieldCheck className="h-4 w-4" strokeWidth={2} aria-hidden="true" />
          <span className="hidden text-sm sm:inline" aria-hidden="true">Platform admin</span>
          <ChevronDown className="h-3.5 w-3.5 text-muted-foreground" strokeWidth={2} aria-hidden="true" />
        </Button>
      </DropdownMenuTrigger>
      <DropdownMenuContent align="end" className="min-w-48">
        <DropdownMenuLabel>Platform admin</DropdownMenuLabel>
        <DropdownMenuSeparator />
        <DropdownMenuItem onSelect={handleSignOut}>
          <LogOut className="mr-2 h-4 w-4" strokeWidth={2} aria-hidden="true" />
          Sign out
        </DropdownMenuItem>
      </DropdownMenuContent>
    </DropdownMenu>
  );
}
