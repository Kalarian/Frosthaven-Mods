#!/bin/bash
# Publish a new mod pack release to GitHub (public repo Kalarian/Frosthaven-Mods).
# Usage: bash tools/publish.sh <version> "<one-line release notes>"
# Steps: write VERSION (and the plugin version), build the mod guide PDF (docs/Frosthaven-Mods-Guide.pdf), pack the zip (fails if any patch no longer applies to the game), rebuild the installer
# (embeds the zip), bump the companion's version and build it (bundles that installer and the guides; companion/build.ps1),
# write SHA256SUMS.txt, commit + tag, create the GitHub release with the companion zip, checksums and guide PDF.
# The companion is the only download (the user, 2026-10-29: the standalone installer is dropped). It installs the mods
# with the installer it carries, so every release bumps the companion's version: friends' companions update themselves
# (Mods tab, "Update the app") and bring the new pack with them. The pack zip and installer are built but not uploaded.
set -e
cd "$(dirname "$0")/.."
GH="/c/Program Files/GitHub CLI/gh.exe"
[ -x "$GH" ] || GH=gh
VER="$1"; NOTES="${2:-Mod pack v$1}"
[ -n "$VER" ] || { echo "usage: bash tools/publish.sh <version> \"<notes>\""; exit 1; }
echo "$VER" > VERSION
# the plugin's version (log lines, BepInEx) and its assembly version follow VERSION
sed -i -E "s#<Version>[^<]*</Version>#<Version>$VER</Version>#" plugins/FrosthavenMods/FrosthavenMods.csproj
sed -i -E "s#(public const string Version = \")[^\"]*#\1$VER#" plugins/FrosthavenMods/src/Plugin.cs
python -I tools/build_guide.py
# the content pack's static checks (dead enhancement slots, unknown ability keys) gate the release
python tools/packcheck.py mods/gloomhaven
# and every rule file must load with the game's own loaders (tools/rulecheck; a rejected file = the error screen at start-up)
dotnet run --project tools/rulecheck -c Release
python -I tools/pack_zip.py
# every game hook must still resolve against the installed game (pack_zip just built the plugin)
dotnet run --project tools/hookcheck -c Release
bash tools/build_installer.sh
# the companion app carries this release's installer and mod guide: a new pack is a new companion (its last number goes up)
CVER=$(sed -n 's/^VERSION = "\([^"]*\)".*/\1/p' companion/paths.py)
CVER="${CVER%.*}.$(( ${CVER##*.} + 1 ))"
sed -i -E "s#^VERSION = \"[^\"]*\"#VERSION = \"$CVER\"#" companion/paths.py
powershell -ExecutionPolicy Bypass -File companion/build.ps1
CVER=$(sed -n 's/^VERSION = "\([^"]*\)".*/\1/p' companion/paths.py)
COMPANION="Frosthaven-Companion-$CVER.zip"
cp "companion/dist/$COMPANION" "dist/$COMPANION"
cd dist
sha256sum "$COMPANION" > SHA256SUMS.txt
cat SHA256SUMS.txt
cd ..
git add -A
git commit -q -m "Release v$VER: $NOTES" || echo "(nothing new to commit)"
git tag -f "v$VER"
git push -q origin "HEAD:${PUBLISH_BRANCH:-main}"   # PUBLISH_BRANCH: a release from another branch (v0.9.1 from release-0.9, main holding unreleased classes)
"$GH" release delete "v$VER" --yes --cleanup-tag 2>/dev/null || true   # re-publishing the same version replaces the release
git push -q -f origin "v$VER"
"$GH" release create "v$VER" "dist/$COMPANION" "dist/SHA256SUMS.txt" "docs/Frosthaven-Mods-Guide.pdf" --title "Frosthaven Mods v$VER" --notes "$NOTES"
echo "Published v$VER: https://github.com/Kalarian/Frosthaven-Mods/releases/tag/v$VER"
