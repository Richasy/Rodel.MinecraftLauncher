import assert from "node:assert/strict";
import { createHash } from "node:crypto";
import { readdir, readFile, stat } from "node:fs/promises";
import path from "node:path";
import { fileURLToPath } from "node:url";

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const out = path.join(root, "out");
const origin = "https://rmcl.richasy.net";

async function files(directory, suffix) {
  const entries = await readdir(directory, { withFileTypes: true });
  const nested = await Promise.all(
    entries.map((entry) => {
      const file = path.join(directory, entry.name);
      return entry.isDirectory()
        ? files(file, suffix)
        : file.endsWith(suffix)
          ? [file]
          : [];
    }),
  );
  return nested.flat();
}

function decode(value) {
  return value
    .replace(/&amp;/g, "&")
    .replace(/&#(\d+);/g, (_, code) => String.fromCodePoint(Number(code)));
}

function tags(html, tag) {
  return [...html.matchAll(new RegExp(`<${tag}\\b[^>]*>`, "g"))].map((match) =>
    Object.fromEntries(
      [...match[0].matchAll(/([\w:-]+)="([^"]*)"/g)].map((attribute) => [
        attribute[1],
        decode(attribute[2]),
      ]),
    ),
  );
}

const zh = await files(path.join(root, "content/docs"), ".mdx");
const en = await files(path.join(root, "content/en/docs"), ".mdx");
const relative = (base, list) =>
  list.map((file) => path.relative(base, file)).sort();
assert.deepEqual(
  relative(path.join(root, "content/docs"), zh),
  relative(path.join(root, "content/en/docs"), en),
  "Chinese and English article paths differ",
);
for (const file of [...zh, ...en]) {
  const content = await readFile(file, "utf8");
  const headings = [...content.matchAll(/^#{2,4} (.+)$/gm)].map(
    (match) => match[1],
  );
  assert.equal(
    new Set(headings).size,
    headings.length,
    `Duplicate heading: ${file}`,
  );
  assert.ok(
    !/^#{2,4} [^\n]+\n\s*\n#{2,4} /m.test(content),
    `Empty section: ${file}`,
  );
}

const pages = new Map();
for (const file of await files(out, ".html")) {
  const html = await readFile(file, "utf8");
  pages.set(file, {
    ids: new Set(
      [...html.matchAll(/\bid="([^"]*)"/g)].map((match) => decode(match[1])),
    ),
    links: tags(html, "a"),
    images: tags(html, "img"),
  });
}
const assets = new Set();
for (const [file, page] of pages) {
  const pagePath = "/" + path.relative(out, file).replaceAll(path.sep, "/");
  for (const { href } of page.links) {
    if (!href) continue;
    const url = new URL(href, origin + pagePath);
    if (url.origin !== origin) continue;
    let target = path.join(out, decodeURIComponent(url.pathname));
    if (!path.extname(target) || (await stat(target)).isDirectory())
      target = path.join(target, "index.html");
    assert.ok((await stat(target)).isFile(), `Missing link ${href} in ${file}`);
    if (url.hash && pages.has(target)) {
      assert.ok(
        pages.get(target).ids.has(decodeURIComponent(url.hash.slice(1))),
        `Missing anchor ${href} in ${file}`,
      );
    }
  }
  for (const img of page.images) {
    assert.ok("alt" in img, `Image has no alt attribute: ${file}`);
    const url = new URL(img.src, origin);
    if (url.origin !== origin) continue;
    const target = path.join(out, decodeURIComponent(url.pathname));
    const bytes = await readFile(target);
    if (!url.pathname.startsWith("/assets/")) continue;
    assets.add(url.pathname);
    const source = await readFile(
      path.join(root, "public", decodeURIComponent(url.pathname)),
    );
    assert.ok(bytes.equals(source), `Stale exported image: ${url.pathname}`);
    const digest = createHash("sha256")
      .update(source)
      .digest("hex")
      .slice(0, 12);
    if (
      /^\/(?:assets)\/(?:zh|en)-/.test(url.pathname) ||
      url.searchParams.has("v")
    ) {
      assert.equal(
        url.searchParams.get("v"),
        digest,
        `Incorrect image revision: ${img.src}`,
      );
    }
  }
}
console.log(
  `Checked ${zh.length} bilingual article pairs, ${pages.size} HTML pages and ${assets.size} referenced images.`,
);
