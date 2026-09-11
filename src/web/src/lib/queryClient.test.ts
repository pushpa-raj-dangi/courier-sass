import { afterEach, describe, expect, it } from "vitest";
import { redirectToBillingOnTrialExpired } from "./queryClient";
import { TrialExpiredError } from "@/lib/api/clientFetch";

// Follows the window.location redefinition pattern established in
// src/features/billing/GatewayRedirectForm.test.ts — jsdom throws "Not implemented: navigation"
// if window.location.href is actually assigned, so redefine just the `href` accessor to record
// the assignment instead of performing it.
function stubLocation(pathname: string) {
  let assignedHref = "";
  Object.defineProperty(window, "location", {
    configurable: true,
    value: { ...window.location, pathname, get href() { return assignedHref; }, set href(value: string) { assignedHref = value; } },
  });
  return () => assignedHref;
}

describe("redirectToBillingOnTrialExpired", () => {
  afterEach(() => {
    Object.defineProperty(window, "location", { configurable: true, value: { ...window.location, href: "" } });
  });

  it("ignores errors that are not TrialExpiredError", () => {
    const getHref = stubLocation("/shipments");

    redirectToBillingOnTrialExpired(new Error("boom"));

    expect(getHref()).toBe("");
  });

  it("does not redirect when already on /billing", () => {
    const getHref = stubLocation("/billing");

    redirectToBillingOnTrialExpired(new TrialExpiredError());

    expect(getHref()).toBe("");
  });

  it.each(["/merchant", "/merchant/shipments", "/rider", "/rider/tasks/today", "/platform", "/platform/organizations"])(
    "does not redirect from %s — a different auth realm / a user who can't reach /billing",
    (pathname) => {
      const getHref = stubLocation(pathname);

      redirectToBillingOnTrialExpired(new TrialExpiredError());

      expect(getHref()).toBe("");
    }
  );

  it.each(["/shipments", "/branches", "/dashboard"])(
    "redirects to /billing from staff surface %s",
    (pathname) => {
      const getHref = stubLocation(pathname);

      redirectToBillingOnTrialExpired(new TrialExpiredError());

      expect(getHref()).toBe("/billing");
    }
  );
});
