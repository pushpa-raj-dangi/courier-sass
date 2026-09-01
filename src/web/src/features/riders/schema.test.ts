import { describe, expect, it } from "vitest";
import { createRiderSchema, createVehicleSchema } from "./schema";

describe("createRiderSchema", () => {
  it("accepts a valid rider", () => {
    const result = createRiderSchema.safeParse({
      email: "rider@example.com",
      password: "P@ssw0rd123!",
      fullName: "Test Rider",
      licenseNumber: "LIC-001",
      baseBranchId: "b1",
      roleType: "DeliveryRider",
    });
    expect(result.success).toBe(true);
  });

  it("rejects an invalid email", () => {
    const result = createRiderSchema.safeParse({
      email: "not-an-email",
      password: "P@ssw0rd123!",
      fullName: "Test Rider",
      licenseNumber: "LIC-001",
      baseBranchId: "b1",
      roleType: "DeliveryRider",
    });
    expect(result.success).toBe(false);
  });

  it("rejects a missing base branch", () => {
    const result = createRiderSchema.safeParse({
      email: "rider@example.com",
      password: "P@ssw0rd123!",
      fullName: "Test Rider",
      licenseNumber: "LIC-001",
      baseBranchId: "",
      roleType: "DeliveryRider",
    });
    expect(result.success).toBe(false);
  });
});

describe("createVehicleSchema", () => {
  it("accepts a valid vehicle", () => {
    const result = createVehicleSchema.safeParse({ type: "Motorcycle", plateNumber: "BA 2 KHA 1234" });
    expect(result.success).toBe(true);
  });

  it("rejects a missing plate number", () => {
    const result = createVehicleSchema.safeParse({ type: "Motorcycle", plateNumber: "" });
    expect(result.success).toBe(false);
  });
});
