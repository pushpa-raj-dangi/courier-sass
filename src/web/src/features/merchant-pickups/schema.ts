import { z } from "zod";

export const createMerchantPickupSchema = z.object({
  shipmentIds: z.array(z.string()).min(1, "Select at least one shipment."),
  requestedForDate: z.string().min(1, "Requested date is required."),
  notes: z.string().optional(),
});

export type CreateMerchantPickupFormValues = z.infer<typeof createMerchantPickupSchema>;
