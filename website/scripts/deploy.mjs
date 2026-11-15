import assert from "node:assert/strict";
import { execFile as execFileCallback } from "node:child_process";
import { createHash } from "node:crypto";
import { appendFile, mkdtemp, readFile, rm, writeFile } from "node:fs/promises";
import os from "node:os";
import path from "node:path";
import { promisify } from "node:util";

const execFile = promisify(execFileCallback);
const env = process.env;
function required(name, pattern) {
  const value = env[name];
  assert.ok(
    value && (!pattern || pattern.test(value)),
    `Missing or invalid ${name}`,
  );
  return value;
}
const host = required("WEBSITE_SSH_HOST", /^[a-zA-Z0-9.-]+$/);
const user = required("WEBSITE_SSH_USER", /^[a-z_][a-z0-9_-]*$/);
const port = required("WEBSITE_SSH_PORT", /^\d{1,5}$/);
const root = required("WEBSITE_DEPLOY_ROOT", /^\/home\/www\/[a-zA-Z0-9_-]+$/);
const site = new URL(required("WEBSITE_URL"));
assert.equal(site.protocol, "https:");
assert.equal(site.hostname, "rmcl.richasy.net");
assert.equal(required("GITHUB_REF"), "refs/heads/main");
const commit = required("GITHUB_SHA", /^[a-f0-9]{40}$/);
const run = required("GITHUB_RUN_ID", /^\d+$/);
const attempt = required("GITHUB_RUN_ATTEMPT", /^\d+$/);
const release = `${run}-${attempt}-${commit.slice(0, 12)}`;
const marker = {
  commit,
  run,
  attempt,
  release,
  publishedAt: new Date().toISOString(),
};
const temporary = await mkdtemp(
  path.join(env.RUNNER_TEMP || os.tmpdir(), "rmcl-deploy-"),
);
if (env.GITHUB_ENV) {
  await appendFile(env.GITHUB_ENV, `RMCL_DEPLOY_TEMP=${temporary}\n`);
}
let previous;
const target = `${user}@${host}`;
const incoming = `${root}/incoming/${release}`;
const key = path.join(temporary, "id_ed25519");
const knownHosts = path.join(temporary, "known_hosts");
const options = [
  "-i",
  key,
  "-o",
  "IdentitiesOnly=yes",
  "-o",
  "BatchMode=yes",
  "-o",
  "StrictHostKeyChecking=yes",
  "-o",
  `UserKnownHostsFile=${knownHosts}`,
  "-o",
  "ConnectTimeout=20",
  "-o",
  "ServerAliveInterval=15",
  "-o",
  "ServerAliveCountMax=4",
];
async function ssh(command) {
  return (
    await execFile("ssh", [...options, "-p", port, target, command], {
      timeout: 240_000,
    })
  ).stdout.trim();
}
let requestNumber = 0;
const curl = process.platform === "win32" ? "curl.exe" : "curl";
async function request(url, status = 200) {
  const prefix = path.join(temporary, `response-${++requestNumber}`);
  const body = prefix + ".body";
  const headersFile = prefix + ".headers";
  try {
    const { stdout } = await execFile(
      curl,
      [
        "--silent",
        "--show-error",
        "--compressed",
        "--max-time",
        "30",
        "--proto",
        "=https",
        "--header",
        "Cache-Control: no-cache",
        "--output",
        body,
        "--dump-header",
        headersFile,
        "--write-out",
        "%{http_code}",
        url.toString(),
      ],
      { timeout: 40_000 },
    );
    const code = Number(stdout.trim());
    assert.equal(code, status, `${url}: HTTP ${code}`);
    const headers = new Headers();
    for (const line of (await readFile(headersFile, "utf8")).split(/\r?\n/)) {
      const colon = line.indexOf(":");
      if (colon > 0)
        headers.set(line.slice(0, colon), line.slice(colon + 1).trim());
    }
    return new Response(await readFile(body), { status: code, headers });
  } finally {
    await Promise.all([
      rm(body, { force: true }),
      rm(headersFile, { force: true }),
    ]);
  }
}
async function verify() {
  const url = (pathname) => new URL(pathname, site).href;
  const current = await (
    await request(url(`/deployment.json?run=${run}-${attempt}`))
  ).json();
  assert.equal(
    current.release,
    release,
    "Public site does not serve this release",
  );
  const sitemap = await (await request(url("/sitemap.xml"))).text();
  const paths = [...sitemap.matchAll(/<loc>([^<]+)<\/loc>/g)].map(
    (match) => new URL(match[1]).pathname,
  );
  assert.ok(paths.length >= 2, "Sitemap has no documentation");
  const images = new Map();
  const queue = [...paths];
  await Promise.all(
    Array.from({ length: 6 }, async () => {
      while (queue.length) {
        const pathname = queue.shift();
        const response = await request(url(pathname));
        assert.match(response.headers.get("content-type"), /text\/html/);
        const html = await response.text();
        for (const match of html.matchAll(
          /<img\b[^>]*\bsrc="(\/assets\/[^\"]+)"/g,
        )) {
          const asset = new URL(match[1].replaceAll("&amp;", "&"), site);
          images.set(asset.pathname, asset);
        }
      }
    }),
  );
  const imageQueue = [...images.values()];
  await Promise.all(
    Array.from({ length: 6 }, async () => {
      while (imageQueue.length) {
        const asset = imageQueue.shift();
        const bytes = Buffer.from(await (await request(asset)).arrayBuffer());
        const source = await readFile(
          path.join("public", decodeURIComponent(asset.pathname)),
        );
        assert.ok(
          bytes.equals(source),
          `Public screenshot differs: ${asset.pathname}`,
        );
      }
    }),
  );
  for (const pathname of [
    "/this-page-must-not-exist/",
    "/.env",
    "/_next/static/missing.js.map",
  ])
    await request(url(pathname), 404);
  console.log(
    `Verified production: ${paths.length} pages, ${images.size} images, release ${release}.`,
  );
  return { pages: paths.length, images: images.size };
}

