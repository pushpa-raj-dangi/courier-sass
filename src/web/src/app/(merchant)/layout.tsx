import type { ReactNode } from "react";
import { MerchantAppShell } from "@/components/merchant-layout/MerchantAppShell";

// Tasks 9-14 each mount their own Sheet component here, alongside {children} — same
// one-Sheet-added-per-phase pattern app/(staff)/layout.tsx shows across F1-F6.
export default function MerchantLayout({ children }: { children: ReactNode }) {
  return <MerchantAppShell>{children}</MerchantAppShell>;
}
