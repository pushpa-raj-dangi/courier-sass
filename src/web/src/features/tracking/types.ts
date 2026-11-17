export type PublicTrackingStatus =
  | "Received"
  | "PickedUp"
  | "InTransit"
  | "AtDestinationBranch"
  | "OutForDelivery"
  | "Delivered"
  | "DeliveryAttemptUnsuccessful"
  | "Cancelled";

export interface PublicTrackingEvent {
  status: PublicTrackingStatus;
  timestamp: string;
  location: string | null;
}

export interface PublicTrackingResult {
  awb: string;
  currentStatus: PublicTrackingStatus;
  events: PublicTrackingEvent[];
  destinationArea: string | null;
  senderName: string | null;
}
