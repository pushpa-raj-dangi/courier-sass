export interface PlanSummary {
  planName: string;
  monthlyPriceNpr: number;
  status: "Active" | "PastDue" | "Cancelled" | "Trial" | "TrialExpired";
}

export interface SubscriptionStatusInfo {
  status: "Active" | "PastDue" | "Cancelled" | "Trial" | "TrialExpired";
  trialEndsAt: string | null;
  planName: string | null;
  monthlyPriceNpr: number | null;
}

export interface AvailablePlan {
  id: string;
  name: string;
  slug: string;
  monthlyPriceNpr: number;
  maxBranches: number | null;
  maxStaffUsers: number | null;
  maxMonthlyShipments: number | null;
  isDefault: boolean;
  isActive: boolean;
}

export interface Invoice {
  id: string;
  planName: string;
  amountNpr: number;
  cycleStart: string;
  cycleEnd: string;
  status: "Pending" | "Paid" | "Overdue" | "Cancelled";
  dueAt: string;
  paidAt: string | null;
}

export interface GatewayCheckoutInfo {
  redirectUrl: string;
  formFields: Record<string, string>;
}

export interface TrialStatusInfo {
  status: "Active" | "PastDue" | "Cancelled" | "Trial" | "TrialExpired";
  trialEndsAt: string | null;
}
