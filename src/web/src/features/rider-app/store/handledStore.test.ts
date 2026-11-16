import { beforeEach, describe, expect, it } from "vitest";
import { useHandledStore } from "./handledStore";

describe("useHandledStore", () => {
  beforeEach(() => useHandledStore.setState({ handled: new Set() }));

  it("remembers shipments handled on this phone until told to forget one", () => {
    const before = useHandledStore.getState().handled;
    useHandledStore.getState().markHandled("s1");
    useHandledStore.getState().markHandled("s2");
    expect([...useHandledStore.getState().handled]).toEqual(["s1", "s2"]);
    // A new Set each change so subscribers re-render.
    expect(useHandledStore.getState().handled).not.toBe(before);

    useHandledStore.getState().forget("s1");
    expect([...useHandledStore.getState().handled]).toEqual(["s2"]);
  });

  it("keeps the same set when nothing changes", () => {
    useHandledStore.getState().markHandled("s1");
    const set = useHandledStore.getState().handled;
    useHandledStore.getState().markHandled("s1");
    useHandledStore.getState().forget("nope");
    expect(useHandledStore.getState().handled).toBe(set);
  });
});
