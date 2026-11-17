// Tailwind only emits class names it can see as complete literals, so every status token the
// public tracking surface can resolve is mapped to full class strings here.
interface TokenClasses {
  /** Solid dot background + border. */
  dot: string;
  /** Soft ring around the "now" dot. */
  ring: string;
  /** Text colour for the status label. */
  text: string;
}

const TOKEN_CLASSES: Record<string, TokenClasses> = {
  "status-created": { dot: "border-status-created bg-status-created", ring: "ring-4 ring-status-created/20", text: "text-status-created" },
  "status-in-transit": { dot: "border-status-in-transit bg-status-in-transit", ring: "ring-4 ring-status-in-transit/20", text: "text-status-in-transit" },
  "status-out-for-delivery": { dot: "border-status-out-for-delivery bg-status-out-for-delivery", ring: "ring-4 ring-status-out-for-delivery/20", text: "text-status-out-for-delivery" },
  "status-delivered": { dot: "border-status-delivered bg-status-delivered", ring: "ring-4 ring-status-delivered/20", text: "text-status-delivered" },
  "status-ndr": { dot: "border-status-ndr bg-status-ndr", ring: "ring-4 ring-status-ndr/20", text: "text-status-ndr" },
  "status-cancelled": { dot: "border-status-cancelled bg-status-cancelled", ring: "ring-4 ring-status-cancelled/20", text: "text-status-cancelled" },
};

export function statusTokenClasses(token: string): TokenClasses {
  return TOKEN_CLASSES[token] ?? TOKEN_CLASSES["status-cancelled"];
}
