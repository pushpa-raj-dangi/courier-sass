"use client";

import { useState } from "react";
import { RolesTab } from "@/features/admin/components/RolesTab";
import { RoleCreateSheet } from "@/features/admin/components/RoleCreateSheet";
import { RoleDetailSheet } from "@/features/admin/components/RoleDetailSheet";

// Plain button-row tab switcher, matching /cod's own precedent (no Tabs primitive in this repo).
// Task 17 appends the "users" tab entry, its `activeTab === "users"` branch, and its own Sheets
// at the bottom — this file grows additively, never restructured, the same way CodPage did.
const TABS = [
  { key: "roles", label: "Roles" },
] as const;

type TabKey = (typeof TABS)[number]["key"];

export default function AdminPage() {
  const [activeTab, setActiveTab] = useState<TabKey>("roles");

  return (
    <div className="space-y-4">
      <h1 className="font-display text-xl font-semibold">Admin</h1>
      <div className="flex gap-1 border-b border-border" role="tablist">
        {TABS.map((tab) => (
          <button
            key={tab.key}
            type="button"
            role="tab"
            aria-selected={activeTab === tab.key}
            onClick={() => setActiveTab(tab.key)}
            className={`px-3 py-2 text-sm font-medium transition-colors ${
              activeTab === tab.key
                ? "border-b-2 border-foreground text-foreground"
                : "text-muted-foreground hover:text-foreground"
            }`}
          >
            {tab.label}
          </button>
        ))}
      </div>

      {activeTab === "roles" && <RolesTab />}

      <RoleCreateSheet />
      <RoleDetailSheet />
    </div>
  );
}
