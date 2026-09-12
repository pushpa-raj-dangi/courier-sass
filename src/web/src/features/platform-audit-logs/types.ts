export interface AuditLogRecord {
  id: string;
  organizationId: string | null;
  organizationName: string | null;
  action: string;
  entityType: string;
  entityId: string;
  userId: string | null;
  impersonatedByUserId: string | null;
  isCrossTenantAction: boolean;
  timestamp: string;
  before: string | null;
  after: string | null;
  ipAddress: string | null;
  userAgent: string | null;
}
