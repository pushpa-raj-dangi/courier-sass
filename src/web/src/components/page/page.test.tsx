import { describe, expect, it } from "vitest";
import { render, screen } from "@testing-library/react";
import { Package } from "lucide-react";
import { Sheet, SheetContent, SheetDescription } from "@/components/ui/sheet";
import { PageHeader } from "./PageHeader";
import { SectionCard } from "./SectionCard";
import { KpiTile } from "./KpiTile";
import { PipelineStrip } from "./PipelineStrip";
import { SheetHeaderBlock } from "./SheetHeaderBlock";

describe("PageHeader", () => {
  it("renders an h1 title with eyebrow, description, badge and actions", () => {
    render(
      <PageHeader
        eyebrow="Good morning, Anita"
        title="Shipments"
        description="2,418 active parcels"
        badge={<span>Beta</span>}
        actions={<button type="button">Book shipment</button>}
      />
    );
    expect(screen.getByRole("heading", { level: 1, name: "Shipments" })).toBeInTheDocument();
    expect(screen.getByText("Good morning, Anita")).toBeInTheDocument();
    expect(screen.getByText("2,418 active parcels")).toBeInTheDocument();
    expect(screen.getByText("Beta")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Book shipment" })).toBeInTheDocument();
  });
});

describe("SectionCard", () => {
  it("renders an h2 title, description, action and body", () => {
    render(
      <SectionCard title="Branch performance" icon={Package} description="Last 30 days" action={<a href="/branches">All</a>}>
        <p>Body</p>
      </SectionCard>
    );
    expect(screen.getByRole("heading", { level: 2, name: "Branch performance" })).toBeInTheDocument();
    expect(screen.getByText("Last 30 days")).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "All" })).toBeInTheDocument();
    expect(screen.getByText("Body")).toBeInTheDocument();
  });
});

describe("KpiTile", () => {
  it("renders label, value, delta text with its tone, and note", () => {
    render(<KpiTile label="NDR rate" value="5.2%" delta={{ text: "+0.4 pts", tone: "negative" }} note="vs last 7 days" />);
    expect(screen.getByText("NDR rate")).toBeInTheDocument();
    expect(screen.getByText("5.2%")).toBeInTheDocument();
    const delta = screen.getByText("+0.4 pts");
    expect(delta).toHaveAttribute("data-tone", "negative");
    expect(screen.getByText("vs last 7 days")).toBeInTheDocument();
  });

  it("renders without a delta", () => {
    render(<KpiTile label="Riders" value={37} />);
    expect(screen.getByText("37")).toBeInTheDocument();
    expect(document.querySelector("[data-tone]")).toBeNull();
  });
});

describe("PipelineStrip", () => {
  const stages = [
    { key: "booked", label: "Booked", count: 1284, token: "status-created", share: 1.4, href: "/shipments?status=Created" },
    { key: "delivered", label: "Delivered", count: 0, token: "status-delivered", caption: "Today" },
  ];

  it("renders a labelled list with formatted counts and links only where href is given", () => {
    render(<PipelineStrip stages={stages} ariaLabel="Parcel pipeline" />);
    expect(screen.getByRole("list", { name: "Parcel pipeline" })).toBeInTheDocument();
    expect(screen.getByRole("link", { name: /Booked/ })).toHaveAttribute("href", "/shipments?status=Created");
    expect(screen.getByText("1,284")).toBeInTheDocument();
    expect(screen.getByText("0")).toBeInTheDocument();
    expect(screen.getAllByRole("link")).toHaveLength(1);
  });

  it("clamps share bars to 0–100%", () => {
    render(<PipelineStrip stages={stages} ariaLabel="Parcel pipeline" />);
    const bar = document.querySelector('[data-share-bar="booked"]') as HTMLElement;
    expect(bar.style.width).toBe("100%");
  });
});

describe("SheetHeaderBlock", () => {
  it("renders eyebrow, the sheet title, status and facts", () => {
    render(
      <Sheet open>
        <SheetContent>
          <SheetHeaderBlock
            eyebrow="AWB · EXPRESS · COD"
            title="NX-260930-00148"
            mono
            status={<span>Out for delivery</span>}
            facts={[{ label: "COD", value: "Rs 2,500" }]}
          />
          <SheetDescription>detail</SheetDescription>
        </SheetContent>
      </Sheet>
    );
    expect(screen.getByRole("dialog", { name: "NX-260930-00148" })).toBeInTheDocument();
    expect(screen.getByText("AWB · EXPRESS · COD")).toBeInTheDocument();
    expect(screen.getByText("Out for delivery")).toBeInTheDocument();
    expect(screen.getByText("COD")).toBeInTheDocument();
    expect(screen.getByText("Rs 2,500")).toBeInTheDocument();
  });
});
