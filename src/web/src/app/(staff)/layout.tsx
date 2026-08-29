import type { ReactNode } from "react";
import { AppShell } from "@/components/layout/AppShell";
import { BranchCreateSheet } from "@/features/branches/components/BranchCreateSheet";
import { BranchDetailSheet } from "@/features/branches/components/BranchDetailSheet";
import { ShipmentCreateSheet } from "@/features/shipments/components/ShipmentCreateSheet";
import { ShipmentDetailSheet } from "@/features/shipments/components/ShipmentDetailSheet";

export default function StaffLayout({ children }: { children: ReactNode }) {
  return (
    <AppShell>
      {children}
      <BranchCreateSheet />
      <BranchDetailSheet />
      <ShipmentCreateSheet />
      <ShipmentDetailSheet />
    </AppShell>
  );
}
