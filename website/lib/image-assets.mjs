import { createHash } from "node:crypto";
import { readFileSync } from "node:fs";
import { isAbsolute, relative, resolve } from "node:path";

/** Gives local images a content revision so updated screenshots bypass old browser caches. */
export function versionedAssetUrl(src) {
  if (!src.startsWith("/assets/")) return src;
  const url = new URL(src, "https://rmcl.invalid");
  const root = resolve(process.cwd(), "public/assets");
  const file = resolve(
    process.cwd(),
    "public",
    "." + decodeURIComponent(url.pathname),
  );
  const location = relative(root, file);
  if (location.startsWith("..") || isAbsolute(location)) {
    throw new Error(`Image must remain inside public/assets: ${src}`);
  }
  url.searchParams.set(
    "v",
    createHash("sha256").update(readFileSync(file)).digest("hex").slice(0, 12),
  );
  return url.pathname + url.search + url.hash;
}

/** Sends explicit local images through the same build-rendered component as Markdown images. */
export function remarkImageAssets() {
  return (tree) => {
    function visit(node) {
      if (node.name === "img" && Array.isArray(node.attributes)) {
        const source = node.attributes.find(
          (attribute) =>
            attribute.type === "mdxJsxAttribute" && attribute.name === "src",
        );
        if (
          typeof source?.value === "string" &&
          source.value.startsWith("/assets/")
        )
          node.name = "Screenshot";
      }
      if (Array.isArray(node.children)) node.children.forEach(visit);
    }
    visit(tree);
  };
}
