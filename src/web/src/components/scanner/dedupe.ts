export const SCAN_DEDUPE_MS = 3000;

// A barcode held in front of the camera is decoded every frame. Emit a code once, then
// suppress it while it keeps being seen within `windowMs` of the previous sighting.
export function createDeduper(windowMs: number, now: () => number = () => Date.now()) {
  let lastCode: string | null = null;
  let lastSeen = -Infinity;
  return (code: string): boolean => {
    const t = now();
    const duplicate = code === lastCode && t - lastSeen < windowMs;
    lastCode = code;
    lastSeen = t;
    return !duplicate;
  };
}
