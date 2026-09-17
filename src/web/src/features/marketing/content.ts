import { AlertTriangle, Building2, MapPinned, ScanLine, Truck, Wallet, type LucideIcon } from "lucide-react";

export interface NavLink {
  href: string;
  label: string;
}

export const NAV_LINKS: NavLink[] = [
  { href: "#features", label: "Features" },
  { href: "/pricing", label: "Pricing" },
  { href: "#how-it-works", label: "How it works" },
  { href: "/track", label: "Track a parcel" },
];

export interface CtaLink {
  href: string;
  label: string;
}

export function getPrimaryCta(isAuthenticated: boolean): CtaLink {
  return isAuthenticated
    ? { href: "/dashboard", label: "Go to dashboard" }
    : { href: "/register", label: "Get started" };
}

export interface MarketingFeature {
  icon: LucideIcon;
  title: string;
  description: string;
}

export const FEATURES: MarketingFeature[] = [
  {
    icon: Building2,
    title: "Multi-branch & role-based access",
    description:
      "Organizations, branches, and permission-based roles — every user scoped to exactly the branches and actions they're allowed.",
  },
  {
    icon: ScanLine,
    title: "Scan-driven custody",
    description:
      "Barcode and QR scanning for receive, sort, dispatch, and transfer — every custody change is a real scan, never an edit.",
  },
  {
    icon: Truck,
    title: "Manifests & hub transfers",
    description:
      "Sealed manifests move shipments branch to branch, with missing-shipment reconciliation on every receive.",
  },
  {
    icon: Wallet,
    title: "COD lifecycle & settlement",
    description:
      "Collection through rider deposit, branch reconciliation, and merchant settlement — every rupee traceable.",
  },
  {
    icon: AlertTriangle,
    title: "NDR & RTO management",
    description: "Structured non-delivery follow-up and return-to-origin workflows, not a dead-end failed status.",
  },
  {
    icon: MapPinned,
    title: "Nepal-specific geography & pricing",
    description:
      "Province, District, Local Level, and Ward drive delivery zones and rate cards — never free-text addresses.",
  },
];

export type WalkthroughTone = "neutral" | "motion" | "success";

export interface HowItWorksStep {
  step: string;
  title: string;
  description: string;
  tone: WalkthroughTone;
}

// Tones mirror WALKTHROUGH_STOPS 1:1 (Book~Created, Scan & Sort~In Transit, Deliver &
// Prove~Out for Delivery, Reconcile~Delivered) — the two sections describe the same
// four-stage lifecycle, per the design spec's "synchronized with the four HowItWorks
// steps" intent.
export const HOW_IT_WORKS_STEPS: HowItWorksStep[] = [
  {
    step: "01",
    title: "Book",
    description: "A shipment is created with an AWB, sender, recipient, and declared value.",
    tone: "neutral",
  },
  {
    step: "02",
    title: "Scan & sort",
    description: "Origin branch receive, manifest, and hub-to-hub transfer — every step scanned.",
    tone: "motion",
  },
  {
    step: "03",
    title: "Deliver & prove",
    description: "Assigned to a rider, delivery attempted, proof of delivery captured.",
    tone: "motion",
  },
  {
    step: "04",
    title: "Reconcile",
    description: "COD collected, deposited, reconciled, and settled to the merchant.",
    tone: "success",
  },
];

export interface WalkthroughStop {
  label: string;
  caption: string;
  tone: WalkthroughTone;
}

export const WALKTHROUGH_STOPS: WalkthroughStop[] = [
  { label: "Created", caption: "AWB NP2026-000000 booked at the origin counter.", tone: "neutral" },
  { label: "In transit", caption: "Scanned through the origin-to-destination hub transfer.", tone: "motion" },
  { label: "Out for delivery", caption: "Assigned to a rider, delivery attempt logged.", tone: "motion" },
  { label: "Delivered", caption: "Proof of delivery captured, COD reconciled.", tone: "success" },
];

export interface CredibilityPoint {
  title: string;
  description: string;
}

export const CREDIBILITY_POINTS: CredibilityPoint[] = [
  {
    title: "Real Nepal geography",
    description: "Province, District, Local Level, and Ward drive coverage and pricing — never free text.",
  },
  {
    title: "NPR-only, fully traceable COD",
    description:
      "Every collection, deposit, and settlement is recorded with before/after values and a reason for any adjustment.",
  },
  {
    title: "Status changes are real operations",
    description:
      "A shipment only moves forward through an actual scan, manifest, or delivery attempt — never an arbitrary admin edit.",
  },
  {
    title: "Audited, not assumed",
    description: "Sensitive changes — status corrections, COD adjustments, role changes — are logged with who, when, and why.",
  },
];

