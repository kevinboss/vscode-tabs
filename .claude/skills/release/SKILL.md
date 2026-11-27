---
name: release
description: Cut a new release of the VS Code Tabs extension. Bumps the version, tags it, and pushes so the Release workflow builds the win32-x64 .vsix (with the bundled vscode-tabs.exe) and publishes it to GitHub Releases and the VS Code Marketplace. Use when the user wants to ship a release or publish a new version.
---

# Release

Cut a version tag, and GitHub Actions (`.github/workflows/release.yml`) does the building and publishing.
This skill's job is the local prep and the tag.
Run from the repo root.

## Steps

1. **Preflight: abort if any check fails.**
   - On `main` and clean: `git status --porcelain` is empty and `git rev-parse --abbrev-ref HEAD` is `main`.
   - Up to date with origin: `git fetch`, then confirm there are no unpushed or unpulled commits.
   - Green: `dotnet build src/VsCodeTabs -c Release` finishes with no warnings and no errors.
     The repo has no test suites, so the build is the check.
   - Packages: in `extension/`, run `npm ci && npm run package`.
     `vscode:prepublish` publishes the self-contained single-file exe into `extension/bin`, so this proves the `.vsix` the workflow builds will come together.
     Delete the resulting `.vsix` afterwards (it is gitignored, but stale ones pile up in `extension/`).

2. **Choose the version: the minor must be even.**
   Read the current version from `extension/package.json`.
   Otherwise bump per semver: patch for fixes, minor for features, major for breaking changes.
   Take the user's explicit version or bump if given.
   Confirm the target `vX.Y.Z` with the user before writing anything (their preference: confirm before committing).

   **Odd minors belong to the pre-release channel**, which publishes `0.<even+1>.<commit count>` from every push to main.
   So stable steps two at a time: after `0.2.x` the next feature release is `0.4.0`, never `0.3.0`.
   Patches within an even minor are unrestricted (`0.2.1`, `0.2.2`, and so on).
   `release.yml` enforces this: `scripts/publish-version.mjs stable <tag>` fails an odd minor, a tag that disagrees with `package.json`, or a version already on the Marketplace.

3. **Update the changelog.**
   Add a `## [X.Y.Z] - YYYY-MM-DD` section at the top of `extension/CHANGELOG.md` (today's date, Keep a Changelog headings Added / Changed / Fixed / Removed as they apply), plus the matching `[X.Y.Z]: https://github.com/kevinboss/vscode-tabs/releases/tag/vX.Y.Z` link at the bottom.
   Summarize the user-facing changes since the last tag.
   `git log --oneline vPREV..HEAD` gives the raw list, but write for users, not commit by commit.
   The Marketplace renders this file, so a missing entry ships a blank changelog.
   This is enforced: `release.yml` fails the build when the tag has no `## [X.Y.Z]` section, so a forgotten changelog aborts the release after the tag is already pushed.
   Fix it by adding the entry and re-tagging.

4. **Bump.**
   In `extension/`, run `npm version <X.Y.Z> --no-git-tag-version` (updates `package.json` and `package-lock.json`, no commit or tag).
   The `--no-git-tag-version` matters: this skill controls the commit and tag, not npm.
   The csproj stamps the exe's FileVersion from `extension/package.json`, and the update handover between a running and a newly started exe compares those versions, so the bump also has to land before the build in CI.

5. **Commit.**
   Stage `extension/package.json`, `extension/package-lock.json` and `extension/CHANGELOG.md` only, and commit `Release vX.Y.Z` (subject plus a couple of "why" bullets if the release is notable).
   Do not add a `Co-Authored-By` line.

6. **Tag and push.**
   Create an *annotated* tag: `git tag -a vX.Y.Z -m "vX.Y.Z"`.
   This matters: `git push --follow-tags` only pushes annotated tags, so a lightweight `git tag vX.Y.Z` never reaches origin and the workflow silently fails to trigger.
   Then `git push origin main --follow-tags`.
   Confirm the tag actually landed with `git ls-remote --tags origin vX.Y.Z` (must be non-empty).
   If it is empty, push it explicitly with `git push origin vX.Y.Z`.
   The tag must be exactly `v` plus the `extension/package.json` version, or the workflow fails the release.

7. **Watch the pipeline.**
   The tag push triggers the `release` job of the Release workflow.
   Report its URL (`gh run list --workflow=Release --limit 2`, the tag run and the branch run of the same push), optionally `gh run watch <id>`.
   It builds the `.vsix` on Linux (the csproj sets `EnableWindowsTargeting`, so the Windows exe cross-builds), attaches it to a GitHub Release with auto-generated notes, and publishes it to the VS Code Marketplace.

8. **Verify.**
   Once the run succeeds, confirm with `gh release view vX.Y.Z` that `vscode-tabs-win32-x64-X.Y.Z.vsix` is attached, and that the Marketplace listing shows the new version (https://marketplace.visualstudio.com/items?itemName=kevinboss.vscode-tabs).
   Report the release URL.

## Notes

- **The bump commit publishes no pre-release, by design.**
  The `pre-release` job in `release.yml` skips any commit that moves the version in `extension/package.json`, and any tagged commit.
  Without that, cutting a release would publish a stable version and a pre-release of the same code at once.
  The version check is the guard that still works when the bump and the tag are pushed separately, since the branch push carries no tag yet.
  Don't remove either as dead code.
- **Publishing uses Marketplace Trusted Publishing, not a token.**
  A policy on the `kevinboss` publisher trusts `.github/workflows/release.yml`, and `vsce publish --oidc` exchanges the job's GitHub OIDC token for a Marketplace one.
  Renaming or moving that workflow file, or splitting a publish step into another workflow, breaks publishing until the policy is updated.
- Release notes are auto-generated from commits since the last tag (`--generate-notes`).
  For curated notes, edit the release after it is created.
- The repo is public.
  The extension is Windows-only: users install it from the VS Code Marketplace (search "VS Code Tabs"), or download the `.vsix` from the GitHub Release and use "Install from VSIX".
