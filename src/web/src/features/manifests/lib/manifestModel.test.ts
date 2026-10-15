import { describe, expect, it } from "vitest";
import { manifestStageLabel, parcelsSummary, parseManifestStatus, parseManifestTab, pickManifestHighlight, stepperState } from "./manifestModel";

describe("pickManifestHighlight", () => {
  it("picks the open stage with the most manifests, ignoring Closed", () => {
    expect(pickManifestHighlight({ Draft: 1, Sealed: 2, InTransit: 5, Received: 3, Closed: 40 })).toBe("InTransit");
  });

  it("breaks ties to the left and treats missing keys as 0", () => {
    expect(pickManifestHighlight({ Sealed: 2, Received: 2 })).toBe("Sealed");
  });

  it("returns nothing when every open stage is empty", () => {
    expect(pickManifestHighlight({ Closed: 9 })).toBeUndefined();
    expect(pickManifestHighlight({})).toBeUndefined();
  });
});

describe("parsers and labels", () => {
  it("defaults the tab to incoming and rejects unknown statuses", () => {
    expect(parseManifestTab(null)).toBe("incoming");
    expect(parseManifestTab("outgoing")).toBe("outgoing");
    expect(parseManifestTab("all")).toBe("all");
    expect(parseManifestTab("bogus")).toBe("incoming");
    expect(parseManifestStatus("Received")).toBe("Received");
    expect(parseManifestStatus("Dispatched")).toBeUndefined();
    expect(manifestStageLabel("Received")).toBe("Receiving");
  });
});

describe("parcelsSummary", () => {
  it("shows received of total and the missing count once receiving has started", () => {
    expect(parcelsSummary({ status: "Received", shipmentCount: 120, receivedCount: 118 })).toEqual({ primary: "118 / 120", missing: 2 });
    expect(parcelsSummary({ status: "Closed", shipmentCount: 3, receivedCount: 3 })).toEqual({ primary: "3 / 3", missing: 0 });
  });

  it("shows the parcel total before receiving", () => {
    expect(parcelsSummary({ status: "InTransit", shipmentCount: 120, receivedCount: 0 })).toEqual({ primary: "120 parcels", missing: 0 });
    expect(parcelsSummary({ status: "Draft", shipmentCount: 1, receivedCount: 0 })).toEqual({ primary: "1 parcel", missing: 0 });
  });
});

describe("stepperState", () => {
  it("marks earlier stages complete, the current one current and later ones upcoming", () => {
    expect(stepperState("InTransit", "Sealed")).toBe("complete");
    expect(stepperState("InTransit", "InTransit")).toBe("current");
    expect(stepperState("InTransit", "Closed")).toBe("upcoming");
  });

  it("treats every stage of a closed manifest as complete", () => {
    expect(stepperState("Closed", "Closed")).toBe("complete");
    expect(stepperState("Closed", "Draft")).toBe("complete");
  });
});
