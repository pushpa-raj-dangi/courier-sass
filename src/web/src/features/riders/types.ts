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

export interface MyTask {
  taskId: string;
  shipmentId: string;
  type: DeliveryTaskType;
  sequence: number;
  awb: string;
  recipientName: string;
  recipientPhone: string;
  recipientAddressLine: string;
  recipientWardNumber: number;
  codAmount: number;
}

export interface MyTasksToday {
  deliveryRunId: string | null;
  tasks: MyTask[];
}

export type DeliveryAttemptReason =
  | "CustomerUnavailable" | "PhoneUnreachable" | "WrongAddress" | "IncompleteAddress"
  | "CustomerRefused" | "CustomerRequestedReschedule" | "CashUnavailable" | "DamagedParcel" | "Other";

export interface RecordFailedAttemptInput {
  shipmentId: string;
  reason: DeliveryAttemptReason;
  notes?: string;
  latitude?: number;
  longitude?: number;
  customerContacted: boolean;
  nextAction?: string;
}

export interface DeliveryAttemptResult {
  id: string;
  shipmentId: string;
  riderId: string;
  attemptNumber: number;
  succeeded: boolean;
  reason: DeliveryAttemptReason | null;
  nextAction: string | null;
}

export interface UploadPodFileResult {
  storageKey: string;
}

export interface CompleteDeliveryInput {
  shipmentId: string;
  recipientName: string;
  otpVerified: boolean;
  signatureStorageKey?: string | null;
  photoStorageKey?: string | null;
  latitude?: number | null;
  longitude?: number | null;
  collectedCodAmount?: number | null;
}

// F15 R3b-2 rider board and rider Sheet (GET /api/v1/riders/board, /riders/{id}/today).
export type DeliveryRunStatus = "Active" | "Completed";
export type DeliveryTaskStatus = "Pending" | "Completed" | "Failed";

export interface RiderBoardRun {
  id: string;
  status: DeliveryRunStatus;
}

/** Counts are today's run only (UTC date). `lastPingAt` is null when the rider has not pinged today. */
export interface RiderBoardRow {
  riderId: string;
  userId: string;
  fullName: string;
  baseBranchId: string;
  roleType: RiderRoleType;
  isActive: boolean;
  vehiclePlate: string | null;
  run: RiderBoardRun | null;
  tasksAssigned: number;
  tasksCompleted: number;
  tasksFailed: number;
  lastPingAt: string | null;
}

export interface RiderBoard {
  items: RiderBoardRow[];
}

export interface RiderTodayTask {
  taskId: string;
  shipmentId: string;
  /** "" when the AWB lookup missed. */
  awb: string;
  type: DeliveryTaskType;
  sequence: number;
  status: DeliveryTaskStatus;
}

export interface RiderToday {
  rider: RiderBoardRow;
  tasks: RiderTodayTask[];
}

// F15 R4 rider PWA "my day" (GET /api/v1/riders/me/today): every status of today's run.
export interface MyTodayTask extends MyTask {
  status: DeliveryTaskStatus;
}

export interface MyToday {
  run: RiderBoardRun | null;
  tasks: MyTodayTask[];
  counts: { assigned: number; completed: number; failed: number };
  cash: { amount: number; parcelCount: number; oldestCollectedAt: string } | null;
  branchName: string | null;
  vehiclePlate: string | null;
}
