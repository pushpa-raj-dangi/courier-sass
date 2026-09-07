import "@testing-library/jest-dom/vitest";
import { Blob as NodeBlob, File as NodeFile } from "node:buffer";

// jsdom's Blob/File are distinct classes from Node's built-in ones, and Node's native
// structuredClone (used internally by fake-indexeddb on every IndexedDB write, per
// https://github.com/dumbmatter/fakeIndexedDB#jsdom-often-used-with-jest) only recognizes
// its own Blob/File implementation — a jsdom Blob silently clones to `{}`, losing its
// content. This is global (not scoped to one test file via vi.stubGlobal) because more than
// one suite constructs real Blobs that flow through the same fake-indexeddb/structuredClone
// path: rider-app/hooks/useCompleteDelivery.test.tsx (this task) AND
// rider-app/sync/syncManager.test.ts's completeAction/completeActionWithPhoto fixtures
// (Task 5), which persist signature/photo Blobs via addOutboxItem/updateOutboxItem the same
// way. Scoping the shim to only the newer test file would leave Task 5's suite silently
// exercising corrupted (`{}`) Blobs — its assertions happen to still pass on corrupted data
// (a truthy `{}` still satisfies `if (action.signatureBlob && ...)` in syncManager.ts's
// replayAction), which hides the bug rather than avoiding it. Every suite that touches a
// Blob through this persistence path deserves real Blob fidelity, so the fix is global.
if (typeof window !== "undefined") {
  window.Blob = NodeBlob as unknown as typeof Blob;
  window.File = NodeFile as unknown as typeof File;
}
globalThis.Blob = NodeBlob as unknown as typeof Blob;
globalThis.File = NodeFile as unknown as typeof File;

// jsdom doesn't implement matchMedia — needed by the sidebar primitive's mobile-breakpoint hook
// (src/hooks/use-mobile.tsx) whenever a test renders anything inside SidebarProvider.
if (typeof window !== "undefined" && !window.matchMedia) {
  window.matchMedia = (query: string) => ({
    matches: false,
    media: query,
    onchange: null,
    addListener: () => {},
    removeListener: () => {},
    addEventListener: () => {},
    removeEventListener: () => {},
    dispatchEvent: () => false,
  }) as unknown as MediaQueryList;
}

// jsdom doesn't implement ResizeObserver — recharts' ResponsiveContainer (used by every
// dashboard chart) needs it to measure its container, and jsdom also reports 0 for
// offsetWidth/offsetHeight by default, which makes ResponsiveContainer render nothing.
if (typeof window !== "undefined") {
  window.ResizeObserver = class {
    private callback: ResizeObserverCallback;
    constructor(callback: ResizeObserverCallback) {
      this.callback = callback;
    }
    observe(target: Element) {
      this.callback(
        [{ target, contentRect: { width: 500, height: 300 } } as ResizeObserverEntry],
        this as unknown as ResizeObserver
      );
    }
    unobserve() {}
    disconnect() {}
  } as unknown as typeof ResizeObserver;
}
Object.defineProperty(HTMLElement.prototype, "offsetWidth", { configurable: true, value: 500 });
Object.defineProperty(HTMLElement.prototype, "offsetHeight", { configurable: true, value: 300 });
Object.defineProperty(HTMLElement.prototype, "getBoundingClientRect", {
  configurable: true,
  value: () => ({ width: 500, height: 300, top: 0, left: 0, bottom: 300, right: 500, x: 0, y: 0, toJSON() {} }),
});

// jsdom doesn't implement these pointer-capture/scroll APIs, which Radix UI's Select primitive
// calls directly when its trigger is opened via a click — needed by any test that opens a Select.
if (typeof HTMLElement !== "undefined") {
  HTMLElement.prototype.hasPointerCapture ??= () => false;
  HTMLElement.prototype.setPointerCapture ??= () => {};
  HTMLElement.prototype.releasePointerCapture ??= () => {};
  HTMLElement.prototype.scrollIntoView ??= () => {};
}

// jsdom doesn't implement IntersectionObserver — framer-motion's whileInView prop
// (every marketing section, Frontend Phase F13) checks for it before observing.
if (typeof window !== "undefined" && !window.IntersectionObserver) {
  window.IntersectionObserver = class {
    constructor(_callback: IntersectionObserverCallback, _options?: IntersectionObserverInit) {}
    observe() {}
    unobserve() {}
    disconnect() {}
    takeRecords(): IntersectionObserverEntry[] {
      return [];
    }
    root = null;
    rootMargin = "";
    thresholds: number[] = [];
  } as unknown as typeof IntersectionObserver;
}
