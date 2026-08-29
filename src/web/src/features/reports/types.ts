export interface CodFinancialSummary {
  expected: number;
  collected: number;
  pending: number;
  settled: number;
  merchantPayable: number;
}

export interface OrgDashboardSummary {
  todaysShipmentsByStatus: Record<string, number>;
  financial: CodFinancialSummary;
  overallDeliverySuccessRate: number | null;
  overallNdrRate: number | null;
  overallRtoRate: number | null;
  // .NET TimeSpan's default JSON format, e.g. "02:15:30" (hh:mm:ss) or "1.02:15:30" (d.hh:mm:ss)
  averageDeliveryTime: string | null;
}
