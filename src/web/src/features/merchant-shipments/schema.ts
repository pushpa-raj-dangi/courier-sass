import { z } from "zod";

const itemSchema = z.object({
  description: z.string().min(1, "Description is required."),
  quantity: z.number({ error: "Quantity is required." }).positive("Quantity must be positive."),
  unitValue: z.number({ error: "Unit value is required." }).nonnegative("Unit value cannot be negative."),
});

// No merchantId field — unlike staff's createShipmentSchema, the backend forces MerchantId to
// the caller's own id for a merchant-JWT/API-key caller (ShipmentDtos.cs's own doc comment), so
// there is nothing for this form to collect or submit for it.
export const createMerchantShipmentSchema = z.object({
  originBranchId: z.string({ error: "Origin branch is required." }).min(1, "Origin branch is required."),
  destinationBranchId: z.string({ error: "Destination branch is required." }).min(1, "Destination branch is required."),
  serviceTypeId: z.string({ error: "Service type is required." }).min(1, "Service type is required."),
  weightKg: z.number({ error: "Weight is required." }).positive("Weight must be positive."),
  lengthCm: z.number().positive().optional(),
  widthCm: z.number().positive().optional(),
  heightCm: z.number().positive().optional(),
  codAmount: z.number({ error: "COD amount is required." }).nonnegative("COD amount cannot be negative."),
  wantsInsurance: z.boolean(),
  recipientName: z.string().min(1, "Recipient name is required."),
  recipientPhone: z.string().min(1, "Recipient phone is required."),
  recipientProvinceId: z.string({ error: "Recipient province is required." }).min(1, "Recipient province is required."),
  recipientDistrictId: z.string({ error: "Recipient district is required." }).min(1, "Recipient district is required."),
  recipientLocalLevelId: z.string({ error: "Recipient local level is required." }).min(1, "Recipient local level is required."),
  recipientWardNumber: z.number({ error: "Recipient ward number is required." }).positive(),
  recipientAddressLine: z.string().min(1, "Recipient address is required."),
  senderName: z.string().min(1, "Sender name is required."),
  senderPhone: z.string().min(1, "Sender phone is required."),
  senderProvinceId: z.string({ error: "Sender province is required." }).min(1, "Sender province is required."),
  senderDistrictId: z.string({ error: "Sender district is required." }).min(1, "Sender district is required."),
  senderLocalLevelId: z.string({ error: "Sender local level is required." }).min(1, "Sender local level is required."),
  senderWardNumber: z.number({ error: "Sender ward number is required." }).positive(),
  senderAddressLine: z.string().min(1, "Sender address is required."),
  items: z.array(itemSchema).min(1, "Add at least one item."),
});

export type CreateMerchantShipmentFormValues = z.infer<typeof createMerchantShipmentSchema>;
