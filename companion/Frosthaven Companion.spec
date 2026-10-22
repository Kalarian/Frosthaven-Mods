# -*- mode: python ; coding: utf-8 -*-
# PyInstaller spec for the companion. Build it with build.ps1 (it makes .venv-build from requirements-build.txt,
# stages the guides, installer and VERSION into build-res\, runs PyInstaller and zips dist\Frosthaven Companion).
#
# ONE-FOLDER ONLY (as MnM Meter): a one-file exe unpacks itself to a temp folder on every start, which is slower and
# trips antivirus more often.
#
# What goes in: app.py and what it imports, with ..\tools on the path for the scenario map (scenario_map,
# fh_ruleset), pywebview's WinForms/WebView2 backend (picked at run time, so named as hidden imports), web\ (the
# page and icon) and build-res\ (the mod guide, the play guide when there is one, the mod pack installer, VERSION).
ICON = "web/icon.ico"

a = Analysis(
    ["app.py"],
    pathex=[".", "../tools"],
    binaries=[],
    datas=[("web", "web"), ("build-res", ".")],
    hiddenimports=["clr", "clr_loader", "webview", "webview.platforms.winforms", "webview.platforms.edgechromium",
                   "scenario_map", "fh_ruleset"],
    hookspath=[],
    runtime_hooks=[],
    excludes=["tests", "pytest", "tkinter"],
    noarchive=False,
)
pyz = PYZ(a.pure)
exe = EXE(
    pyz,
    a.scripts,
    exclude_binaries=True,
    name="Frosthaven Companion",
    debug=False,
    strip=False,
    upx=False,
    console=False,
    icon=ICON,
)
coll = COLLECT(
    exe,
    a.binaries,
    a.datas,
    strip=False,
    upx=False,
    name="Frosthaven Companion",
)
