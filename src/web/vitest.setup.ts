import "@testing-library/jest-dom/vitest";

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
