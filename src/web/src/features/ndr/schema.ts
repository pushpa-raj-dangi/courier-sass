import { z } from "zod";

// Status lists only the backend's legal next moves (NEXT_NDR_STATUSES), so a chosen value is
// always valid; empty means the user has not picked between two branches yet.
export const ndrFollowUpSchema = z.object({
  status: z.string().min(1, "Choose the next status."),
  customerResponse: z.string(),
  nextAction: z.string(),
  followUpAt: z.string(),
});

export type NdrFollowUpFormValues = z.infer<typeof ndrFollowUpSchema>;
