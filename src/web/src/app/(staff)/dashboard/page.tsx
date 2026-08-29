import { LayoutDashboard } from "lucide-react";
import { EmptyState } from "@/components/states/EmptyState";

export default function DashboardPage() {
  return (
    <EmptyState
      icon={LayoutDashboard}
      title="Dashboard coming in Phase F1"
      description="Organization-wide shipment, NDR/RTO, and COD summary tiles land in Frontend Phase F1."
    />
  );
}
