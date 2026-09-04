"use client";

import { useMutation, useQueryClient } from "@tanstack/react-query";
import { addOutboxItem } from "@/features/rider-app/sync/outboxDb";
import { drainOutbox } from "@/features/rider-app/sync/syncManager";

export interface CompleteDeliveryFields {
  shipmentId: string;
  recipientName: string;
  otpVerified: boolean;
  latitude: number | null;
  longitude: number | null;
  collectedCodAmount: number | null;
  signatureBlob: Blob | null;
  photoBlob: Blob | null;
}

export function useCompleteDelivery(riderId: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (input: CompleteDeliveryFields) => {
      await addOutboxItem({
        kind: "delivery-complete",
        riderId,
        shipmentId: input.shipmentId,
        recipientName: input.recipientName,
        otpVerified: input.otpVerified,
        latitude: input.latitude,
        longitude: input.longitude,
        collectedCodAmount: input.collectedCodAmount,
        signatureBlob: input.signatureBlob,
        photoBlob: input.photoBlob,
        signatureStorageKey: null,
        photoStorageKey: null,
      });
      void drainOutbox();
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["riders", "me", "tasks", "today"] });
    },
  });
}
