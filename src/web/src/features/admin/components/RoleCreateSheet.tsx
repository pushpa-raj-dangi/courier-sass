"use client";

import { useState } from "react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Sheet, SheetContent, SheetHeader, SheetTitle } from "@/components/ui/sheet";
import { Spinner } from "@/components/ui/spinner";
import { usePermissionCatalog } from "@/features/admin/hooks/usePermissionCatalog";
import { useCreateRole } from "@/features/admin/hooks/useCreateRole";
import { useAdminPanelStore } from "@/features/admin/store/adminPanelStore";
import { adminErrorMessage } from "@/features/admin/lib/adminErrorMessage";

export function RoleCreateSheet() {
  const panel = useAdminPanelStore((s) => s.panel);
  const close = useAdminPanelStore((s) => s.close);
  const openDetail = useAdminPanelStore((s) => s.openRoleDetail);
  const isOpen = panel.mode === "role-create";

  const { data: groups } = usePermissionCatalog();
  const createRole = useCreateRole();
  const [name, setName] = useState("");
  const [selected, setSelected] = useState<Set<string>>(new Set());
  const [serverError, setServerError] = useState<string | null>(null);

  function toggle(key: string) {
    setSelected((prev) => {
      const next = new Set(prev);
      if (next.has(key)) next.delete(key); else next.add(key);
      return next;
    });
  }

  function reset() {
    setName("");
    setSelected(new Set());
    setServerError(null);
  }

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    setServerError(null);
    try {
      const created = await createRole.mutateAsync({ name, permissionKeys: Array.from(selected) });
      reset();
      openDetail(created.id);
    } catch (error) {
      setServerError(adminErrorMessage(error));
    }
  }

  return (
    <Sheet open={isOpen} onOpenChange={(open) => { if (!open) { reset(); close(); } }}>
      <SheetContent className="w-full overflow-y-auto sm:max-w-xl">
        <SheetHeader>
          <SheetTitle>New Role</SheetTitle>
        </SheetHeader>
        <form onSubmit={handleSubmit} className="mt-6 space-y-4" noValidate>
          <div className="space-y-2">
            <Label htmlFor="role-name">Name</Label>
            <Input id="role-name" value={name} onChange={(e) => setName(e.target.value)} required />
          </div>
          <div className="space-y-3">
            <Label>Permissions</Label>
            {groups?.map((group) => (
              <fieldset key={group.group} className="rounded-md border border-border p-3">
                <legend className="px-1 text-sm font-medium capitalize text-foreground">{group.group}</legend>
                <div className="grid grid-cols-1 gap-1.5 sm:grid-cols-2">
                  {group.permissions.map((p) => (
                    <label key={p.key} className="flex items-center gap-2 text-sm text-muted-foreground">
                      <input type="checkbox" checked={selected.has(p.key)} onChange={() => toggle(p.key)} />
                      {p.description}
                    </label>
                  ))}
                </div>
              </fieldset>
            ))}
          </div>
          {serverError && <p className="text-sm text-destructive">{serverError}</p>}
          <Button type="submit" disabled={createRole.isPending || name.length === 0}>
            {createRole.isPending ? <Spinner className="h-4 w-4" /> : "Create Role"}
          </Button>
        </form>
      </SheetContent>
    </Sheet>
  );
}
