export interface AdminInvoice {
  id: string;
  organizationId: string;
  organizationName: string;
  planName: string;
  amountNpr: number;
  status: "Pending" | "Paid" | "Overdue" | "Cancelled";
  dueAt: string;
  paidAt: string | null;
}
