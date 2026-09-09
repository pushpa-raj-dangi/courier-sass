import { z } from "zod";

export const createPlanSchema = z.object({
  name: z.string().min(1, "Name is required"),
  slug: z.string().min(1, "Slug is required"),
  monthlyPriceNpr: z.coerce.number().min(0, "Price cannot be negative"),
  maxBranches: z.coerce.number().int().positive().nullable().optional(),
  maxStaffUsers: z.coerce.number().int().positive().nullable().optional(),
  maxMonthlyShipments: z.coerce.number().int().positive().nullable().optional(),
  isDefault: z.boolean().default(false),
});

export type CreatePlanInput = z.infer<typeof createPlanSchema>;
