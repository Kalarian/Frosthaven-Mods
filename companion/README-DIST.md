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
- Stats: the whole party's battles over the campaign: damage round by round, modifier luck, how damage was dealt,
  most played cards, elements, which monsters hurt you most, and records. A scenario counts once it's finished
  (won, lost or resigned); a scenario being played shows as "Live now" and updates every round. The host needs the
  "Battle stats" house rule on.
- Play guide: strategy notes for the campaign (buildings, personal quests, classes).

Starting it
-----------
Unzip the folder anywhere (your Desktop or Documents is fine) and double-click "Frosthaven Companion.exe".
There's nothing to install and nothing is added to Windows. To remove it, delete the folder.

Windows may warn that it's from an unknown publisher (it isn't signed). Click "More info", then "Run anyway".

It opens in its own window. If the window can't open (very old Windows 10 without the WebView2 Runtime), it
opens in your web browser instead and tells you why; it then closes by itself a minute or so after you close
that browser tab.

Updating
--------
When a newer companion is out, the Mods tab says so: click "Update the app". It downloads the new version from the
mod pack's GitHub release, checks it against the release's checksums, replaces its own files (your logs and
settings stay), and reopens by itself. (Version 0.1.0 can't do this yet: unzip 0.1.1 over it once by hand.)
If the folder can't be written to (under Program Files, say), it links to the release page instead.

Files it writes
---------------
- logs\companion.log next to the exe: what it did, and any errors. Send this if something goes wrong.
- webview2\ next to the exe: the window's browser cache.
If the folder isn't writable, these go to %LOCALAPPDATA%\Frosthaven Companion instead.
