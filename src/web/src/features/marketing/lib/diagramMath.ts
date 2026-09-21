export interface CodBreakdown {
  collected: number;
  fee: number;
  payable: number;
}

/** Merchant payable = COD collected − courier fee (CLAUDE.md §22). Traceable, never silently adjusted. */
export function codBreakdown(collected: number, fee: number): CodBreakdown {
  if (collected < 0 || fee < 0) throw new RangeError("Amounts must be non-negative");
  if (fee > collected) throw new RangeError("Courier fee cannot exceed collected COD");
  return { collected, fee, payable: collected - fee };
}

export interface ManifestReconciliation {
  expected: number;
  scanned: number;
  missing: number;
  missingIndices: number[];
}

export function reconcileManifest(expected: number, missingIndices: number[]): ManifestReconciliation {
  const unique = Array.from(new Set(missingIndices))
    .filter((index) => index >= 0 && index < expected)
    .sort((a, b) => a - b);
  return { expected, scanned: expected - unique.length, missing: unique.length, missingIndices: unique };
}

export function formatNpr(amount: number): string {
  return `NPR ${amount.toLocaleString("en-US")}`;
}
