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
}
