import type { WalkthroughTone } from "../content";

// References --marketing-tone-* (globals.css), which carries independently
// verified light- and dark-mode values so these dot/rail fills stay WCAG AA
// (>=3:1 non-text) against --marketing-bg/-bg-elevated in whichever theme is
// active — see globals.css's :root and .dark comments for the derivation.
export const TONE_COLOR: Record<WalkthroughTone, string> = {
  neutral: "hsl(var(--marketing-tone-neutral))",
  motion: "hsl(var(--marketing-tone-motion))",
  success: "hsl(var(--marketing-tone-success))",
};
