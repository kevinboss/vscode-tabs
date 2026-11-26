// The Marketplace rejects semver pre-release tags, so the channels are kept apart
// by the parity of the minor: stable even, pre-release the next odd one. The
// pre-release patch is the commit count: GITHUB_RUN_NUMBER restarts when a
// workflow is renamed and would collide with published numbers.

import { execFileSync } from "node:child_process";
import { readFileSync } from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const repoRoot = path.join(path.dirname(fileURLToPath(import.meta.url)), "..");
const extensionDir = path.join(repoRoot, "extension");

function die(message, code = 1) {
  console.error(`publish-version: ${message}`);
  process.exit(code);
}

export function parseVersion(text) {
  const m = /^(\d+)\.(\d+)\.(\d+)$/.exec(text.trim());
  return m ? { major: Number(m[1]), minor: Number(m[2]), patch: Number(m[3]) } : undefined;
}

export const formatVersion = (v) => `${v.major}.${v.minor}.${v.patch}`;

export function compareVersions(a, b) {
  return Math.sign(a.major - b.major || a.minor - b.minor || a.patch - b.patch);
}

const highestOf = (versions) =>
  versions.reduce((max, v) => (max === undefined || compareVersions(v, max) > 0 ? v : max), undefined);

const parseAll = (texts) => texts.map((t) => parseVersion(t.replace(/^v/, ""))).filter(Boolean);

export function laneError(stable) {
  if (stable.minor % 2 === 0) {
    return undefined;
  }
  return (
    `extension/package.json is ${formatVersion(stable)}, whose minor is odd.\n` +
    "  Stable releases live on even minors, odd minors belong to the pre-release channel.\n" +
    "  Cut an even-minor release before publishing either lane."
  );
}

export const prereleaseVersion = (stable, build) => ({
  major: stable.major,
  minor: stable.minor + 1,
  patch: build,
});

export function publishBlocker(target, published) {
  const wanted = formatVersion(target);
  if (published.some((p) => p.trim() === wanted)) {
    return `${wanted} is already published, and Marketplace versions are permanent.`;
  }
  const highest = highestOf(
    parseAll(published).filter((v) => v.major === target.major && v.minor === target.minor)
  );
  if (highest !== undefined && compareVersions(target, highest) <= 0) {
    return `${wanted} is not above ${formatVersion(highest)}, the highest published ${target.major}.${target.minor}.x.`;
  }
  return undefined;
}

export function tagBlocker(target, tags) {
  const highest = highestOf(parseAll(tags).filter((v) => compareVersions(v, target) > 0));
  return highest === undefined
    ? undefined
    : `${formatVersion(target)} is behind v${formatVersion(highest)}, already tagged here.`;
}

function git(...args) {
  return execFileSync("git", args, { cwd: repoRoot, encoding: "utf8" });
}

function readManifest() {
  const pkg = JSON.parse(readFileSync(path.join(extensionDir, "package.json"), "utf8"));
  const version = parseVersion(String(pkg.version));
  if (version === undefined) {
    die(`extension/package.json version "${pkg.version}" is not major.minor.patch`);
  }
  return { id: `${pkg.publisher}.${pkg.name}`, version };
}

function commitCount() {
  const n = Number(git("rev-list", "--count", "HEAD").trim());
  if (!Number.isInteger(n) || n <= 0) {
    die("could not read a commit count from git");
  }
  return n;
}

// Fails closed: a version that is already published or lower cannot be ruled out
// without the Marketplace's answer, and a publish cannot be taken back.
function publishedVersions(id) {
  const vsce = path.join(extensionDir, "node_modules", "@vscode", "vsce", "vsce");
  let out;
  try {
    out = execFileSync(process.execPath, [vsce, "show", id, "--json"], {
      cwd: extensionDir,
      encoding: "utf8",
      stdio: ["ignore", "pipe", "pipe"],
      maxBuffer: 16 * 1024 * 1024,
    });
  } catch (e) {
    die(`could not read the published versions of ${id} (${e.message}), refusing to publish`);
  }
  const versions = JSON.parse(out).versions;
  if (!Array.isArray(versions)) {
    die(`unexpected \`vsce show ${id} --json\` output (no versions array)`);
  }
  return versions.map((v) => v?.version).filter((v) => typeof v === "string");
}

function usage() {
  die(
    [
      "usage: node scripts/publish-version.mjs <mode> [tag]",
      "",
      "  prerelease        print the next pre-release version (odd minor, commit count as patch)",
      "  stable <vX.Y.Z>   print the stable version, checking the tag matches package.json",
    ].join("\n"),
    2
  );
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  const [lane, tag] = process.argv.slice(2);
  if (lane !== "stable" && lane !== "prerelease") {
    usage();
  }
  const { id, version: stable } = readManifest();
  const parity = laneError(stable);
  if (parity !== undefined) {
    die(parity);
  }

  let wanted = stable;
  if (lane === "prerelease") {
    wanted = prereleaseVersion(stable, commitCount());
  } else {
    if (tag === undefined) {
      usage();
    }
    if (tag !== `v${formatVersion(stable)}`) {
      die(`tag ${tag} does not match extension/package.json (expected v${formatVersion(stable)})`);
    }
    const behind = tagBlocker(wanted, git("tag", "--list", "v*").split("\n").filter(Boolean));
    if (behind !== undefined) {
      die(behind);
    }
  }

  const blocker = publishBlocker(wanted, publishedVersions(id));
  if (blocker !== undefined) {
    die(blocker);
  }
  console.error(`publish-version: ${lane} -> ${formatVersion(wanted)} (${id})`);
  console.log(formatVersion(wanted));
}
