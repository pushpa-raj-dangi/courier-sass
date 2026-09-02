"use client";

import { Button } from "@/components/ui/button";
import { Badge } from "@/components/ui/badge";
import { DataTable, type ColumnDef } from "@/components/data-table/DataTable";
import { useRoles } from "@/features/admin/hooks/useRoles";
import { useAdminPanelStore } from "@/features/admin/store/adminPanelStore";
import { usePermission } from "@/features/auth/hooks/usePermission";
import type { Role } from "@/features/admin/types";

export function RolesTab() {
  const { data: roles, isLoading } = useRoles();
  const openCreate = useAdminPanelStore((s) => s.openRoleCreate);
  const openDetail = useAdminPanelStore((s) => s.openRoleDetail);
  const canManage = usePermission("role.manage");

  const columns: ColumnDef<Role>[] = [
    {
      key: "name",
      header: "Name",
      render: (r) => (
        <button type="button" onClick={() => openDetail(r.id)} className="font-medium text-foreground hover:underline">
          {r.name}
        </button>
      ),
    },
    { key: "permissionCount", header: "Permissions", render: (r) => r.permissionKeys.length },
    {
      key: "kind",
      header: "Kind",
      render: (r) => <Badge variant={r.isSystemDefault ? "secondary" : "outline"}>{r.isSystemDefault ? "System" : "Custom"}</Badge>,
    },
  ];

  return (
    <div className="space-y-4">
      {canManage && (
        <div className="flex justify-end">
          <Button type="button" onClick={openCreate}>New Role</Button>
        </div>
      )}
      <DataTable
        columns={columns}
        data={roles ? { items: roles, page: 1, pageSize: roles.length || 1, totalCount: roles.length, totalPages: 1 } : undefined}
        isLoading={isLoading}
        emptyTitle="No roles"
        emptyDescription="Custom roles created for this organization will show up here."
        onPageChange={() => {}}
        rowKey={(r) => r.id}
      />
    </div>
  );
}
