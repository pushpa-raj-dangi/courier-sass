import type { ReactNode } from "react";
import { AppShell } from "@/components/layout/AppShell";
import { BranchCreateSheet } from "@/features/branches/components/BranchCreateSheet";
import { BranchDetailSheet } from "@/features/branches/components/BranchDetailSheet";
import { ShipmentCreateSheet } from "@/features/shipments/components/ShipmentCreateSheet";
import { ShipmentDetailSheet } from "@/features/shipments/components/ShipmentDetailSheet";
import { ManifestCreateSheet } from "@/features/manifests/components/ManifestCreateSheet";
import { ManifestDetailSheet } from "@/features/manifests/components/ManifestDetailSheet";
import { RiderCreateSheet } from "@/features/riders/components/RiderCreateSheet";

export default function StaffLayout({ children }: { children: ReactNode }) {
  return (
    <AppShell>
      {children}
      <BranchCreateSheet />
      <BranchDetailSheet />
      <ShipmentCreateSheet />
      <ShipmentDetailSheet />
      <ManifestCreateSheet />
      <ManifestDetailSheet />
      <RiderCreateSheet />
    </AppShell>
  );
}
