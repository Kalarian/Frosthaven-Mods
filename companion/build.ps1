# Builds the shareable companion folder and zip: dist\Frosthaven Companion\ and dist\Frosthaven-Companion-<version>.zip.
# Run from anywhere:  powershell -ExecutionPolicy Bypass -File companion\build.ps1
# Needs Python 3.13 (py launcher) and the mod pack installer built first (..\dist\Install Frosthaven Mods.exe: run
# tools\publish.sh, or tools\pack_zip.py then tools\build_installer.sh). The build tools live in .venv-build next to
# this script, never in the system Python. The zip is made from dist\ only.
$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot

$venv = ".venv-build"
$python = Join-Path $venv "Scripts\python.exe"
$pyinstaller = Join-Path $venv "Scripts\pyinstaller.exe"
$out = "dist\Frosthaven Companion"

if (-not (Test-Path $python)) {
    Write-Host "Creating $venv ..."
    py -3.13 -m venv $venv
}
& $python -m pip install -q -r requirements-build.txt
if ($LASTEXITCODE -ne 0) { throw "pip install failed" }

# What the exe carries besides its code: the guides, the installer and the pack version, staged in build-res\
$res = "build-res"
if (Test-Path $res) { Remove-Item -Recurse -Force $res }
New-Item -ItemType Directory -Force "$res\guides", "$res\installer" | Out-Null
$installer = "..\dist\Install Frosthaven Mods.exe"
if (-not (Test-Path $installer)) { throw "no ${installer}: build the mod pack installer first" }
Copy-Item $installer "$res\installer\"
Copy-Item "..\docs\guide\guide.html" "$res\guides\mod-guide.html"
Copy-Item "..\VERSION" "$res\VERSION"
$playGuide = "C:\Claude\FrosthavenGuide\frosthaven-play-guide.html"
if (Test-Path $playGuide) { Copy-Item $playGuide "$res\guides\play-guide.html" }
else { Write-Host "No play guide at $playGuide; the Play guide tab will be hidden." }

if (Test-Path $out) { Remove-Item -Recurse -Force $out }
& $pyinstaller --noconfirm --clean --log-level WARN "Frosthaven Companion.spec"
if ($LASTEXITCODE -ne 0) { throw "PyInstaller failed" }

if (-not ((Get-Content "paths.py" -Raw) -match 'VERSION\s*=\s*"([^"]+)"')) { throw "no VERSION in paths.py" }
$ver = $Matches[1]

Copy-Item "README-DIST.md" (Join-Path $out "README.txt")
# Next to the exe, where .NET reads it: lets a zip unpacked with Explorer's Extract All (every file marked as
# downloaded) still load pythonnet and WebView2 (found on MnM Meter, 2026-09-23).
Copy-Item "Frosthaven Companion.exe.config" (Join-Path $out "Frosthaven Companion.exe.config")

$zip = "dist\Frosthaven-Companion-$ver.zip"
Compress-Archive -Force -Path $out -DestinationPath $zip
$mb = [math]::Round((Get-Item $zip).Length / 1MB, 1)
Write-Host ""
Write-Host "Built $((Resolve-Path $zip).Path)  ($mb MB)"
