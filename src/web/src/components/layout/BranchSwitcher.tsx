"use client";

import { useEffect } from "react";
import { Building2, ChevronDown } from "lucide-react";
import { useTranslations } from "next-intl";
import { useSession } from "@/features/auth/hooks/useSession";
import { useUiStore } from "@/lib/store/uiStore";
import { useBranchPanelStore } from "@/features/branches/store/branchPanelStore";
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
  const t = useTranslations("console");
  const { data: session } = useSession();
  const activeBranchId = useUiStore((s) => s.activeBranchId);
  const setActiveBranchId = useUiStore((s) => s.setActiveBranchId);
  const openDetail = useBranchPanelStore((s) => s.openDetail);

  useEffect(() => {
    if (session && session.branches.length > 0 && !activeBranchId) {
      setActiveBranchId(session.branches[0].branchId);
    }
  }, [session, activeBranchId, setActiveBranchId]);

  if (!session || session.branches.length < 2) {
    return null;
  }

  const active = session.branches.find((b) => b.branchId === activeBranchId);

  function handleSelect(branchId: string) {
    setActiveBranchId(branchId);
    openDetail(branchId);
  }

  return (
    <DropdownMenu>
      <DropdownMenuTrigger asChild>
        <Button
          variant="ghost"
          className="h-auto min-h-12 w-full justify-start gap-2.5 rounded-lg border border-sidebar-border bg-sidebar-accent/60 px-3 py-2 text-left text-sidebar-accent-foreground hover:bg-sidebar-accent hover:text-sidebar-accent-foreground focus-visible:ring-sidebar-ring"
        >
          <Building2 className="h-4 w-4 shrink-0 text-sidebar-primary" strokeWidth={2} aria-hidden="true" />
          <span className="flex min-w-0 flex-1 flex-col">
            <span className="text-2xs font-normal text-sidebar-foreground">{t("branchScope")}</span>
            <span className="truncate text-sm font-medium">{active?.branchName ?? t("selectBranch")}</span>
          </span>
          <ChevronDown className="h-3.5 w-3.5 shrink-0 text-sidebar-muted" strokeWidth={2} aria-hidden="true" />
        </Button>
      </DropdownMenuTrigger>
      <DropdownMenuContent>
        {session.branches.map((branch) => (
          <DropdownMenuItem key={branch.branchId} onSelect={() => handleSelect(branch.branchId)}>
            {branch.branchName}
          </DropdownMenuItem>
        ))}
      </DropdownMenuContent>
    </DropdownMenu>
  );
}
