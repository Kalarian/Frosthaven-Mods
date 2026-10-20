# Mod loader

`BepInEx_5.4.23.5/` contains the unmodified Windows x64 (Mono) loader files from BepInEx 5.4.23.5
(https://github.com/BepInEx/BepInEx/releases), the same build the Stolen Realm pack uses. These are the doorstop
proxy (`winhttp.dll`, `doorstop_config.ini`, `.doorstop_version`) and `BepInEx/core/*.dll` (XML docs left out).

`tools/pack_zip.py` copies them into the pack under `game/`, and the installer puts them in the game folder.

BepInEx is licensed under LGPL-2.1; HarmonyX and MonoMod are MIT; Mono.Cecil is MIT.
