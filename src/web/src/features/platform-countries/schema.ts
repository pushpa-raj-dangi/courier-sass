import { z } from "zod";

export const countryZoneSchema = z.object({
  name: z.string().min(1, "Name is required"),
  description: z.string().optional(),
});

export type CountryZoneFormValues = z.infer<typeof countryZoneSchema>;
