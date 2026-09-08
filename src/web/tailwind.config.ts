import type { Config } from "tailwindcss";

const config: Config = {
  darkMode: ["class"],
  // A single broad glob over all of src/ rather than one entry per top-level folder — the
  // previous per-folder list silently missed src/features/ and src/lib/, so Tailwind's JIT
  // never scanned LoginForm.tsx for classes; some utilities "worked" only because another
  // already-scanned file happened to use the same class string (e.g. dropdown-menu.tsx's
  // `absolute`), while -translate-y-1/2 (used nowhere else) generated no CSS at all.
  content: ["./src/**/*.{js,ts,jsx,tsx,mdx}"],
  theme: {
    extend: {
      colors: {
        background: "hsl(var(--background) / <alpha-value>)",
        foreground: "hsl(var(--foreground) / <alpha-value>)",
        card: {
          DEFAULT: "hsl(var(--card) / <alpha-value>)",
          foreground: "hsl(var(--card-foreground) / <alpha-value>)",
        },
        popover: {
          DEFAULT: "hsl(var(--popover) / <alpha-value>)",
          foreground: "hsl(var(--popover-foreground) / <alpha-value>)",
        },
        primary: {
          DEFAULT: "hsl(var(--primary) / <alpha-value>)",
          foreground: "hsl(var(--primary-foreground) / <alpha-value>)",
        },
        "auth-panel": {
          DEFAULT: "hsl(var(--auth-panel) / <alpha-value>)",
          foreground: "hsl(var(--auth-panel-foreground) / <alpha-value>)",
        },
        secondary: {
          DEFAULT: "hsl(var(--secondary) / <alpha-value>)",
          foreground: "hsl(var(--secondary-foreground) / <alpha-value>)",
        },
        muted: {
          DEFAULT: "hsl(var(--muted) / <alpha-value>)",
          foreground: "hsl(var(--muted-foreground) / <alpha-value>)",
        },
        accent: {
          DEFAULT: "hsl(var(--accent) / <alpha-value>)",
          foreground: "hsl(var(--accent-foreground) / <alpha-value>)",
        },
        destructive: {
          DEFAULT: "hsl(var(--destructive) / <alpha-value>)",
          foreground: "hsl(var(--destructive-foreground) / <alpha-value>)",
        },
        border: "hsl(var(--border) / <alpha-value>)",
        input: "hsl(var(--input) / <alpha-value>)",
        ring: "hsl(var(--ring) / <alpha-value>)",
        "status-created": {
          DEFAULT: "hsl(var(--status-created) / <alpha-value>)",
          foreground: "hsl(var(--status-created-foreground) / <alpha-value>)",
        },
        "status-in-transit": {
          DEFAULT: "hsl(var(--status-in-transit) / <alpha-value>)",
          foreground: "hsl(var(--status-in-transit-foreground) / <alpha-value>)",
        },
        "status-out-for-delivery": {
          DEFAULT: "hsl(var(--status-out-for-delivery) / <alpha-value>)",
          foreground: "hsl(var(--status-out-for-delivery-foreground) / <alpha-value>)",
        },
        "status-delivered": {
          DEFAULT: "hsl(var(--status-delivered) / <alpha-value>)",
          foreground: "hsl(var(--status-delivered-foreground) / <alpha-value>)",
        },
        "status-ndr": {
          DEFAULT: "hsl(var(--status-ndr) / <alpha-value>)",
          foreground: "hsl(var(--status-ndr-foreground) / <alpha-value>)",
        },
        "status-rto": {
          DEFAULT: "hsl(var(--status-rto) / <alpha-value>)",
          foreground: "hsl(var(--status-rto-foreground) / <alpha-value>)",
        },
        "status-cancelled": {
          DEFAULT: "hsl(var(--status-cancelled) / <alpha-value>)",
          foreground: "hsl(var(--status-cancelled-foreground) / <alpha-value>)",
        },
        "status-sealed": {
          DEFAULT: "hsl(var(--status-sealed) / <alpha-value>)",
          foreground: "hsl(var(--status-sealed-foreground) / <alpha-value>)",
        },
        "status-received": {
          DEFAULT: "hsl(var(--status-received) / <alpha-value>)",
          foreground: "hsl(var(--status-received-foreground) / <alpha-value>)",
        },
        sidebar: {
          DEFAULT: "hsl(var(--sidebar-background))",
          foreground: "hsl(var(--sidebar-foreground))",
          primary: "hsl(var(--sidebar-primary))",
          "primary-foreground": "hsl(var(--sidebar-primary-foreground))",
          accent: "hsl(var(--sidebar-accent))",
          "accent-foreground": "hsl(var(--sidebar-accent-foreground))",
          border: "hsl(var(--sidebar-border))",
          ring: "hsl(var(--sidebar-ring))",
        },
        "marketing-bg": "hsl(var(--marketing-bg) / <alpha-value>)",
        "marketing-bg-elevated": "hsl(var(--marketing-bg-elevated) / <alpha-value>)",
        "marketing-fg": "hsl(var(--marketing-fg) / <alpha-value>)",
        "marketing-muted-fg": "hsl(var(--marketing-muted-fg) / <alpha-value>)",
        "marketing-accent": "hsl(var(--marketing-accent) / <alpha-value>)",
        "marketing-accent-fg": "hsl(var(--marketing-accent-foreground) / <alpha-value>)",
        "marketing-border": "hsl(var(--marketing-border) / <alpha-value>)",
      },
      borderRadius: {
        lg: "var(--radius)",
        md: "calc(var(--radius) - 2px)",
        sm: "calc(var(--radius) - 4px)",
      },
      fontFamily: {
        sans: ["var(--font-plex-sans)", "Arial", "Helvetica", "sans-serif"],
        mono: ["var(--font-plex-mono)", "monospace"],
        display: ["var(--font-space-grotesk)", "var(--font-plex-sans)", "sans-serif"],
      },
    },
  },
  plugins: [require("tailwindcss-animate")],
};
export default config;
