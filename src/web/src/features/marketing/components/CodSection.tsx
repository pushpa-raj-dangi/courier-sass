import { COD_STAGES } from "../content";
import { CodWaterfallDiagram } from "./CodWaterfallDiagram";
import { SectionShell } from "./SectionShell";
import { StageList } from "./StageList";

export function CodSection() {
  return (
    <SectionShell
      id="cod"
      index={3}
      eyebrow="COD ledger"
      title="Cash on delivery, followed to the last rupee."
      lede="COD is a financial lifecycle, not a checkbox. Collection, rider deposit, branch reconciliation, finance verification and merchant settlement each leave a traceable record."
      tone="elevated"
    >
      <div className="grid gap-12 lg:grid-cols-[1.2fr_1fr] lg:items-center">
        <CodWaterfallDiagram />
        <StageList label="COD lifecycle" stages={COD_STAGES} />
      </div>
    </SectionShell>
  );
}
