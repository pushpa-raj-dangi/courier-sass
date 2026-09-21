import { CustodyChainDiagram } from "./CustodyChainDiagram";
import { SectionShell } from "./SectionShell";

export function CustodySection() {
  return (
    <SectionShell
      id="custody"
      index={1}
      eyebrow="Custody chain"
      title="Every hand-off is a scan. Every scan is a record."
      lede="A shipment only moves forward through a real operation — a rider pickup, a branch receive, a manifest dispatch, a proof of delivery. Never an arbitrary status edit."
      tone="elevated"
    >
      <CustodyChainDiagram />
    </SectionShell>
  );
}
