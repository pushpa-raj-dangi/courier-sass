export interface RecordFailedAttemptFields {
  shipmentId: string;
  reason:
    | "CustomerUnavailable" | "PhoneUnreachable" | "WrongAddress" | "IncompleteAddress"
    | "CustomerRefused" | "CustomerRequestedReschedule" | "CashUnavailable" | "DamagedParcel" | "Other";
  notes?: string;
  latitude?: number | null;
  longitude?: number | null;
  customerContacted: boolean;
  nextAction?: string;
}

export type QueuedAction =
  | { kind: "pickup-scan"; riderId: string; shipmentId: string; awb: string }
  | { kind: "delivery-fail"; riderId: string; shipmentId: string; input: RecordFailedAttemptFields }
  | {
      kind: "delivery-complete";
      riderId: string;
      shipmentId: string;
      recipientName: string;
      otpVerified: boolean;
      latitude: number | null;
      longitude: number | null;
      collectedCodAmount: number | null;
      signatureBlob: Blob | null;
      photoBlob: Blob | null;
      signatureStorageKey: string | null;
      photoStorageKey: string | null;
    };

export type OutboxStatus = "pending" | "syncing" | "stuck";

export interface OutboxItem {
  id: string;
  action: QueuedAction;
  idempotencyKey: string;
  createdAt: number;
  status: OutboxStatus;
  lastError?: string;
}
