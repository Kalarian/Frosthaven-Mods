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
  - **Tradeable resources and equipment** (bit 24; released in v0.2.0 as "Shared gold", then built as two rules,
    Give resources and Item trading, merged into one; bit 25 is unused). In the outpost, Ctrl+left-click the portrait
    of a character you control in the party column, or use the GIVE button under the name in the top bar. The
    Ctrl+click wraps the portrait's own Select click; middle-click is taken by the game's character summary. The game's Building Donation table opens, filled with our data
    (`GiveResourcesViewState`), with **Resources** and **Items** tab buttons in the window's separator row (`GiveTabs`).
    Resources tab:
    - Columns: the rest of the party (plus a character being created into a party slot), so at most 3. The bench was
      tried, but the table doesn't scroll well past 6 columns, and giving to the bench is rare.
    - Rows: gold, lumber, metal, hide. Each row header counts down the giver's amount left.
    - Click +1, right-click -1 (from the table); Ctrl+click +/-10 and Shift+click all/none (ours). Esc closes it
      (and the pause menu doesn't also open).
    - Nothing is sent until **Give**, which sends one `FrosthavenMods.GiveResources` command per filled cell.

    The command is resolved by a prefix on `MultiplayerGameController.GetExectionContext`, so it runs on every machine
    like SellItem/BuyItem, with the host
    validating it. Limits:
    - The giver must be in the party and have the amount. (A "played at least 1 scenario" check was removed for
      testing; it guarded against passing on a new character's starting purchases.)
    - Herbs aren't included: the digital game keeps herbs only in the shared Frosthaven supply.
    - Gold given to a character in creation is lost if not spent before creation finishes; materials are kept.
    - Every player needs the mod: a peer without it fails when the command arrives.

    Items tab: one row per item the giver owns (worn or not), with the game's item card on hover; a cell is an on/off
    pick, laid out like the game's item reward window. A receiver who already owns a copy is marked and locked. Long lists use the table's own vertical scroll view. **Give** sends the
    queued gifts from both tabs, one `FrosthavenMods.GiveItem` command per item:
    - `Action_RemoveItem` (the selling path: unequips, back to the supply, re-checks small item slots), then
      `Action_GainItem` (the buying path: out of the supply again), then equip if a slot is free.
    - Receivers: party members only (not a character in creation, who could buy with starting gold and pass it on).
  - **Share duplicate loot** (bit 26). Board game rule (p. 32): a looted copy of an item you already own goes to
    another character or is sold; the digital game always sells it. A prefix on `CharacterController.Action_GainItem`
    for source `"scenario_loot"` puts the duplicate in the game's own items-to-gain list (`ItemToGainIDs` +
    `GainItem` unresolved action), so the outpost opens the usual item reward window. Falls back to the auto-sell when
    nobody else can own it or the reward window couldn't hand out every pending item.
  - Item reward window (all item rewards, not a switch; `RewardWindowPatches`): opens with nothing assigned instead of
    the game's pre-assignment, and left-clicking another character moves an assigned item there in one click
    (unassign + assign through the window's own synced `UpdateItemsDistribution`).

The installer copies `game/*` from the pack into the game folder and records every file it placed in `state.json`.
A BepInEx someone else installed is kept (only our plugin is added). Uninstall removes only what we placed, plus
BepInEx's own cache/config/logs when the loader is ours. Other people's plugins are never touched.

## Battle stats (always on, display only)

The post-scenario Statistics page (the screen with XP and accolades) gets about 40 extra rows in six sections
(`BattleStats`). The game records ~40 kinds of events per character per scenario (`CPlayerStats` lists, filled by
`StatsDataStorage.ScrapeEventLog`) but its table sums only 8; ours are computed from the same records
(`Party.LastScenarioStats`). What the records mean was checked in the game code (see the comments in `BattleStats.cs`):
summons count as part of their owner (the game already files their damage, kills and heals on the owner; their
modifier draws, abilities and elements are matched back from the game's summon pool by summon GUID / class).

"This scenario" / "Campaign" buttons in the page header switch to campaign-long totals (`CampaignStats`): per
character (class + name), sums for counts, bests for "biggest" rows, campaign-wide ratios for averages. Stored in
`<game>\FrosthavenMods\stats\campaign-<campaign name>.txt`, one file per campaign, per player. The game's own lifetime
records (`CPlayerRecords`) are never updated by the game, so totals start with the mod. Each scenario's raw records
also go to `<game>\FrosthavenMods\stats\scenario-*.txt` for checking.

## Desync recorder (always on in online games, logging only)

`DesyncRecorder` logs what's needed to find the cause of a co-op desync, on every player's PC, into
`<game>\FrosthavenMods\desync\<launch time>\` (last 10 launches kept):
- `timeline.txt`: every game action sent / queued / applied (`[player:n]` IDs), side actions, the game's state
  fingerprints at each checkpoint (`DesyncDetector.GenerateStateDumpAndFastAutoSave`: important + full MD5) with the
  positions of the scenario's four RNGs, outpost commands (from `CoopLog`), sync checks and desync events.
- `steps.txt`: every rules-engine message the PC processes (`Choreographer.ProcessMessage`), the finest order.
- On a desync: this PC's state dump, the game's own dumps and one-line diff (the game builds them but its
  `StateDumper.SaveStateDump` is empty in retail), and a copy of `Player.log`. A "desync now" signal rides on the
  game's `LogReproStep` text message, so every player saves their state at the same moment (the host relays it; only
  clients run the game's check). At most 5 captures per launch.

Ctrl+F7 zips the newest 3 launches + `coop-log.txt` + `Player.log` / `Player-prev.log` to the desktop.
`python tools/desync_compare.py <zip> <zip> [...]` lines the players up: desync events, the first checkpoint whose
fingerprints differ, the first difference in applied-action order and in rules-engine steps, and the differing lines
of the state dumps.

## Co-op testing

`docs/COOP_TEST.md` is the two-player test script. The mod logs every outpost command, rule change, gift (resources
and items), duplicate loot and Ctrl+F8 checkpoint (gold, materials, owned and worn items per character) to `<game>\FrosthavenMods\coop-log.txt`, and `tools/coop_compare.py <host log> <client log>`
compares the two players' checkpoints.

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
