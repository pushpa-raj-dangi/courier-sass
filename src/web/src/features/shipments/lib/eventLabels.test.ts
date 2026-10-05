import { describe, expect, it } from "vitest";
import { eventTitle } from "./eventLabels";

describe("eventTitle", () => {
  it("maps known event types", () => {
    expect(eventTitle("ReceivedAtDestination")).toBe("Received at destination");
    expect(eventTitle("OutForDelivery")).toBe("Out for delivery");
  });
  it("humanises unknown types", () => {
    expect(eventTitle("SomethingNewHappened")).toBe("Something new happened");
  });
});
