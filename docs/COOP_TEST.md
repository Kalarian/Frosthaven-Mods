# Co-op test: the Frosthaven Mods house rules (v0.6.5)

Two players: **Host** (runs the campaign) and **Client** (joins). Takes about an hour. Have voice chat open, since
some steps are done at the same moment.

## What we're checking

1. House rules (Options > Custom Rules) sync from the host to the client, and only the host can change them.
2. Giving resources and items works for both players, and both PCs end up with the same gold, materials and items.
3. Nobody can give from a character that the other player controls.
4. Nothing desyncs afterwards: saving, reloading, shopping, crafting, donating, a scenario.
5. The battle stats (extra rows and the Campaign view on the post-scenario Statistics page) show the same numbers on
   both PCs.
6. Event previews (Reveal event outcomes) show the same thing on both PCs, and the result matches the preview.
7. Rules that change play are locked during online scenarios, and the co-op desync fixes (Game bug fixes) hold up:
   no desync, including after short rests.

## How we check

- **Checkpoints:** when a step says **CHECKPOINT**, close any open window, count down on voice ("3, 2, 1"), and
  **both press Ctrl+F8 at the same time**. Each PC writes a snapshot of every character's gold, materials and items
  (owned and worn), the herb supply and the house rules. If one of you misses it, both press again and say so.
- **The log:** everything goes into `FrosthavenMods\coop-log.txt` in each player's game folder
  (`C:\Program Files (x86)\Steam\steamapps\common\Frosthaven\FrosthavenMods\coop-log.txt` by default). After the test,
  `python tools/coop_compare.py host.txt client.txt` compares the two files checkpoint by checkpoint.
- **Desyncs:** if the game ever shows a **desync / out of sync** message, stop. Note the step number, take a
  screenshot, and **everyone presses Ctrl+F7** (before closing the game): it zips the desync recordings to your desktop
  (`Frosthaven-desync-<name>-<time>.zip`) and opens the folder. Then skip ahead to "After the test".

## Before the session

**Host, alone:**
1. Use a **test campaign that's already past the prologue** (e.g. "Mod Test"), not your real campaign.
2. The party needs **4 characters**: 2 for the host, 2 for the client.
3. In solo, with the dev tools still installed, press **Ctrl+F9** once or twice so every party member has gold and
   materials. The dev tools don't work online, which is intended.
4. Spend some of that gold so the characters own items (the shop, or the Craftsman):
   - every character owns **at least 2 items**, and at least one of them is **worn** (equipped);
   - **two characters own the same item** (e.g. both buy the same cheap item). That's for the "already owns" check.
5. In Options > **Custom Rules**, turn on all ten mod rules: **Tradeable resources and equipment**, **Share duplicate
   loot**, **Reveal event outcomes**, **Dwarf's balance changes**, **Game bug fixes**, **Milestone saves**, **Battle
   stats**, **Easier item reward window**, **Modifier deck odds** and **Scenario map file**.
6. Optional, solo: test duplicate loot, battle stats and milestone saves (see the end of this file). Then save and quit to the main
   menu.

