#!/bin/bash
# Rebuild the one-file installer around dist/Frosthaven-Mods-pack.zip (run tools/pack_zip.py first).
set -e
cd "$(dirname "$0")/../installer"
[ -f ../dist/Frosthaven-Mods-pack.zip ] || { echo "no dist/Frosthaven-Mods-pack.zip; run python tools/pack_zip.py"; exit 1; }
dotnet build -c Release --no-incremental 2>&1 | grep -E " error |Build succeeded" || true
[ -f "bin/Release/Install Frosthaven Mods.exe" ] || { echo "build failed"; exit 1; }
cp "bin/Release/Install Frosthaven Mods.exe" "../dist/Install Frosthaven Mods.exe"
ls -la "../dist/Install Frosthaven Mods.exe" | awk '{print $5 " bytes -> dist/Install Frosthaven Mods.exe"}'
