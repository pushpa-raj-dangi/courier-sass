import { describe, expect, it, vi } from "vitest";

vi.mock("next/navigation", () => ({ redirect: vi.fn() }));

import { redirect } from "next/navigation";
import LiveMapRedirect from "./page";

describe("/riders/live-map", () => {
  it("redirects to the Map tab", () => {
    LiveMapRedirect({ searchParams: {} });
    expect(redirect).toHaveBeenCalledWith("/riders?tab=map");
  });

  it("keeps the branch when one is given", () => {
    LiveMapRedirect({ searchParams: { branch: "b 1" } });
    expect(redirect).toHaveBeenLastCalledWith("/riders?tab=map&branch=b+1");
  });
});