try {
  await execFile(curl, ["--version"]);
  await writeFile(key, required("WEBSITE_SSH_KEY").trim() + "\n", {
    mode: 0o600,
  });
  await writeFile(
    knownHosts,
    required("WEBSITE_SSH_KNOWN_HOSTS").trim() + "\n",
    { mode: 0o600 },
  );
  if (process.platform === "win32") {
    const { stdout } = await execFile("whoami", ["/user", "/fo", "csv", "/nh"]);
    const sid = stdout.match(/S-1-[\d-]+/)[0];
    await execFile("icacls", [
      key,
      "/inheritance:r",
      "/grant:r",
      `*${sid}:(R)`,
    ]);
  }
  await writeFile("out/deployment.json", JSON.stringify(marker) + "\n");
  const { stdout } = await execFile(process.execPath, [
    "scripts/package-static.mjs",
  ]);
  const packed = JSON.parse(stdout);
  console.log(
    `Packaged ${packed.files} files and ${packed.gzipFiles} gzip copies.`,
  );
  const archive = path.join(temporary, "site.tar.gz");
  await execFile("tar", ["-czf", archive, "-C", packed.directory, "."], {
    timeout: 120_000,
  });
  const digest = createHash("sha256")
    .update(await readFile(archive))
    .digest("hex");
  await ssh(`mkdir -m 700 '${incoming}'`);
  await execFile(
    "scp",
    [
      ...options,
      "-P",
      port,
      archive,
      "deploy/release.sh",
      `${target}:${incoming}/`,
    ],
    { timeout: 240_000 },
  );
  previous = await ssh(
    `bash '${incoming}/release.sh' publish '${root}' '${release}' '${digest}'`,
  );
  assert.match(previous, /^releases\/[a-zA-Z0-9_-]+$/);
  const verified = await verify();
  if (env.GITHUB_STEP_SUMMARY) {
    await appendFile(
      env.GITHUB_STEP_SUMMARY,
      `Deployed [${site.hostname}](${site.href}) from \`${commit}\`.\n\nVerified ${verified.pages} pages and ${verified.images} images. Release: \`${release}\`.\n\nPrevious release: \`${previous}\`.\n`,
    );
  }
  try {
    await ssh(
      `rm -f '${incoming}/site.tar.gz' '${incoming}/release.sh'; rmdir '${incoming}'`,
    );
  } catch {
    console.warn(
      "Published successfully; temporary upload files remain in incoming/.",
    );
  }
} catch (error) {
  if (previous) {
    try {
      console.error(
        await ssh(
          `bash '${incoming}/release.sh' rollback '${root}' '${release}' '${previous}'`,
        ),
      );
    } catch (rollbackError) {
      console.error("Automatic rollback failed:", rollbackError.message);
    }
  }
  throw error;
} finally {
  await rm(temporary, { recursive: true, force: true });
}
