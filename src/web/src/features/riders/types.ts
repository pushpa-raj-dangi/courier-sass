export type RiderRoleType = "PickupRider" | "DeliveryRider";

export interface Rider {
  id: string;
  userId: string;
  baseBranchId: string;
  licenseNumber: string;
  roleType: RiderRoleType;
  isActive: boolean;
}

export interface CreateRiderInput {
  email: string;
  password: string;
  fullName: string;
  phone?: string;
  licenseNumber: string;
  baseBranchId: string;
  roleType: RiderRoleType;
}

export interface Vehicle {
  id: string;
  riderId: string | null;
  type: string;
  plateNumber: string;
  capacity: number | null;
}

export interface CreateVehicleInput {
  riderId?: string;
  type: string;
  plateNumber: string;
  capacity?: number;
}

export type DeliveryTaskType = "Pickup" | "Delivery";

export interface AssignDeliveryTaskInput {
  riderId: string;
  shipmentId: string;
  type: DeliveryTaskType;
}
