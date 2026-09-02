"use client";

import { useState } from "react";
import { Button } from "@/components/ui/button";
import { Badge } from "@/components/ui/badge";
import { DataTable, type ColumnDef } from "@/components/data-table/DataTable";
import { useAdminUsers } from "@/features/admin/hooks/useAdminUsers";
import { useAdminPanelStore } from "@/features/admin/store/adminPanelStore";
import { usePermission } from "@/features/auth/hooks/usePermission";
import type { AdminUser } from "@/features/admin/types";

export function UsersTab() {
  const [page, setPage] = useState(1);
  const pageSize = 20;
  const { data, isLoading } = useAdminUsers({ page, pageSize });
  const openDetail = useAdminPanelStore((s) => s.openUserDetail);
  const openInvite = useAdminPanelStore((s) => s.openUserInvite);
  const canInvite = usePermission("user.create");

  const columns: ColumnDef<AdminUser>[] = [
    {
      key: "email",
      header: "Email",
      render: (u) => (
        <button type="button" onClick={() => openDetail(u.id)} className="font-medium text-foreground hover:underline">
          {u.email}
        </button>
      ),
    },
    { key: "fullName", header: "Name", render: (u) => u.fullName },
    { key: "role", header: "Role", render: (u) => u.roleName ?? "—" },
    { key: "branches", header: "Branches", render: (u) => u.branchCount },
    {
      key: "status",
      header: "Status",
      render: (u) => <Badge variant={u.isActive ? "default" : "secondary"}>{u.isActive ? "Active" : "Inactive"}</Badge>,
    },
  ];

  return (
    <div className="space-y-4">
      {canInvite && (
        <div className="flex justify-end">
          <Button type="button" onClick={openInvite}>Invite User</Button>
        </div>
      )}
      <DataTable
        columns={columns}
        data={data}
        isLoading={isLoading}
        emptyTitle="No users"
        emptyDescription="Users invited into this organization will show up here."
        onPageChange={setPage}
        rowKey={(u) => u.id}
      />
    </div>
  );
}
