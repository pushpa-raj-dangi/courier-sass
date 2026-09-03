"use client";

import { useMutation, useQueryClient } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";
import type { MerchantStaffMember } from "@/features/merchant-staff/types";
import type { InviteMerchantStaffFormValues } from "@/features/merchant-staff/schema";

export function useInviteMerchantStaff() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (input: InviteMerchantStaffFormValues) =>
      clientFetch<MerchantStaffMember>("/api/merchant/staff", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(input),
      }),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["merchant-staff"] });
    },
  });
}
