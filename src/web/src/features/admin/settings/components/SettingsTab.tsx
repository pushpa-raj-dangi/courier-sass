"use client";

import { useState } from "react";
import { Tabs, TabsContent, TabsList, TabsTrigger } from "@/components/ui/tabs";
import { usePermission } from "@/features/auth/hooks/usePermission";
import { OrganizationProfileForm } from "@/features/admin/settings/components/OrganizationProfileForm";
import { SecurityPolicyForm } from "@/features/admin/settings/components/SecurityPolicyForm";
import { OperationalDefaultsForm } from "@/features/admin/settings/components/OperationalDefaultsForm";
import { NotificationTemplatesPanel } from "@/features/admin/settings/notification-templates/components/NotificationTemplatesPanel";

// Nested tabs grow additively, same convention as the outer Admin page's TABS array — a later
// task appends its own TabsTrigger/TabsContent pair here without restructuring this file.
const SUB_TABS = [
  { key: "profile", label: "Profile", permission: "org.settings.manage" },
  { key: "security", label: "Security", permission: "org.settings.manage" },
  { key: "defaults", label: "Defaults", permission: "org.settings.manage" },
  { key: "notification-templates", label: "Notification Templates", permission: "notification_template.manage" },
] as const;

type SubTabKey = (typeof SUB_TABS)[number]["key"];

export function SettingsTab() {
  const canManageSettings = usePermission("org.settings.manage");
  const canManageTemplates = usePermission("notification_template.manage");
  const permitted: Record<SubTabKey, boolean> = {
    profile: canManageSettings,
    security: canManageSettings,
    defaults: canManageSettings,
    "notification-templates": canManageTemplates,
  };
  const visibleSubTabs = SUB_TABS.filter((t) => permitted[t.key]);
  const [requestedTab, setRequestedTab] = useState<SubTabKey | null>(null);
  const activeTab = requestedTab && permitted[requestedTab] ? requestedTab : (visibleSubTabs[0]?.key ?? null);

  if (!activeTab) {
    return null;
  }

  return (
    <Tabs value={activeTab} onValueChange={(v) => setRequestedTab(v as SubTabKey)}>
      <TabsList>
        {visibleSubTabs.map((tab) => (
          <TabsTrigger key={tab.key} value={tab.key}>{tab.label}</TabsTrigger>
        ))}
      </TabsList>
      <TabsContent value="profile"><OrganizationProfileForm /></TabsContent>
      <TabsContent value="security"><SecurityPolicyForm /></TabsContent>
      <TabsContent value="defaults"><OperationalDefaultsForm /></TabsContent>
      <TabsContent value="notification-templates"><NotificationTemplatesPanel /></TabsContent>
    </Tabs>
  );
}
