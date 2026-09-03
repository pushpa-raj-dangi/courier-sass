"use client";

import { useQuery } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";
import type { MerchantStaffMember } from "@/features/merchant-staff/types";

export function useMerchantStaff() {
  return useQuery({
    queryKey: ["merchant-staff"],
    queryFn: () => clientFetch<MerchantStaffMember[]>("/api/merchant/staff"),
  });
}
