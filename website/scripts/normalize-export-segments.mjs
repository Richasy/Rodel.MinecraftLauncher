import { readdir, readFile, rename } from "node:fs/promises";
import path from "node:path";
import { fileURLToPath } from "node:url";

// Next 16.4.2 exports segment request names with Windows separators.
// Browser requests use dots. Normalize only those generated output files;
// never patch the dependency or touch source/content files.
const output = path.resolve(
  path.dirname(fileURLToPath(import.meta.url)),
  "../out",
);
const boundary = output + path.sep;

function owned(file) {
  const resolved = path.resolve(file);
  if (!resolved.startsWith(boundary)) throw new Error("Export path left out/");
  return resolved;
}

async function files(directory) {
  const entries = await readdir(directory, { withFileTypes: true });
  const nested = await Promise.all(
    entries.map(async (entry) => {
      const target = owned(path.join(directory, entry.name));
      return entry.isDirectory() ? files(target) : [target];
    }),
  );
  return nested.flat();
}

let normalized = 0;
for (const source of await files(output)) {
  const parts = path.relative(output, source).split(path.sep);
  const marker = parts.findIndex((part) => part.startsWith("__next."));
  if (marker < 0 || marker === parts.length - 1 || !source.endsWith(".txt"))
    continue;
  const target = owned(
    path.join(output, ...parts.slice(0, marker), parts.slice(marker).join(".")),
  );
  try {
    const existing = await readFile(target);
    const original = await readFile(source);
    if (!existing.equals(original))
      throw new Error("Conflicting exported segment: " + target);
    // Preserve an identical existing output; a clean Next export normally has none.
    continue;
  } catch (error) {
    if (error.code !== "ENOENT") throw error;
  }
  await rename(owned(source), target);
  normalized++;
}
process.stdout.write("Normalized " + normalized + " static segment paths.\n");
