import { z } from "zod";

export const createRiderSettlementSchema = z.object({
  riderId: z.string({ error: "Rider is required." }).min(1, "Rider is required."),
  branchId: z.string({ error: "Branch is required." }).min(1, "Branch is required."),
  // z.number(), not z.coerce.number() — coerce's *input* type is `unknown`, which conflicts with
  // useForm<CreateRiderSettlementFormValues>'s output-inferred `number` (same gap
  // branches/schema.ts already documents). The input's onChange converts the raw string to a
  // number before it reaches the form instead.
  declaredAmount: z
    .number({ error: "Cash received is required." })
    .positive("Cash received must be greater than zero.")
    .refine((v) => /^\d+(\.\d{1,2})?$/.test(String(v)), "Use at most 2 decimal places."),
});

export type CreateRiderSettlementFormValues = z.infer<typeof createRiderSettlementSchema>;

export const createMerchantSettlementSchema = z
  .object({
    merchantId: z.string({ error: "Merchant is required." }).min(1, "Merchant is required."),
    periodFrom: z.string({ error: "Period start is required." }).min(1, "Period start is required."),
    periodTo: z.string({ error: "Period end is required." }).min(1, "Period end is required."),
  })
  .refine((values) => new Date(values.periodTo) >= new Date(values.periodFrom), {
    message: "Period end must not be before period start.",
    path: ["periodTo"],
  });

export type CreateMerchantSettlementFormValues = z.infer<typeof createMerchantSettlementSchema>;

export const requestAdjustmentSchema = z.object({
  // z.number(), not z.coerce.number() — same input/output type gap createRiderSettlementSchema's
  // declaredAmount documents above. Bound via register("afterValue", { valueAsNumber: true }) so
  // react-hook-form itself converts the DOM string to a number before it reaches form state.
  afterValue: z.number({ error: "New value is required." }).min(0, "New value cannot be negative."),
  reason: z.string({ error: "Reason is required." }).min(1, "Reason is required — CLAUDE.md §23 requires a reason for every financial adjustment."),
});

export type RequestAdjustmentFormValues = z.infer<typeof requestAdjustmentSchema>;
