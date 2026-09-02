"use client";

import { useState } from "react";
import { Button } from "@/components/ui/button";
import { Badge } from "@/components/ui/badge";
import { Sheet, SheetContent, SheetHeader, SheetTitle } from "@/components/ui/sheet";
import { Spinner } from "@/components/ui/spinner";
import { useAdminUsers } from "@/features/admin/hooks/useAdminUsers";
import { useSetUserActive } from "@/features/admin/hooks/useSetUserActive";
import { useAdminPanelStore } from "@/features/admin/store/adminPanelStore";
import { adminErrorMessage } from "@/features/admin/lib/adminErrorMessage";
import { usePermission } from "@/features/auth/hooks/usePermission";
import { UserBranchGrants } from "@/features/admin/components/UserBranchGrants";

export function UserDetailSheet() {
  const panel = useAdminPanelStore((s) => s.panel);
  const close = useAdminPanelStore((s) => s.close);
  const isOpen = panel.mode === "user-detail";
  const id = panel.mode === "user-detail" ? panel.id : "";

  // Reuses the already-loaded list page rather than a dedicated GET /users/{id} — no such
  // endpoint exists (Task 7 only paginated the list), and the tab is almost always open with the
  // list already fetched when a row is clicked.
  const { data } = useAdminUsers({ pageSize: 1000 });
  const user = data?.items.find((u) => u.id === id);
  const setActive = useSetUserActive(id);
  const canUpdate = usePermission("user.update");
  const [serverError, setServerError] = useState<string | null>(null);

  async function handleToggleActive() {
    setServerError(null);
    try {
      await setActive.mutateAsync(!user!.isActive);
    } catch (error) {
      setServerError(adminErrorMessage(error));
    }
  }

  return (
    <Sheet open={isOpen} onOpenChange={(open) => { if (!open) close(); }}>
      <SheetContent className="w-full overflow-y-auto sm:max-w-xl">
        <SheetHeader>
          <SheetTitle className="flex items-center gap-2">
            {user?.fullName ?? "User"}
            {user && <Badge variant={user.isActive ? "default" : "secondary"}>{user.isActive ? "Active" : "Inactive"}</Badge>}
          </SheetTitle>
        </SheetHeader>
        {!user && (
          <div className="flex items-center justify-center p-12">
            <Spinner className="h-6 w-6" />
          </div>
        )}
        {user && (
          <div className="mt-6 space-y-6">
            <div className="grid grid-cols-2 gap-4 text-sm">
              <div>
                <p className="text-xs text-muted-foreground">Email</p>
                <p className="font-medium text-foreground">{user.email}</p>
              </div>
              <div>
                <p className="text-xs text-muted-foreground">Role</p>
                <p className="font-medium text-foreground">{user.roleName ?? "—"}</p>
              </div>
            </div>
            {serverError && <p className="text-sm text-destructive">{serverError}</p>}
            {canUpdate && (
              <Button type="button" variant={user.isActive ? "destructive" : "default"} disabled={setActive.isPending} onClick={handleToggleActive}>
                {setActive.isPending ? <Spinner className="h-4 w-4" /> : user.isActive ? "Deactivate" : "Reactivate"}
              </Button>
            )}
            <UserBranchGrants userId={user.id} />
          </div>
        )}
      </SheetContent>
    </Sheet>
  );
}
