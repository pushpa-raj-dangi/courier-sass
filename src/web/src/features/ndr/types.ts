export type NdrStatus = "Open" | "Contacting" | "CustomerConfirmed" | "ReattemptScheduled" | "RtoRequested" | "Resolved";
export type NdrReason =
  | "CustomerUnavailable" | "PhoneUnreachable" | "WrongAddress" | "IncompleteAddress"
  | "CustomerRefused" | "CustomerRequestedReschedule" | "CashUnavailable" | "DamagedParcel" | "Other";
export type NdrResolution = "Reattempt" | "Rto" | "AutoRto" | "Other";

export interface Ndr {
  id: string;
  shipmentId: string;
  awb: string;
  reason: NdrReason;
  assignedStaffUserId: string | null;
  status: NdrStatus;
  customerResponse: string | null;
  followUpAt: string | null;
  nextAction: string | null;
  resolution: NdrResolution | null;
  resolvedAt: string | null;
  createdAt: string;
}

export interface UpdateNdrInput {
  status: NdrStatus;
  customerResponse?: string;
  nextAction?: string;
  followUpAt?: string;
}

export interface ResolveNdrInput {
  resolution: NdrResolution;
  rtoReason?: string;
}

export interface ReturnShipment {
  id: string;
  originalShipmentId: string;
  originalAwb: string;
  returnAwb: string;
  rtoReason: string;
  originBranchId: string;
  destinationBranchId: string;
  currentStatus: string;
  currentBranchId: string | null;
  merchantConfirmedAt: string | null;
}
