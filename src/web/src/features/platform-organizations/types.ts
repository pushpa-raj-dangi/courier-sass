export interface OrganizationSummary {
  id: string;
  name: string;
  slug: string;
  planName: string | null;
  isActive: boolean;
  isDeleted: boolean;
  branchCount: number;
}

export interface OrganizationUser {
  id: string;
  email: string;
  fullName: string;
  isActive: boolean;
}

export interface OrganizationBranch {
  id: string;
  code: string;
  name: string;
  isActive: boolean;
}

export interface OrganizationInvoice {
  id: string;
  planName: string;
  amountNpr: number;
  cycleStart: string;
  cycleEnd: string;
  status: "Pending" | "Paid" | "Overdue" | "Cancelled";
  dueAt: string;
  paidAt: string | null;
}

export interface OrganizationAuditEntry {
  id: string;
  action: string;
  entityType: string;
  entityId: string;
  userId: string | null;
  impersonatedByUserId: string | null;
  isCrossTenantAction: boolean;
  timestamp: string;
}

export interface OrganizationDetail {
  organization: {
    id: string;
    name: string;
    slug: string;
    isActive: boolean;
    suspendedBySystem: boolean;
    deletedAt: string | null;
    createdAt: string;
  };
  planName: string | null;
  subscriptionStatus: "Active" | "PastDue" | "Cancelled" | "Trial" | "TrialExpired" | null;
  trialEndsAt: string | null;
  users: OrganizationUser[];
  branches: OrganizationBranch[];
  invoices: OrganizationInvoice[];
  recentAuditEntries: OrganizationAuditEntry[];
}
