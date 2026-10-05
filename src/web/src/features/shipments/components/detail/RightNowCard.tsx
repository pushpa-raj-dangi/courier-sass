import { MapPin } from "lucide-react";
import { usePeopleNames } from "@/features/shipments/hooks/usePeopleNames";
import { formatDuration } from "@/features/shipments/lib/custodyChain";
import type { Shipment } from "@/features/shipments/types";

function Fact({ label, value }: { label: string; value: string }) {
  return (
    <div className="flex flex-col gap-0.5">
      <span className="text-[11.5px] text-muted-foreground">{label}</span>
      <span className="text-[13.5px] font-semibold text-foreground">{value}</span>
    </div>
  );
}

export function RightNowCard({ shipment, since }: { shipment: Shipment; since: string | null }) {
  const names = usePeopleNames();
  const custodian = shipment.currentCustodianRiderId
    ? names.riderName(shipment.currentCustodianRiderId)
    : shipment.currentCustodianUserId
      ? names.userName(shipment.currentCustodianUserId)
      : "—";
  return (
    <section aria-label="Current custody" className="space-y-3 rounded-xl border border-primary/25 bg-primary/5 p-4">
      <h3 className="flex items-center gap-2 font-display text-sm font-semibold text-primary">
        <MapPin className="h-4 w-4" aria-hidden="true" />
        Right now
      </h3>
      <div className="grid grid-cols-2 gap-x-4 gap-y-3">
        <Fact label="Custodian" value={custodian} />
        <Fact label="Owning branch" value={shipment.currentBranchId ? names.branchName(shipment.currentBranchId) : "In transit"} />
        <Fact
          label="In custody since"
          value={since ? `${new Date(since).toLocaleTimeString([], { hour: "numeric", minute: "2-digit" })} · ${formatDuration(since, null)}` : "—"}
        />
        <Fact label="Destination branch" value={names.branchName(shipment.destinationBranchId)} />
      </div>
    </section>
  );
}
