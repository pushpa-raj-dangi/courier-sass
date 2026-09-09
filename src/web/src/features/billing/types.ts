export interface PlanSummary {
  planName: string;
  monthlyPriceNpr: number;
  status: "Active" | "PastDue" | "Cancelled";
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
