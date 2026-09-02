"use client";

import { useState } from "react";
import { Tabs, TabsContent, TabsList, TabsTrigger } from "@/components/ui/tabs";
import { RolesTab } from "@/features/admin/components/RolesTab";
import { RoleCreateSheet } from "@/features/admin/components/RoleCreateSheet";
import { RoleDetailSheet } from "@/features/admin/components/RoleDetailSheet";
import { UsersTab } from "@/features/admin/components/UsersTab";
import { UserInviteSheet } from "@/features/admin/components/UserInviteSheet";
import { UserDetailSheet } from "@/features/admin/components/UserDetailSheet";
import { usePermission } from "@/features/auth/hooks/usePermission";

// This file grows additively — new tabs and their Sheets appended, never restructured.
const TABS = [
  { key: "roles", label: "Roles", permission: "role.manage" },
  { key: "users", label: "Users", permission: "user.read" },
] as const;

type TabKey = (typeof TABS)[number]["key"];

export default function AdminPage() {
  const canManageRoles = usePermission("role.manage");
  const canReadUsers = usePermission("user.read");
  const permitted = { roles: canManageRoles, users: canReadUsers };

  // The sidebar's own Admin link is visible to anyone with role.manage OR user.read (nav-main.tsx)
  // — a user with only one of the two must never see a tab (or its data fetch) requiring the
  // other, since the backend's own RequirePermission on each endpoint is stricter than that OR.
  const visibleTabs = TABS.filter((tab) => permitted[tab.key]);
  const [requestedTab, setRequestedTab] = useState<TabKey | null>(null);
  const activeTab = requestedTab && permitted[requestedTab] ? requestedTab : (visibleTabs[0]?.key ?? null);

  return (
    <div className="space-y-4">
      <h1 className="font-display text-xl font-semibold">Admin</h1>
      {activeTab && (
        <Tabs value={activeTab} onValueChange={(v) => setRequestedTab(v as TabKey)}>
          <TabsList>
            {visibleTabs.map((tab) => (
              <TabsTrigger key={tab.key} value={tab.key}>{tab.label}</TabsTrigger>
            ))}
          </TabsList>
          <TabsContent value="roles"><RolesTab /></TabsContent>
          <TabsContent value="users"><UsersTab /></TabsContent>
        </Tabs>
      )}

      <RoleCreateSheet />
      <RoleDetailSheet />
      <UserInviteSheet />
      <UserDetailSheet />
    </div>
  );
}
