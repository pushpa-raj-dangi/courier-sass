import { z } from "zod";

export const createRiderSettlementSchema = z.object({
  riderId: z.string({ error: "Rider is required." }).min(1, "Rider is required."),
  branchId: z.string({ error: "Branch is required." }).min(1, "Branch is required."),
  // z.number(), not z.coerce.number() — coerce's *input* type is `unknown`, which conflicts with
  // useForm<CreateRiderSettlementFormValues>'s output-inferred `number` (same gap
  // branches/schema.ts already documents). The input's onChange converts the raw string to a
  // number before it reaches the form instead.
  declaredAmount: z.number({ error: "Declared amount is required." }).positive("Declared amount must be greater than zero."),
});

export type CreateRiderSettlementFormValues = z.infer<typeof createRiderSettlementSchema>;
