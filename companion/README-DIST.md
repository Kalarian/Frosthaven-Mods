Frosthaven Companion
====================

An optional helper for the Frosthaven mod pack. You don't need it to play with the mods: the plain
"Install Frosthaven Mods.exe" installer does everything the game needs. The companion adds:

- Mods: which mod pack version you have, whether there's a newer one, and one-click update, reinstall or
  removal (it runs the same installer for you). It won't change anything while Frosthaven is running. Below
  that, the mod guide: what each mod does and how to use it.
- Scenario map: every scenario, what unlocks it and what you've done, coloured by your campaign. It updates by
  itself when the game autosaves. The host needs the "Scenario map file" house rule on (Options, Custom Rules).
  Spoilers: it shows every scenario's name.
- Play guide: strategy notes for the campaign (buildings, personal quests, classes).

Starting it
-----------
Unzip the folder anywhere (your Desktop or Documents is fine) and double-click "Frosthaven Companion.exe".
There's nothing to install and nothing is added to Windows. To remove it, delete the folder.

Windows may warn that it's from an unknown publisher (it isn't signed). Click "More info", then "Run anyway".

It opens in its own window. If the window can't open (very old Windows 10 without the WebView2 Runtime), it
opens in your web browser instead and tells you why; it then closes by itself a minute or so after you close
that browser tab.

Files it writes
---------------
- logs\companion.log next to the exe: what it did, and any errors. Send this if something goes wrong.
- webview2\ next to the exe: the window's browser cache.
If the folder isn't writable, these go to %LOCALAPPDATA%\Frosthaven Companion instead.