**Both players:**
7. Close Frosthaven and run the installer (`Install Frosthaven Mods.exe`, from the
   [latest release](https://github.com/Kalarian/Frosthaven-Mods/releases/latest)). Click **Install / Update**. The
   window must say **v0.6.5** for both of you.
   - If Windows shows a warning, click "More info", then "Run anyway".
8. Start the game.

## A. Join and assign characters

| # | Who | Do | Expected |
|---|---|---|---|
| A1 | Host | Load the test campaign as a multiplayer game. Client joins. | The join works. |
| A2 | Host | Give the client 2 of the 4 characters (the game's party slot / player assignment). | Each player controls 2 characters. Write down which ones: Host = **H1, H2**, Client = **C1, C2**. Also note which two own the same item. |
| A3 | Both | Options > **Custom Rules**. | Both see "Mod pack rules" with all ten mod rules, all Active. On the client they're greyed out (only the host can change them). |
| A4 | Both | **CHECKPOINT** (#1) | |

## B. House rules sync

| # | Who | Do | Expected |
|---|---|---|---|
| B1 | Host | Custom Rules: turn **Tradeable resources and equipment** off. | |
| B2 | Client | Close and reopen Custom Rules. | It shows **Inactive** for the client too. |
| B3 | Both | Go to another outpost screen and back (e.g. open and close a building). | For **both**: the **GIVE** button under the name in the top bar is gone, and so is the **Ctrl + click Give** line under the party list. Ctrl+click on a portrait just selects it. |
| B4 | Host | Turn it back on. Both: change screens again. | The GIVE button and the hint line are back for both. Opening Give shows the **Resources / Items** buttons at the top. Cancel. |
| B5 | Host | Turn **Share duplicate loot** off, then the client reopens Custom Rules; then the host turns it back on and the client reopens again. | The client sees Inactive, then Active. |
| B6 | Client | Try clicking a Custom Rules switch. | Nothing happens (greyed out). |
| B7 | Both | **CHECKPOINT** (#2) | |

## C. The host gives resources

| # | Who | Do | Expected |
|---|---|---|---|
| C1 | Host | Ctrl+click **H1**'s portrait. | The Give window opens on Resources with the other 3 characters as columns. "H1 gives…" at the top. |
| C2 | Host | Give **C1** 10 gold (Ctrl+click the gold cell) and 2 metal (click twice). Press **Give**. | The window closes. |
| C3 | Both | Select H1, then C1, and read the top bar. | Both PCs: H1 has 10 gold and 2 metal **less**, C1 has 10 gold and 2 metal **more**. Same numbers on both screens. |
| C4 | Host | Select **H2**, click the **GIVE** button under the name. Give **H1** 5 lumber (or any material H2 has). | Both PCs show the change. |
| C5 | Host | Open Give for H1, put some amounts in, then press **Esc**. | The window closes, nothing changes, and the pause menu does **not** open. A second Esc opens the pause menu as usual. |
| C6 | Both | **CHECKPOINT** (#3) | |

## D. The client gives resources

| # | Who | Do | Expected |
|---|---|---|---|
| D1 | Client | Ctrl+click **C1**. Give **H1** 25 gold (Ctrl+click twice, click 5 times). Give. | Both PCs: C1 −25, H1 +25. |
| D2 | Client | Select **C2**, use the top-bar **GIVE** button. Give **H2** some hide (or lumber). | Both PCs show the change. |
| D3 | Client | Ctrl+click **C2**. **Shift+click** the metal cell under C1, then **Shift+right-click** it, then Shift+click it again. Give. | The cell fills with all of C2's metal, empties, fills again. After Give, C2 has 0 metal and C1 got all of it, on both PCs. |
| D4 | Both | **CHECKPOINT** (#4) | |

## E. Permissions (nothing should change)

| # | Who | Do | Expected |
|---|---|---|---|
| E1 | Client | Select **H1** (the host's character). Click the top-bar **GIVE** button. | Message: "You can only give from characters you control." |
| E2 | Client | Ctrl+click **H2**'s portrait. | The same message. |
| E3 | Host | Ctrl+click **C1**, and use the GIVE button with C2 selected. | The same message both times. |
| E4 | Both | **CHECKPOINT** (#5) | The numbers must be identical to checkpoint #4. |

## F. Items

To check an item on a character: select them and open their equipment / item screen.

| # | Who | Do | Expected |
|---|---|---|---|
| F1 | Host | Open Give for **H1**, click **Items**. | Title "Give Items". One row per item H1 owns, with its icon; hovering an icon shows the item card. The Resources button is dimmed, Items is bright. |
| F2 | Host | Hover the cells. Find a character who already owns one of H1's items (the shared item). | That cell shows the item icon greyed with an "owned" mark and can't be clicked. |
| F3 | Host | Click the cell to give one of H1's **worn** items to **C1**. Click it again, then click it once more. | The item icon appears in the cell, disappears, appears again. The **Items** button now reads **Items (1)**. The other cells in that row can't be clicked while it's picked. |
| F4 | Host | Click **Resources**, queue 5 gold to **C2**, then press **Give**. | **Resources (1)** while queued. One Give sends both. Both PCs: C1 now owns the item (and wears it if a slot was free), H1 doesn't have it anymore, C2 got 5 gold. |
| F5 | Client | Open Give for **C1**, Items tab. Give **H2** an item H2 doesn't own, and **H1** another one. Give. | Both PCs: both items moved. |
| F6 | Client | Give the item C1 just received back to **H1**. | Works: an item can be passed on again. |
| F7 | Client | Ctrl+click **H1** (the host's character), and the GIVE button with H2 selected. | "You can only give from characters you control." both times. |
| F8 | Both | **CHECKPOINT** (#6) | |

## G. At the same time

| # | Who | Do | Expected |
|---|---|---|---|
| G1 | Both | Host opens Give from **H1**, client opens Give from **C1**. Each sends some gold to the *other player's* characters. Count down and press **Give** at the same moment. | Both gifts go through. Both PCs end with the same numbers. |
| G2 | Both | Host opens Give from **H2** with **all** of H2's gold queued to C2 (Shift+click), but doesn't press Give. Client gives **H2** 10 gold from C1. Then the host presses Give. | H2 gives what it had when the host clicked Give (the window can be 10 short, which is fine). Nothing breaks, and both PCs match. |
| G3 | Both | Only if **H1 and C1 both own the same item** and C2 doesn't: the host queues H1's copy to **C2**, the client queues C1's copy to **C2**. Count down, both press Give. | One goes through; the other is refused and undone (there may be no message). C2 owns one copy, the refused giver keeps theirs, and both PCs match. Otherwise skip this step. |
| G4 | Both | **CHECKPOINT** (#7) | |

## H. Normal outpost actions afterwards

| # | Who | Do | Expected |
|---|---|---|---|
| H1 | Client | With **C1**, buy an item using gifted gold. | Purchase works, and both PCs see C1's gold drop. |
| H2 | Host | With a character that **received** materials, craft an item (Craftsman), or donate materials to a building. | Works, and both PCs show it. |
| H3 | Both | During Downtime (if you're in it), sell an item that was **given**. | It sells for the normal price, on both PCs. Skip if you're not in Downtime. |
| H4 | Both | **CHECKPOINT** (#8) | |

## I. Save, reload, scenario

| # | Who | Do | Expected |
|---|---|---|---|
| I1 | Host | Save. Both quit to the main menu. Host reloads the campaign online, client rejoins. | The join works. Gold, materials and items are as before. Custom Rules still shows all ten rules on, for both players. |
| I2 | Both | **CHECKPOINT** (#9) | Identical to #8. |
| I3 | Both | Start any scenario together, bringing **given** items, and play 3–4 rounds normally. Use a given item if you can. **Each player short rests at least once** (with Restful Slippers if anyone owns them). | **No desync message.** The given items work. (The Give button is hidden during scenarios.) |
| I3b | Host | During the scenario, open Options > **Custom Rules**. | **Share duplicate loot**, **Dwarf's balance changes** and **Game bug fixes** are greyed out with "Locked during online scenarios" in their description. **Tradeable resources** and **Reveal event outcomes** can still be switched. Close without changing anything. |
| I4 | Both | Finish the scenario (win if you can; a loss also works). On the post-scenario screen, open the **Statistics** tab (crossed swords). | Under the game's 8 rows: section titles (OFFENSE, DEFENSE, ...) with no bars, and rows with icons, no white boxes. **Both PCs show the same number in every row.** Take a screenshot on each PC (scroll down for the rest). |
| I5 | Both | Click **Campaign** (top right of the Statistics page), then **This scenario**. | The buttons don't overlap the title. Campaign shows a **Scenarios played** row and totals; each player's count is the number of scenarios they've finished with the mod (they can differ if one played more solo). This scenario switches back. |
| I6 | Host | Continue back to the outpost. | If someone looted a copy of an item they already own, the item reward window opens with nothing assigned. The host (party leader) left-clicks one character, then another: the item moves in one click, on both screens. Done assigns it (Share duplicate loot). |
| I7 | Both | **CHECKPOINT** (#10) | |
| I8 | Host | Later, solo (not now: loading is blocked online): open the Load list. | It has **Mod backup - week N - scenario X start** (from I3) and **Mod backup - week N - outpost** (from I6). The client's Load list has none (backups are host only). |

## J. Events (whenever one comes up)

Town events come at the start of an outpost phase, road events while travelling to a scenario. If one appears at any
point in the session, do this before the host chooses:

| # | Who | Do | Expected |
|---|---|---|---|
| J1 | Both | Hover over each option. | A solid box appears just above the option, its left edge lined up with the option's: the outcome text, then the effects (gold, damage, morale, "Outpost attack!", "Then: …"). The option buttons show only the game's own text. |
| J2 | Both | Compare what each option's box says. | **The same text on both PCs.** |
| J3 | Host | Choose an option. | The result (outcome text and effects) matches what the preview showed, on both PCs. Note the event ID (e.g. SO-12) if anything differs. |
| J4 | Both | If it's an outpost attack from **WO-33, WO-34, WO-35, WO-71 or SO-23**, note the order buildings are attacked in. | It follows the card (Game bug fixes): WO-33 even-numbered buildings from highest number down, WO-34 buildings 1–40 from highest number down, WO-35 highest level first, WO-71 lowest level first, SO-23 only even-numbered buildings. Same on both PCs. |

## After the test

1. **Both press Ctrl+F7** in game: a `Frosthaven-desync-<name>-<time>.zip` appears on your desktop (it includes
   `coop-log.txt`, the desync recorder's files and the game's own log). Then close the game.
2. The **client** sends their zip to the host.
3. The host gives Claude both zips (or runs `python tools/coop_compare.py` on the two `coop-log.txt` files, and
   `python tools/desync_compare.py <host zip> <client zip>`).
4. Also send the newest `FrosthavenMods\stats\scenario-*.txt` from each PC (the raw battle records), to check the
   stats match between players.
5. Also note anything odd from the tables above: what you expected and what happened.

**Don't relaunch the game before copying the files.** They're kept across launches (each launch adds a new session),
but sending them straight away avoids confusion.

## Solo: duplicate loot (host, before the session)

A duplicate drop is rare, so the dev tools can fake one (solo only, test campaign only: the copy is conjured).

1. In the outpost, with **Share duplicate loot** on, select a character who owns at least one item, and press
   **Ctrl+F12**. The item reward window opens with a copy of their first item. Assign it to another character and
   confirm. That character now owns it.
2. Turn **Share duplicate loot** off and press **Ctrl+F12** again. No window: the character gets the item's sell value
   in gold instead (the game's normal behaviour).
3. If every other party member already owns that item, the copy is sold even with the rule on.

## Solo: battle stats (host, before the session)

0. Turn **Battle stats** off in Custom Rules and finish a scenario: the Statistics tab shows only the game's 8 rows
   and no Campaign button. Turn it back on.
1. Play any scenario for a few rounds with some fighting, then finish it (Ctrl+F10 wins it; a win with no fighting
   records almost nothing).
2. On the Statistics tab: the extra rows appear under the game's 8, section titles have no bars, every row has an
   icon. Note anything memorable (a crit, a null, a summon, a heal) and check the matching rows.
3. Click **Campaign**: Scenarios played goes up by one for every scenario finished with the mod. Click
   **This scenario** to switch back.

## Solo: milestone saves (host, any time)

1. Turn on **Milestone saves** in Custom Rules. Start a scenario: once the first round starts, the Load list (from
   the main menu, or in the outpost) has **Mod backup - week N - scenario X start**.
2. Finish the scenario (Ctrl+F10 wins it) and continue to the outpost: once the game autosaves there (it does after
   the scenario screens), the Load list also has **Mod backup - week N - outpost**.
3. Load the scenario-start backup: the scenario starts again from round 1, as it was.
4. Restarting a scenario doesn't add a second backup for it. The 21st backup removes the oldest one; your own named
   saves (like "Mid Battle") stay.

## Solo: modifier deck odds (host, any time)

1. Turn on **Modifier deck odds**. In a scenario, right-click a character: under Attack Modifiers, a "Next draw" line
   with the number of cards left, then Null, -1/-2, +0, +1 or more, x2 (and Rolling, if the deck has rolling cards).
2. Check one number by hand: Null % = nulls left / (cards left - rolling cards left). At the start of a scenario with
   a 20-card deck with no perks it's 1/20 = 5%.
3. Right-click a monster: a "Next monster draw" line for the monster deck.
4. Attack until a null or x2 comes up, then right-click that character: "Reshuffles at the end of the round" appears;
   after the round, it's gone and the counts are back to full.
5. Turn the rule off: the extra lines are gone; the game's counters are unchanged.

## Solo: scenario map file (host, any time)

1. Turn on **Scenario map file**. Play until the game autosaves (end a round, or go back to the outpost): the game
   folder has `FrosthavenMods\scenarios\progress-<campaign>.txt`, listing your scenarios, sections and calendar.
2. Run `python tools/scenario_map.py`: it writes `scenario-map-<campaign>.html` next to the file. Open it: the
   scenarios you completed are green, the ones you can play now are gold, a locked-out one is red.
3. Click a scenario you can play now: the panel lists what unlocked it and what it leads to, and its chain lights up.
4. Choices & max path: the lockouts you already made are under "Already decided"; the open ones show what each
   option gives up.
5. Turn the rule off and autosave again: the file stops changing (its time stays the same).
