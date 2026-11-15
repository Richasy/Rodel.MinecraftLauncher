import { createMDX } from "fumadocs-mdx/next";
const withMDX = createMDX();
export default withMDX({
  output: "export",
  distDir: process.env.NODE_ENV === "development" ? ".next-dev" : ".next",
  trailingSlash: true,
  reactStrictMode: true,
  productionBrowserSourceMaps: false,
  images: { unoptimized: true },
  devIndicators: false,
});
