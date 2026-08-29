import { z } from "zod";

export const createBranchSchema = z.object({
  code: z.string().min(1, "Code is required."),
  name: z.string().min(1, "Name is required."),
  type: z.enum(["HeadOffice", "Hub", "Branch", "PickupPoint", "Warehouse"]),
  provinceId: z.string({ error: "Province is required." }).min(1, "Province is required."),
  districtId: z.string({ error: "District is required." }).min(1, "District is required."),
  localLevelId: z.string({ error: "Local level is required." }).min(1, "Local level is required."),
  // GeographyPicker's onChange already converts the input value to a real number before it
  // reaches the form (via Number(e.target.value)), so z.coerce.number() isn't needed here - and
  // using it caused a real type mismatch: coerce's *input* type is `unknown`, which conflicts
  // with useForm<CreateBranchFormValues>'s output-inferred `number`, since zodResolver's Resolver
  // type is checked against both the schema's input and output shapes. Zod v4 also replaced the
  // v3 `required_error`/`invalid_type_error` options with a single `error` option - the v3 syntax
  // compiles as a type error under this project's Zod version, not just a deprecation warning.
  wardNumber: z.number({ error: "Ward number is required." }).positive("Ward number must be positive."),
  addressLine: z.string().min(1, "Address is required."),
  phone: z.string().optional(),
  operatingHours: z.string().optional(),
  parentBranchId: z.string().optional(),
});

export type CreateBranchFormValues = z.infer<typeof createBranchSchema>;
