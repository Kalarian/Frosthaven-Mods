#!/bin/bash
# Publish a new mod pack release to GitHub (public repo Kalarian/Frosthaven-Mods).
# Usage: bash tools/publish.sh <version> "<one-line release notes>"
# Steps: write VERSION, build the player guide PDF (docs/Frosthaven-Mods-Guide.pdf), pack the zip (fails if any patch no longer applies to the game), rebuild the installer
# (embeds the zip), write SHA256SUMS.txt, commit + tag, create the GitHub release with the zip, installer and checksums.
# Friends' installers ask GitHub for the latest release and download the zip when it is newer than the one they carry.
set -e
cd "$(dirname "$0")/.."
GH="/c/Program Files/GitHub CLI/gh.exe"
[ -x "$GH" ] || GH=gh
VER="$1"; NOTES="${2:-Mod pack v$1}"
[ -n "$VER" ] || { echo "usage: bash tools/publish.sh <version> \"<notes>\""; exit 1; }
echo "$VER" > VERSION
python -I tools/build_guide.py
python -I tools/pack_zip.py
bash tools/build_installer.sh
cd dist
sha256sum "Frosthaven-Mods-pack.zip" "Install Frosthaven Mods.exe" > SHA256SUMS.txt
cat SHA256SUMS.txt
cd ..
git add -A
git commit -q -m "Release v$VER: $NOTES" || echo "(nothing new to commit)"
git tag -f "v$VER"
git push -q origin main
"$GH" release delete "v$VER" --yes --cleanup-tag 2>/dev/null || true   # re-publishing the same version replaces the release
git push -q -f origin "v$VER"
"$GH" release create "v$VER" "dist/Frosthaven-Mods-pack.zip" "dist/Install Frosthaven Mods.exe" "dist/SHA256SUMS.txt" "docs/Frosthaven-Mods-Guide.pdf" --title "Frosthaven Mods v$VER" --notes "$NOTES"
echo "Published v$VER: https://github.com/Kalarian/Frosthaven-Mods/releases/tag/v$VER"
