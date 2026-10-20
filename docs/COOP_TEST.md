# Co-op test: House Rules + Give resources (v0.3.0)

Two players: **Host** (runs the campaign) and **Client** (joins). Takes about 45 minutes. Have voice chat open, since
some steps are done at the same moment.

## What we're checking

1. House rules (Options > Custom Rules) sync from the host to the client, and only the host can change them.
2. Give resources works for both players and both PCs end up with the same numbers.
3. Nobody can give away a character that the other player controls.
4. Nothing desyncs afterwards: saving, reloading, shopping, crafting, donating, a scenario.

## How we check

- **Checkpoints:** when a step says **CHECKPOINT**, close any open window, count down on voice ("3, 2, 1"), and
  **both press Ctrl+F8 at the same time**. Each PC writes a snapshot of every character's gold and materials, the
  herb supply and the house rules. If one of you misses it, both press again and say so.
- **The log:** everything goes into `FrosthavenMods\coop-log.txt` in each player's game folder
  (`C:\Program Files (x86)\Steam\steamapps\common\Frosthaven\FrosthavenMods\coop-log.txt` by default). After the test,
  `python tools/coop_compare.py host.txt client.txt` compares the two files checkpoint by checkpoint.
- **Desyncs:** if the game ever shows a **desync / out of sync** message, stop. Note the step number, take a
  screenshot, and skip ahead to "After the test".

## Before the session

**Host, alone:**
1. Use a **test campaign that's already past the prologue** (e.g. "Mod Test"), not your real campaign.
2. In solo, with the dev tools still installed, press **Ctrl+F9** once or twice so every party member has gold and
   materials. Save, then quit to the main menu. The dev tools don't work online, which is intended.
3. The party needs **4 characters**: 2 for the host, 2 for the client. Make sure the save has 4 in the party.

**Both players:**
4. Close Frosthaven and run the installer (`Install Frosthaven Mods.exe`, from the
   [latest release](https://github.com/Kalarian/Frosthaven-Mods/releases/latest)). Click **Install / Update**. The
   window must say **v0.3.0** for both of you.
   - If Windows shows a warning, click "More info", then "Run anyway".
5. Start the game.

## A. Join and assign characters

| # | Who | Do | Expected |
|---|---|---|---|
| A1 | Host | Load the test campaign as a multiplayer game. Client joins. | The join works. |
| A2 | Host | Give the client 2 of the 4 characters (the game's party slot / player assignment). | Each player controls 2 characters. Write down which ones: Host = **H1, H2**, Client = **C1, C2**. |
| A3 | Both | Options > **Custom Rules**. | Both see "Mod pack rules" with **Give resources** and **Item trading**, both Active. On the client they're greyed out (only the host can change them). |
| A4 | Both | **CHECKPOINT** (#1) | |

## B. House rules sync

| # | Who | Do | Expected |
|---|---|---|---|
| B1 | Host | Custom Rules: turn **Item trading** off. | |
| B2 | Client | Close and reopen Custom Rules. | Item trading shows **Inactive** for the client too. |
| B3 | Host | Turn **Give resources** off. | |
| B4 | Both | Go to another outpost screen and back (e.g. open and close a building). | For **both**: the **GIVE** button under the name in the top bar is gone, and so is the **Ctrl + click Give** line under the party list. Ctrl+click on a portrait just selects it. |
| B5 | Host | Turn **Give resources** and **Item trading** back on. Both: change screens again. | The GIVE button and the hint line are back for both. |
| B6 | Client | Try clicking a Custom Rules switch. | Nothing happens (greyed out). |
| B7 | Both | **CHECKPOINT** (#2) | |

## C. The host gives

| # | Who | Do | Expected |
|---|---|---|---|
| C1 | Host | Ctrl+click **H1**'s portrait. | The Give Resources window opens with the other 3 characters as columns. "H1 gives…" at the top. |
| C2 | Host | Give **C1** 10 gold (Ctrl+click the gold cell) and 2 metal (click twice). Press **Give**. | The window closes. |
| C3 | Both | Select H1, then C1, and read the top bar. | Both PCs: H1 has 10 gold and 2 metal **less**, C1 has 10 gold and 2 metal **more**. Same numbers on both screens. |
| C4 | Host | Select **H2**, click the **GIVE** button under the name. Give **H1** 5 lumber (or any material H2 has). | Both PCs show the change. |
| C5 | Host | Open Give for H1, put some amounts in, then press **Esc**. | The window closes, nothing changes, and the pause menu does **not** open. A second Esc opens the pause menu as usual. |
| C6 | Both | **CHECKPOINT** (#3) | |

## D. The client gives

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

## F. At the same time

| # | Who | Do | Expected |
|---|---|---|---|
| F1 | Both | Host opens Give from **H1**, client opens Give from **C1**. Each sends some gold to the *other player's* characters. Count down and press **Give** at the same moment. | Both gifts go through. Both PCs end with the same numbers. |
| F2 | Both | Host opens Give from **H2** with **all** of H2's gold queued to C2 (Shift+click), but doesn't press Give. Client gives **H2** 10 gold from C1. Then the host presses Give. | H2 gives what it had when the host clicked Give (the window can be 10 short, which is fine). Nothing breaks, and both PCs match. |
| F3 | Both | **CHECKPOINT** (#6) | |

## G. Normal outpost actions afterwards

| # | Who | Do | Expected |
|---|---|---|---|
| G1 | Client | With **C1**, buy an item using gifted gold. | Purchase works, and both PCs see C1's gold drop. |
| G2 | Host | With a character that **received** materials, craft an item (Craftsman), or donate materials to a building. | Works, and both PCs show it. |
| G3 | Both | **CHECKPOINT** (#7) | |

## H. Save, reload, scenario

| # | Who | Do | Expected |
|---|---|---|---|
| H1 | Host | Save. Both quit to the main menu. Host reloads the campaign online, client rejoins. | The join works. Gold and materials are as before. Custom Rules still shows both rules on, for both players. |
| H2 | Both | **CHECKPOINT** (#8) | Identical to #7. |
| H3 | Both | Start any scenario together and play 2–3 rounds normally. | **No desync message.** (The Give button is hidden during scenarios.) |
| H4 | Host | Leave the scenario (abandon or finish) and get back to the outpost. | |
| H5 | Both | **CHECKPOINT** (#9) | |

## After the test

1. Both close the game.
2. The **client** sends their `coop-log.txt` to the host.
3. The host gives Claude both files (or runs
   `python tools/coop_compare.py <host coop-log.txt> <client coop-log.txt>`).
4. Also note anything odd from the tables above: what you expected and what happened.

**Don't relaunch the game before copying the files.** They're kept across launches (each launch adds a new session),
but sending them straight away avoids confusion.
