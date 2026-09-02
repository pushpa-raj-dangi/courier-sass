"use client";

import { useEffect, useState } from "react";
import { Button } from "@/components/ui/button";
import { Badge } from "@/components/ui/badge";
import { Sheet, SheetContent, SheetHeader, SheetTitle } from "@/components/ui/sheet";
import { Spinner } from "@/components/ui/spinner";
import { usePermissionCatalog } from "@/features/admin/hooks/usePermissionCatalog";
import { useRoles } from "@/features/admin/hooks/useRoles";
import { useUpdateRole } from "@/features/admin/hooks/useUpdateRole";
import { useDeleteRole } from "@/features/admin/hooks/useDeleteRole";
import { useAdminPanelStore } from "@/features/admin/store/adminPanelStore";
import { adminErrorMessage } from "@/features/admin/lib/adminErrorMessage";

export function RoleDetailSheet() {
  const panel = useAdminPanelStore((s) => s.panel);
  const close = useAdminPanelStore((s) => s.close);
  const isOpen = panel.mode === "role-detail";
  const id = panel.mode === "role-detail" ? panel.id : "";

  const { data: roles } = useRoles();
  const role = roles?.find((r) => r.id === id);
  const { data: groups } = usePermissionCatalog();
  const updateRole = useUpdateRole(id);
  const deleteRole = useDeleteRole();
  const [selected, setSelected] = useState<Set<string>>(new Set());
  const [serverError, setServerError] = useState<string | null>(null);

  useEffect(() => {
    if (role) setSelected(new Set(role.permissionKeys));
  }, [role]);

  function toggle(key: string) {
    if (role?.isSystemDefault) return;
    setSelected((prev) => {
      const next = new Set(prev);
      if (next.has(key)) next.delete(key); else next.add(key);
      return next;
    });
  }

  async function handleSave() {
    setServerError(null);
    try {
      await updateRole.mutateAsync({ permissionKeys: Array.from(selected) });
    } catch (error) {
      setServerError(adminErrorMessage(error));
    }
  }

  async function handleDelete() {
    setServerError(null);
    try {
      await deleteRole.mutateAsync(id);
      close();
    } catch (error) {
      setServerError(adminErrorMessage(error));
    }
  }

  return (
    <Sheet open={isOpen} onOpenChange={(open) => { if (!open) close(); }}>
      <SheetContent className="w-full overflow-y-auto sm:max-w-xl">
        <SheetHeader>
          <SheetTitle className="flex items-center gap-2">
            {role?.name ?? "Role"}
            {role && <Badge variant={role.isSystemDefault ? "secondary" : "outline"}>{role.isSystemDefault ? "System" : "Custom"}</Badge>}
          </SheetTitle>
        </SheetHeader>
        {!role && (
          <div className="flex items-center justify-center p-12">
            <Spinner className="h-6 w-6" />
          </div>
        )}
        {role && (
          <div className="mt-6 space-y-4">
            {role.isSystemDefault && (
              <p className="text-sm text-muted-foreground">System-default roles cannot be edited or deleted.</p>
            )}
            <div className="space-y-3">
              {groups?.map((group) => (
                <fieldset key={group.group} className="rounded-md border border-border p-3">
                  <legend className="px-1 text-sm font-medium capitalize text-foreground">{group.group}</legend>
                  <div className="grid grid-cols-1 gap-1.5 sm:grid-cols-2">
                    {group.permissions.map((p) => (
                      <label key={p.key} className="flex items-center gap-2 text-sm text-muted-foreground">
                        <input
                          type="checkbox"
                          checked={selected.has(p.key)}
                          disabled={role.isSystemDefault}
                          onChange={() => toggle(p.key)}
                        />
                        {p.description}
                      </label>
                    ))}
                  </div>
                </fieldset>
              ))}
            </div>
            {serverError && <p className="text-sm text-destructive">{serverError}</p>}
            {!role.isSystemDefault && (
              <div className="flex gap-2">
                <Button type="button" onClick={handleSave} disabled={updateRole.isPending}>
                  {updateRole.isPending ? <Spinner className="h-4 w-4" /> : "Save Changes"}
                </Button>
                <Button type="button" variant="destructive" onClick={handleDelete} disabled={deleteRole.isPending}>
                  Delete Role
                </Button>
              </div>
            )}
          </div>
        )}
      </SheetContent>
    </Sheet>
  );
}
