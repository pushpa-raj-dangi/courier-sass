"use client";

import { useEffect } from "react";
import { Building2, ChevronDown } from "lucide-react";
import { useSession } from "@/features/auth/hooks/useSession";
import { useUiStore } from "@/lib/store/uiStore";
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuTrigger,
} from "@/components/ui/dropdown-menu";
import { Button } from "@/components/ui/button";

// Renders nothing for a single-branch user (Phase F2's exit criteria) — a switcher implies a
// choice, and a user with exactly one branch (or none) has none to make.
export function BranchSwitcher() {
  const { data: session } = useSession();
  const activeBranchId = useUiStore((s) => s.activeBranchId);
  const setActiveBranchId = useUiStore((s) => s.setActiveBranchId);

  useEffect(() => {
    if (session && session.branches.length > 0 && !activeBranchId) {
      setActiveBranchId(session.branches[0].branchId);
    }
  }, [session, activeBranchId, setActiveBranchId]);

  if (!session || session.branches.length < 2) {
    return null;
  }

  const active = session.branches.find((b) => b.branchId === activeBranchId);

  return (
    <DropdownMenu>
      <DropdownMenuTrigger asChild>
        <Button variant="outline" size="sm" className="gap-1.5">
          <Building2 className="h-3.5 w-3.5" strokeWidth={2} />
          {active?.branchName ?? "Select branch"}
          <ChevronDown className="h-3.5 w-3.5 text-muted-foreground" strokeWidth={2} />
        </Button>
      </DropdownMenuTrigger>
      <DropdownMenuContent>
        {session.branches.map((branch) => (
          <DropdownMenuItem key={branch.branchId} onSelect={() => setActiveBranchId(branch.branchId)}>
            {branch.branchName}
          </DropdownMenuItem>
        ))}
      </DropdownMenuContent>
    </DropdownMenu>
  );
}
