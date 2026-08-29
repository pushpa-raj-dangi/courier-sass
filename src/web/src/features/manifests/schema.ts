import { z } from "zod";

export const createManifestSchema = z
  .object({
    fromBranchId: z.string({ error: "From branch is required." }).min(1, "From branch is required."),
    toBranchId: z.string({ error: "To branch is required." }).min(1, "To branch is required."),
    purpose: z.enum(["Forward", "Return"]),
  })
  .refine((values) => values.fromBranchId !== values.toBranchId, {
    message: "From and To branch must be different.",
    path: ["toBranchId"],
  });

export type CreateManifestFormValues = z.infer<typeof createManifestSchema>;
