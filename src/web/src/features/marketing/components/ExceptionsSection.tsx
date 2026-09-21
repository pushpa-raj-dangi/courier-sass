import { DELIVERY_FAILURE_REASONS, NDR_STAGES, RTO_STAGES } from "../content";
import { MonoLabel } from "./MonoLabel";
import { SectionShell } from "./SectionShell";
import { StageList } from "./StageList";

export function ExceptionsSection() {
  return (
    <SectionShell
      id="exceptions"
      index={4}
      eyebrow="Exceptions"
      title="A failed delivery is a workflow, not a dead end."
      lede="Every attempt records who, when, where and why. Non-delivery reports route to staff follow-up, and returns keep their link to the original shipment."
    >
      <div className="grid gap-12 md:grid-cols-3">
        <div className="flex flex-col gap-3">
          <MonoLabel>Recorded attempt reasons</MonoLabel>
          <ul className="flex flex-col divide-y divide-marketing-border/60 border-y border-marketing-border/60">
            {DELIVERY_FAILURE_REASONS.map((reason) => (
              <li key={reason} className="py-2.5 text-sm text-marketing-fg">
                {reason}
              </li>
            ))}
          </ul>
        </div>
        <div className="flex flex-col gap-4">
          <MonoLabel>NDR</MonoLabel>
          <StageList label="NDR workflow" stages={NDR_STAGES} />
        </div>
        <div className="flex flex-col gap-4">
          <MonoLabel>RTO</MonoLabel>
          <StageList label="RTO workflow" stages={RTO_STAGES} />
        </div>
      </div>
    </SectionShell>
  );
}
