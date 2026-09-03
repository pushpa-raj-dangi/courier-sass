import type { ReactNode } from "react";
import { MerchantAppShell } from "@/components/merchant-layout/MerchantAppShell";
import { MerchantShipmentCreateSheet } from "@/features/merchant-shipments/components/MerchantShipmentCreateSheet";
import { MerchantShipmentDetailSheet } from "@/features/merchant-shipments/components/MerchantShipmentDetailSheet";
import { MerchantPickupCreateSheet } from "@/features/merchant-pickups/components/MerchantPickupCreateSheet";

// Tasks 9-14 each mount their own Sheet component here, alongside {children} — same
// one-Sheet-added-per-phase pattern app/(staff)/layout.tsx shows across F1-F6.
export default function MerchantLayout({ children }: { children: ReactNode }) {
  return (
    <MerchantAppShell>
      {children}
      <MerchantShipmentCreateSheet />
      <MerchantShipmentDetailSheet />
      <MerchantPickupCreateSheet />
    </MerchantAppShell>
  );
}
