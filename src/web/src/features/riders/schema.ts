import { z } from "zod";

export const createRiderSchema = z.object({
  email: z.string().email("Enter a valid email."),
  password: z.string().min(8, "Password must be at least 8 characters."),
  fullName: z.string().min(1, "Full name is required."),
  phone: z.string().optional(),
  licenseNumber: z.string().min(1, "License number is required."),
  baseBranchId: z.string({ error: "Base branch is required." }).min(1, "Base branch is required."),
  roleType: z.enum(["PickupRider", "DeliveryRider"]),
});

export type CreateRiderFormValues = z.infer<typeof createRiderSchema>;

export const createVehicleSchema = z.object({
  type: z.string().min(1, "Vehicle type is required."),
  plateNumber: z.string().min(1, "Plate number is required."),
  capacity: z.number().positive("Capacity must be positive.").optional(),
  riderId: z.string().optional(),
});

export type CreateVehicleFormValues = z.infer<typeof createVehicleSchema>;
