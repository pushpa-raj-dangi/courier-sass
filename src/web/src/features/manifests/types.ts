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
  receivedCount: number;
  createdAt: string;
}

export type ManifestDirection = "incoming" | "outgoing";

export interface CreateManifestInput {
  fromBranchId: string;
  toBranchId: string;
  purpose: ManifestPurpose;
}

export interface ManifestShipmentScan {
  id: string;
  shipmentId: string;
  awb: string;
  scannedAt: string;
  receivedAt: string | null;
}

export interface MissingShipment {
  shipmentId: string;
  awb: string;
  scannedAt: string;
}

export interface DispatchManifestInput {
  vehiclePlate: string;
  driverId?: string;
}
