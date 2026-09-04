import withSerwistInit from "@serwist/next";

const withSerwist = withSerwistInit({
  swSrc: "src/app/rider/sw.ts",
  swDest: "public/sw-rider.js",
  disable: process.env.NODE_ENV === "development",
});

/** @type {import('next').NextConfig} */
const nextConfig = {
  output: "standalone",
};

export default withSerwist(nextConfig);
