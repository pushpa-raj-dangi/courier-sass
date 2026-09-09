export interface Plan {
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
