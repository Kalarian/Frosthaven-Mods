#!/bin/bash
# Publish a new mod pack release to GitHub (public repo Kalarian/Frosthaven-Mods).
# Usage: bash tools/publish.sh <version> "<one-line release notes>"
# Steps: write VERSION (and the plugin version), build the mod guide PDF (docs/Frosthaven-Mods-Guide.pdf), pack the zip (fails if any patch no longer applies to the game), rebuild the installer
# (embeds the zip), build the companion app (bundles that installer and the guides; companion/build.ps1), write
# SHA256SUMS.txt, commit + tag, create the GitHub release with the zip, installer, companion zip and checksums.
# Friends' installers ask GitHub for the latest release and download the zip when it is newer than the one they carry.
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
# the companion app: its version is its own (companion/paths.py); it carries this release's installer and mod guide
powershell -ExecutionPolicy Bypass -File companion/build.ps1
CVER=$(sed -n 's/^VERSION = "\([^"]*\)".*/\1/p' companion/paths.py)
COMPANION="Frosthaven-Companion-$CVER.zip"
cp "companion/dist/$COMPANION" "dist/$COMPANION"
cd dist
sha256sum "Frosthaven-Mods-pack.zip" "Install Frosthaven Mods.exe" "$COMPANION" > SHA256SUMS.txt
cat SHA256SUMS.txt
cd ..
git add -A
git commit -q -m "Release v$VER: $NOTES" || echo "(nothing new to commit)"
git tag -f "v$VER"
git push -q origin main
"$GH" release delete "v$VER" --yes --cleanup-tag 2>/dev/null || true   # re-publishing the same version replaces the release
git push -q -f origin "v$VER"
"$GH" release create "v$VER" "dist/Frosthaven-Mods-pack.zip" "dist/Install Frosthaven Mods.exe" "dist/$COMPANION" "dist/SHA256SUMS.txt" "docs/Frosthaven-Mods-Guide.pdf" --title "Frosthaven Mods v$VER" --notes "$NOTES"
echo "Published v$VER: https://github.com/Kalarian/Frosthaven-Mods/releases/tag/v$VER"
