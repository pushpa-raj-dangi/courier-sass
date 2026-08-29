// Maps ManifestScanRejectedException's ReasonCode (backend) to a plain-language message — the
// raw code is never shown to the user. Reasons per BRANCH-OPERATIONS.md §1's five-check scan
// validation, carried through Manifests' ManifestDtos.cs.
const REASON_MESSAGES: Record<string, string> = {
  SHIPMENT_NOT_FOUND: "No shipment matches that AWB.",
  WRONG_STATE: "This shipment isn't in a state that allows this scan.",
  WRONG_CUSTODY: "This shipment isn't currently in this branch's custody.",
  MANIFEST_NOT_OPEN: "This manifest is no longer open for scanning.",
  MANIFEST_NOT_RECEIVING: "This manifest isn't ready to receive shipments yet.",
  NOT_ON_MANIFEST: "This shipment isn't on this manifest.",
  ALREADY_RECEIVED: "This shipment was already received on this manifest.",
};

export function manifestScanErrorMessage(reasonCode: string): string {
  return REASON_MESSAGES[reasonCode] ?? "Couldn't scan this shipment. Check the AWB and try again.";
}
