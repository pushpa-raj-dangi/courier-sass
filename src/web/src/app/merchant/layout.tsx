import type { ReactNode } from "react";
import { MerchantAppShell } from "@/components/merchant-layout/MerchantAppShell";
import { MerchantShipmentCreateSheet } from "@/features/merchant-shipments/components/MerchantShipmentCreateSheet";
import { MerchantShipmentDetailSheet } from "@/features/merchant-shipments/components/MerchantShipmentDetailSheet";
import { MerchantPickupCreateSheet } from "@/features/merchant-pickups/components/MerchantPickupCreateSheet";
import { MerchantSettlementDetailSheet } from "@/features/merchant-cod/components/MerchantSettlementDetailSheet";
import { MerchantKycUploadSheet } from "@/features/merchant-kyc/components/MerchantKycUploadSheet";
import { MerchantStaffInviteSheet } from "@/features/merchant-staff/components/MerchantStaffInviteSheet";
import { MerchantApiKeyCreateSheet } from "@/features/merchant-api-keys/components/MerchantApiKeyCreateSheet";

// Tasks 9-14 each mount their own Sheet component here, alongside {children} — same
// one-Sheet-added-per-phase pattern app/(staff)/layout.tsx shows across F1-F6.
export default function MerchantLayout({ children }: { children: ReactNode }) {
  return (
    <MerchantAppShell>
      {children}
      <MerchantShipmentCreateSheet />
      <MerchantShipmentDetailSheet />
      <MerchantPickupCreateSheet />
      <MerchantSettlementDetailSheet />
      <MerchantKycUploadSheet />
      <MerchantStaffInviteSheet />
      <MerchantApiKeyCreateSheet />
    </MerchantAppShell>
  );
}
