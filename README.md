# Frosthaven mod pack

Mods for the digital Frosthaven (Snapshot Games, Steam app 2347080), made for a group of friends.

> **Co-op:** everyone in a session must run the same mod pack version. The game only checks the game version
> on join and then compares game-state hashes during play, so mismatched mods show up as a desync mid-scenario,
> not as a join error.

**Players:** download `Install Frosthaven Mods.exe` from the
[latest release](https://github.com/Kalarian/Frosthaven-Mods/releases/latest), close the game, run it, and click
Install / Update. Each run checks GitHub, so the same exe always installs the newest pack. "Remove mods" puts the
game's original rules back. Saves are never touched.

Status: pipeline and installer done and tested. A modified `Base.ruleset` loads in game (verified 2026-10-20 with
the `TestBannerSpearHP` mod: Banner Spear showed 20 HP at level 1, no YML errors in Player.log). No release yet,
since there are no real mods yet.

---

## How the mods work

All game rules (characters, ability cards, monsters, items, perks, scenarios, ...) live in one archive,
`Frosthaven_Data/StreamingAssets/Rulebase/Base.ruleset`, as plain-text YML. A mod here is a set of small patches
against those files. Patches are applied on each player's own PC to their own copy of the game file, so:

- no game files are redistributed, only the changed lines plus a little context;
- after a game update, patches reapply to the new file, and one that no longer fits fails with a clear message
  instead of silently undoing the game's changes.

Mod layout, `mods/<Name>/`:
- `mod.json`: `{"name", "description", "enabled"}` (disabled mods are left out of release builds)
- `patches/<path>.diff`: a unified diff against `<path>` inside the ruleset
- `files/<path>`: a new file added to the ruleset (its first line `Parser: <Type>` decides where it loads)

## Code mods (house rules)

`plugins/FrosthavenMods/` is a BepInEx 5 plugin (`frosthaven.mods.dll`, netstandard2.1, HarmonyX). The loader is in
`loader/` (see its README). Every code feature is a **campaign house rule**:
- The game has a House Rules screen (Options > House Rules), but it's hidden in this build because
  `GameSettingsService.ValidCampaignRules` is `None`. The plugin shows it for campaigns and adds our own switches.
- Our rules are extra bits (from `1 << 24` up) in the game's own campaign rules number (`PartyAdventureData.Rules`).
  The game saves that number in the save header and sends it to co-op players (join token + `ChangeHouseRuleSettings`),
  so the host's choice reaches everyone and is stored in the campaign. The vanilla game ignores unknown bits.
- The plugin fixes three game bugs on the way:
  - `ApplyHeader` never reads the rules back, so they reset to off on every reload.
  - The screen's callbacks capture the rules from when the window opened, so flipping two switches undid the first.
  - The window's row list is never emptied.
- Rules so far:
  - **Give resources** (bit 24, released in v0.2.0 as "Shared gold"). In the outpost, the party column gets a
    Ctrl+left-click "Give" action on each portrait you control (wraps the portrait's own Select click; middle-click is
    taken by the game's character summary). It picks a recipient (party, bench, or a character
    being created; not retired), then gold, lumber, metal or hide (list window), then an amount. The
    picker reopens for more. The custom multiplayer command `FrosthavenMods.GiveResources` is resolved by a prefix on
    `MultiplayerGameController.GetExectionContext`, so it runs on every machine like SellItem/BuyItem, with the host
    validating it. Limits:
    - The giver must be in the party, have played at least 1 scenario, and have the amount.
    - Herbs aren't included: the digital game keeps herbs only in the shared Frosthaven supply.
    - Gold given to a character in creation is lost if not spent before creation finishes; materials are kept.
    - Every player needs the mod: a peer without it fails when the command arrives.
  - **Item trading** (bit 25): a switch only so far; does nothing yet.

The installer copies `game/*` from the pack into the game folder and records every file it placed in `state.json`.
A BepInEx someone else installed is kept (only our plugin is added). Uninstall removes only what we placed, plus
BepInEx's own cache/config/logs when the loader is ours. Other people's plugins are never touched.

## Tools (Python 3, stdlib only)

`tools/fh_mods.py`: the mod pipeline
```
python tools/fh_mods.py edit <Mod> <path/in/ruleset.yml>...   # copies vanilla files into work/<Mod>/ to edit
python tools/fh_mods.py diff <Mod>                            # work/<Mod>/ -> mods/<Mod>/patches + files
python tools/fh_mods.py install [--mod NAME ...]              # build + install into the game (vanilla backed up)
python tools/fh_mods.py restore                               # put vanilla back
python tools/fh_mods.py status
python tools/fh_mods.py build <out.ruleset> [--mod NAME ...]
```
The vanilla file is kept in `<game>\FrosthavenMods\Base.ruleset.<steam build id>` next to `state.json`. The
installer exe uses the same folder and format, so the two tools always agree. A fresh backup is taken when Steam
updates the game. A `Base.ruleset` neither tool can account for is never touched: run Steam's "Verify integrity of
game files" instead.

`tools/fh_ruleset.py`: low-level archive tool (`info`, `verify`, `unpack`, `pack`). The vanilla extract used for
authoring is made with `python tools/fh_ruleset.py unpack <Base.ruleset> extracted/Base`. It stays on this machine.

## Ruleset format

Reverse-engineered from `FH.YML.RulebaseFile` in `Assembly-CSharp.dll`:
- Header, 16 bytes: `char[12] "02392fe1"` (a fixed tag, not a checksum) + `uint32 count`
- Entry, 144 bytes: `char[128] name`, `int32 parserType` (`YMLLoading.EYMLParserType`), `int32 isBinary`,
  `uint32 offset`, `uint32 size`
- Data: raw file bodies back to back. No compression or encryption, and every Base entry is text (or `.lvldat`).

The game loads entries in index order, batched by runs of the same parser type. Enum order = load order.

## Notes

- Engine: Unity 2022.3.71f1, Mono. BepInEx code mods are possible later, using the same installer approach as
  the Stolen Realm pack.
- The Gloomhaven Digital mod system (rulesets, Workshop, Level Editor) is still in the code but not enabled in
  Frosthaven (`IsModdingEnabled`).

## Releasing

```
bash tools/publish.sh <version> "<one-line notes>"
```
This writes `VERSION`, runs `tools/pack_zip.py` (every enabled mod, and the release stops if any patch no longer
applies to the current game), builds `installer/` with the zip embedded (`tools/build_installer.sh`, .NET SDK), writes
`SHA256SUMS.txt`, commits, tags, and creates the GitHub release with the zip, exe and checksums. Build output goes
to `dist/`, which is not committed.

Installer (`installer/Program.cs`, .NET Framework 4.8 WinForms, one file): it finds the game through the Steam
libraries, downloads a newer pack from the latest release (checksum-verified), rebuilds `Base.ruleset` from the
vanilla backup plus every mod in the pack, and refuses to run while the game is open. Headless mode for testing:
`"Install Frosthaven Mods.exe" "<game folder>" install [offline]` / `uninstall`, which writes
`FrosthavenMods\install-log.txt`. Its output is byte-identical to `fh_mods.py build` for the same mods.

Licence: MIT (the tools and patches; Frosthaven and its game data belong to their owners).
