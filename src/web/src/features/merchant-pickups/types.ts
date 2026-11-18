export type PickupStage = "Requested" | "Scheduled" | "Collected" | "Cancelled";

export interface PickupParcel {
  id: string;
  awb: string;
  destinationArea: string | null;
  /** null = prepaid */
  codAmount: number | null;
}

export interface PickupRequest {
  id: string;
  merchantId: string;
  branchId: string;
  shipmentIds: string[];
  requestedForDate: string;
  notes: string | null;
  status: "Requested" | "Scheduled" | "Completed" | "Cancelled";
  createdAt: string;
  updatedAt: string;
  stage: PickupStage;
  branchName: string | null;
  shipments: PickupParcel[];
}
