import { afterEach, describe, expect, it, vi } from "vitest";
import { redirectToGateway } from "./GatewayRedirectForm";

describe("redirectToGateway", () => {
  afterEach(() => {
    document.body.innerHTML = "";
    vi.restoreAllMocks();
  });

  it("does a plain redirect when formFields is empty (Khalti)", () => {
    // jsdom throws "Not implemented: navigation" if window.location.href is actually assigned —
    // redefine just the `href` accessor so the assignment is a recorded property set instead.
    let assignedHref = "";
    Object.defineProperty(window, "location", {
      configurable: true,
      value: { ...window.location, get href() { return assignedHref; }, set href(value: string) { assignedHref = value; } },
    });

    redirectToGateway({ redirectUrl: "https://khalti.com/pay/abc", formFields: {} });

    expect(assignedHref).toBe("https://khalti.com/pay/abc");
  });

  it("builds and submits a hidden POST form when formFields is non-empty (eSewa)", () => {
    const submitSpy = vi.spyOn(HTMLFormElement.prototype, "submit").mockImplementation(() => {});

    redirectToGateway({
      redirectUrl: "https://rc-epay.esewa.com.np/api/epay/main/v2/form",
      formFields: { amount: "1500.00", transaction_uuid: "abc-123" },
    });

    const form = document.querySelector("form");
    expect(form).not.toBeNull();
    expect(form!.method).toBe("post");
    expect(form!.action).toBe("https://rc-epay.esewa.com.np/api/epay/main/v2/form");
    const amountInput = form!.querySelector('input[name="amount"]') as HTMLInputElement;
    const uuidInput = form!.querySelector('input[name="transaction_uuid"]') as HTMLInputElement;
    expect(amountInput.type).toBe("hidden");
    expect(amountInput.value).toBe("1500.00");
    expect(uuidInput.value).toBe("abc-123");
    expect(submitSpy).toHaveBeenCalledTimes(1);
  });
});
