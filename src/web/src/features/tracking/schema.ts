import { z } from "zod";

export const trackingSearchSchema = z.object({
  awb: z.string().trim().min(1, "Enter an AWB number."),
});

export type TrackingSearchFormValues = z.infer<typeof trackingSearchSchema>;
