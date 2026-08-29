export type ManifestPurpose = "Forward" | "Return";
export type ManifestStatus = "Draft" | "Sealed" | "InTransit" | "Received" | "Closed";

export interface Manifest {
  id: string;
  code: string;
  fromBranchId: string;
  toBranchId: string;
  purpose: ManifestPurpose;
  vehiclePlate: string | null;
  driverId: string | null;
  status: ManifestStatus;
  shipmentCount: number;
  createdAt: string;
}

export interface CreateManifestInput {
  fromBranchId: string;
  toBranchId: string;
  purpose: ManifestPurpose;
}