export interface MarketingScreenshot {
  id: "dashboard" | "shipment-detail" | "scan-manifest" | "cod-settlement";
  src: string;
  alt: string;
  caption: string;
}

export const SCREENSHOTS: MarketingScreenshot[] = [
  {
    id: "dashboard",
    src: "/marketing/screenshots/dashboard.webp",
    alt: "Org dashboard showing today's shipment counts, COD summary, and branch performance",
    caption: "Dashboard — today's shipments, COD, and branch performance at a glance",
  },
  {
    id: "shipment-detail",
    src: "/marketing/screenshots/shipment-detail.webp",
    alt: "Shipment detail view showing AWB, status timeline, and custody history",
    caption: "Shipment detail — full status timeline and custody history for one parcel",
  },
  {
    id: "scan-manifest",
    src: "/marketing/screenshots/scan-manifest.webp",
    alt: "Manifest screen showing shipments scanned for hub-to-hub transfer",
    caption: "Manifests — scan-verified shipments moving branch to branch",
  },
  {
    id: "cod-settlement",
    src: "/marketing/screenshots/cod-settlement.webp",
    alt: "COD settlement screen showing merchant payable amounts and reconciliation status",
    caption: "COD settlement — collection through merchant payout, fully reconciled",
  },
];

// Mirrors Billing.Domain.Plan's real fields (Name, MonthlyPriceNpr, MaxBranches,
// MaxStaffUsers, MaxMonthlyShipments, IsDefault) rather than an invented feature list —
// there's no public /plans endpoint yet, so these are static and shaped like the entity,
// not fetched. null on a Max* field means unlimited (matches the domain's own nullable-int
// convention); null on monthlyPriceNpr means custom/contact-us (Enterprise has no fixed price).
export interface PricingPlan {
  name: string;
  slug: string;
  monthlyPriceNpr: number | null;
  maxBranches: number | null;
  maxStaffUsers: number | null;
  maxMonthlyShipments: number | null;
  isDefault: boolean;
  tagline: string;
}

export const PRICING_PLANS: PricingPlan[] = [
  {
    name: "Starter",
    slug: "starter",
    monthlyPriceNpr: 4999,
    maxBranches: 1,
    maxStaffUsers: 5,
    maxMonthlyShipments: 1000,
    isDefault: false,
    tagline: "One branch, run properly from day one.",
  },
  {
    name: "Growth",
    slug: "growth",
    monthlyPriceNpr: 12999,
    maxBranches: 5,
    maxStaffUsers: 25,
    maxMonthlyShipments: 8000,
    isDefault: true,
    tagline: "A branch network with hub transfers and COD reconciliation.",
  },
  {
    name: "Scale",
    slug: "scale",
    monthlyPriceNpr: 29999,
    maxBranches: null,
    maxStaffUsers: null,
    maxMonthlyShipments: 30000,
    isDefault: false,
    tagline: "Multi-hub operations, unlimited branches and staff.",
  },
  {
    name: "Enterprise",
    slug: "enterprise",
    monthlyPriceNpr: null,
    maxBranches: null,
    maxStaffUsers: null,
    maxMonthlyShipments: null,
    isDefault: false,
    tagline: "Custom limits, dedicated onboarding, and support.",
  },
];

export interface PricingFaqItem {
  question: string;
  answer: string;
}

// Every answer here is a verified backend behavior, not marketing filler —
// see TrialEnforcementMiddleware, PlanService.AssignPlanAsync, BranchPlanLimitAdapter,
// and Plan.MonthlyPriceNpr (no currency field — NPR is the only supported currency).
export const PRICING_FAQ: PricingFaqItem[] = [
  {
    question: "What happens when the 7-day trial ends?",
    answer:
      "You keep full read access — nothing disappears. Creating or changing anything is paused until you choose a plan.",
  },
  {
    question: "Can I switch plans later?",
    answer: "Yes, any time. Your existing branches, staff, and shipment history carry over — nothing is reset.",
  },
  {
    question: "What do the branch, staff, and shipment numbers mean?",
    answer:
      "Branch count is enforced automatically once you're on a plan. Staff and monthly shipment figures describe what each tier is sized for — talk to us if you expect to outgrow one.",
  },
  {
    question: "Is billing in NPR?",
    answer: "Yes — every plan is priced and billed in Nepalese Rupees only, monthly, no other currency option.",
  },
];

export const FOOTER_LINKS: NavLink[] = [
  { href: "/register", label: "Create an account" },
  { href: "/login", label: "Staff sign in" },
  { href: "/merchant-login", label: "Merchant sign in" },
  { href: "/track", label: "Track a parcel" },
];
