import { defineConfig } from "fumadocs-mdx/config";
import { remarkAlerts } from "./lib/remark-alerts.mjs";
import { remarkImageAssets } from "./lib/image-assets.mjs";

export default defineConfig({
  mdxOptions: { remarkPlugins: [remarkAlerts, remarkImageAssets] },
});
