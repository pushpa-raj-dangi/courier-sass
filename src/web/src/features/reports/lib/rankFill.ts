/**
 * Bucket a value into one of the "--chart-rank-1/2/3" ordinal ramp tokens
 * (globals.css), relative to the largest value in the same series — not by
 * position, so a set of near-equal values stays visually near-equal instead
 * of an arbitrary 3-way split forcing false distinctions.
 */
export function rankFill(value: number, maxValue: number): string {
  if (maxValue <= 0) return "hsl(var(--chart-rank-3))";
  const ratio = value / maxValue;
  if (ratio >= 2 / 3) return "hsl(var(--chart-rank-1))";
  if (ratio >= 1 / 3) return "hsl(var(--chart-rank-2))";
  return "hsl(var(--chart-rank-3))";
}
