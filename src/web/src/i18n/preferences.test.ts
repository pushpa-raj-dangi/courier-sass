import { beforeEach, describe, expect, it } from "vitest";
import { readCookie, setPreferenceCookie } from "./preferences";

describe("preference cookies", () => {
  beforeEach(() => {
    document.cookie = "courier_locale=; Path=/; Max-Age=0";
  });

  it("returns null when the cookie is absent", () => {
    expect(readCookie("courier_locale")).toBeNull();
  });

  it("round-trips a value", () => {
    setPreferenceCookie("courier_locale", "ne");
    expect(readCookie("courier_locale")).toBe("ne");
  });

  it("returns null instead of throwing on malformed percent-encoding", () => {
    document.cookie = "courier_locale=%E0%A4%A; Path=/";
    expect(() => readCookie("courier_locale")).not.toThrow();
    expect(readCookie("courier_locale")).toBeNull();
  });

  it("round-trips a value containing =", () => {
    setPreferenceCookie("courier_locale", "a=b");
    expect(readCookie("courier_locale")).toBe("a=b");
  });

  it("round-trips a value with a space and unicode", () => {
    setPreferenceCookie("courier_locale", "नेपाल time");
    expect(readCookie("courier_locale")).toBe("नेपाल time");
  });

  it("does not confuse cookies with a shared name prefix", () => {
    setPreferenceCookie("courier_locale_extra", "x");
    expect(readCookie("courier_locale")).toBeNull();
    document.cookie = "courier_locale_extra=; Path=/; Max-Age=0";
  });
});
