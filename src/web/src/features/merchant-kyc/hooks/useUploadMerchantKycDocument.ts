"use client";

import { useMutation, useQueryClient } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";
import type { MerchantKycDocument, KycDocumentType } from "@/features/merchant-kyc/types";

export function useUploadMerchantKycDocument() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ documentType, file }: { documentType: KycDocumentType; file: File }) =>
      clientFetch<MerchantKycDocument>(`/api/merchant/kyc-documents?documentType=${documentType}`, {
        method: "POST",
        headers: { "Content-Type": file.type || "application/octet-stream" },
        body: file,
      }),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["merchant-kyc-documents"] });
    },
  });
}
