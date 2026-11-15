import { createHash } from "node:crypto";
import {
  mkdir,
  mkdtemp,
  readdir,
  readFile,
  stat,
  utimes,
  writeFile,
} from "node:fs/promises";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { gzipSync } from "node:zlib";

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const source = path.join(root, "out");
const build = path.join(root, "build");
await mkdir(build, { recursive: true });
const destination = await mkdtemp(path.join(build, "publish-"));
const allowed = new Set([
  ".html",
  ".js",
  ".css",
  ".json",
  ".txt",
  ".md",
  ".xml",
  ".png",
  ".webp",
  ".svg",
  ".ico",
]);
const text = new Set([
  ".html",
  ".js",
  ".css",
  ".json",
  ".txt",
  ".md",
  ".xml",
  ".svg",
]);
const manifest = [];
let skippedMaps = 0;
let gzipFiles = 0;
let originalBytes = 0;
let compressedTextBytes = 0;

async function copy(directory) {
  for (const entry of await readdir(directory, { withFileTypes: true })) {
    const file = path.join(directory, entry.name);
    if (entry.isSymbolicLink())
      throw new Error(`Symlink in static export: ${file}`);
    if (entry.isDirectory()) {
      await copy(file);
      continue;
    }
    const extension = path.extname(entry.name).toLowerCase();
    if (extension === ".map") {
      skippedMaps++;
      continue;
    }
    if (!allowed.has(extension) || entry.name.startsWith("."))
      throw new Error(`Unexpected public file: ${file}`);
    const bytes = await readFile(file);
    if (
      [".js", ".css"].includes(extension) &&
      /[#@]\s*sourceMappingURL\s*=/.test(bytes.toString("utf8"))
    )
      throw new Error(`Source map reference in ${file}`);
    const relative = path.relative(source, file);
    const target = path.join(destination, relative);
    await mkdir(path.dirname(target), { recursive: true });
    await writeFile(target, bytes);
    const { mtime } = await stat(file);
    await utimes(target, mtime, mtime);
    manifest.push({
      path: relative.replaceAll(path.sep, "/"),
      bytes: bytes.length,
      sha256: createHash("sha256").update(bytes).digest("hex"),
    });
    originalBytes += bytes.length;
    if (text.has(extension)) {
      const compressed = gzipSync(bytes, { level: 9 });
      if (bytes.length >= 256 && compressed.length < bytes.length * 0.95) {
        await writeFile(target + ".gz", compressed);
        await utimes(target + ".gz", mtime, mtime);
        gzipFiles++;
        compressedTextBytes += compressed.length;
      } else compressedTextBytes += bytes.length;
    }
  }
}

await copy(source);
if (!manifest.some((file) => file.path === "index.html"))
  throw new Error("Static export has no homepage");
const manifestPath = destination + ".manifest.json";
await writeFile(manifestPath, JSON.stringify(manifest, null, 2) + "\n");
process.stdout.write(
  JSON.stringify({
    directory: destination,
    manifest: manifestPath,
    files: manifest.length,
    gzipFiles,
    skippedMaps,
    originalBytes,
    compressedTextBytes,
  }) + "\n",
);
