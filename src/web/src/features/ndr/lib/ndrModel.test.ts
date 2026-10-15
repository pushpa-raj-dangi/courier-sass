import { describe, expect, it } from "vitest";
import { NEXT_NDR_STATUSES, isFollowUpOverdue, ndrReasonLabel, ownerLabel, parseAssigned, parseNdrReason, parseNdrStatus } from "./ndrModel";

describe("ndrModel", () => {
  it("labels reasons for people", () => {
    expect(ndrReasonLabel("CustomerRequestedReschedule")).toBe("Customer asked to reschedule");
    expect(ndrReasonLabel("CashUnavailable")).toBe("Cash not ready");
  });

  it("parses URL values and rejects unknown ones", () => {
    expect(parseNdrStatus("Open")).toBe("Open");
    expect(parseNdrStatus("Closed")).toBeUndefined();
    expect(parseNdrReason("WrongAddress")).toBe("WrongAddress");
    expect(parseNdrReason("Lost")).toBeUndefined();
    expect(parseAssigned("me")).toBe("me");
    expect(parseAssigned("unassigned")).toBe("unassigned");
    expect(parseAssigned("everyone")).toBeUndefined();
  });

  it("flags follow-ups in the past only for open NDRs", () => {
    const now = new Date("2026-10-15T12:00:00Z");
    expect(isFollowUpOverdue({ followUpAt: "2026-10-15T11:00:00Z", status: "Contacting" }, now)).toBe(true);
    expect(isFollowUpOverdue({ followUpAt: "2026-10-15T13:00:00Z", status: "Contacting" }, now)).toBe(false);
    expect(isFollowUpOverdue({ followUpAt: "2026-10-15T11:00:00Z", status: "Resolved" }, now)).toBe(false);
    expect(isFollowUpOverdue({ followUpAt: null, status: "Open" }, now)).toBe(false);
  });

  it("names the owner", () => {
    const userName = (id: string | null) => (id === "u2" ? "Sita Rai" : "Staff");
    expect(ownerLabel("u1", "u1", userName)).toBe("You");
    expect(ownerLabel("u2", "u1", userName)).toBe("Sita Rai");
    expect(ownerLabel(null, "u1", userName)).toBe("Unassigned");
  });

  it("lists only the backend's legal forward moves", () => {
    expect(NEXT_NDR_STATUSES.Open).toEqual(["Contacting"]);
    expect(NEXT_NDR_STATUSES.CustomerConfirmed).toEqual(["ReattemptScheduled", "RtoRequested"]);
    expect(NEXT_NDR_STATUSES.ReattemptScheduled).toEqual([]);
  });
});
