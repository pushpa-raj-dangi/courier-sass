import type { WalkthroughTone } from "../content";

// Literal HSL values, not the shared --status-* tokens: these are the app's own
// dark-mode status variants (globals.css .dark block), reused directly because every
// marketing surface that carries them sits on the fixed --marketing-bg (near-black)
// regardless of visitor theme — the same reasoning AuthLifecycleGraphic documents for
// the auth panel. Verified against --marketing-bg (228 20% 6%): created 8.14:1,
// in-transit/out-for-delivery 9.38:1, delivered 7.53:1 — all clear both the 3:1
// non-text and 4.5:1 text floors. See the plan's "Verified Design Tokens" table.
export const TONE_COLOR: Record<WalkthroughTone, string> = {
  neutral: "hsl(234.3 13.2% 68.8%)",
  motion: "hsl(38.0 78.9% 57.3%)",
  success: "hsl(169.7 47.9% 47.5%)",
};
