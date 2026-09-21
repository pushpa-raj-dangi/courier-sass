import withSerwistInit from "@serwist/next";
import createNextIntlPlugin from "next-intl/plugin";

const withNextIntl = createNextIntlPlugin("./src/i18n/request.ts");

const withSerwist = withSerwistInit({
  swSrc: "src/app/rider/sw.ts",
  swDest: "public/sw-rider.js",
  disable: process.env.NODE_ENV === "development",
  // This service worker is scoped to /rider/* — a rider never navigates to the staff/merchant/
  // public-tracking app, so precaching those route bundles only wastes their bandwidth/storage
  // on a Nepali mobile connection and adds unrelated assets that could fail the SW install if
  // one goes missing. Verified against .next/app-build-manifest.json: no /rider/* route lists
  // any "app/(staff)/**" or "app/merchant/**" (etc.) chunk as a dependency, so excluding every
  // non-rider app/* route chunk here doesn't drop anything the rider PWA actually needs — the
  // shared framework/vendor chunks it does need live outside "app/" and are left untouched.
  exclude: [/\.map$/, /^manifest.*\.js$/, /^static\/chunks\/app\/(?!rider\/)/],
});

/** @type {import('next').NextConfig} */
const nextConfig = {
  output: "standalone",
};

export default withSerwist(withNextIntl(nextConfig));
