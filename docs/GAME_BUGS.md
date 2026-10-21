# Bugs found in Frosthaven's code (Retail 1.0.34058)

A code audit of the decompiled game, done 2026-10-20 in five areas (randomness, combat, monsters and rounds, campaign
and outpost, co-op determinism), checked against the board game rulebook and the game's own data and text. Every item
is backed by code that was read. **Verified** = re-checked by hand after the audit; **confirmed** = the code path was
read and the effect follows from it; **likely** = the code path is clear but the trigger wasn't fully traced.

Fixed by the mod pack's **Game bug fixes** rule are marked ✅.

## Co-op desyncs (the main complaint in reviews)

| # | Bug | Where | Status | Effect |
|---|---|---|---|---|
| D1 | A failed outpost command never clears the "command in progress" marker | `CampaignController.BeginResult/EndResult`, `MultiplayerGameController.ApplyCommand` (no try/finally) | verified | That PC's outpost rejects every later command until restart; partial changes stay |
| D2 | Restful Slippers / improved short rest draws from the scenario dice in UI code on the resting player's PC only | `CardsHandUI.PerformImprovedShortRestCoroutine` | confirmed | The dice drift apart, so later shuffles and draws differ: desync |
| D3 | Short rest's random card is drawn in UI code, with incomplete compensation on other PCs | `CardsHandUI` short rest | confirmed (path-dependent) | Same as D2 in some paths |
| D4 | The Strategy favor draws monster cards with an unseeded random generator inside a command every PC runs | campaign favor code | confirmed | Each player sees different cards; campaign state diverges |
| D5 | Client rollback/replay can lose the client's own confirmed outpost commands | `FHMultiplayerClient` | confirmed | Client's outpost silently differs from the host's |
| D6 | Gameplay actions dropped on a phase mismatch (use item, select target, augments, waypoints) | `ActionProcessor` | confirmed | One PC applies an action another skipped |
| D7 | Receivers replay some actions through their own UI (ShortRest clicks a button on whatever dialog is open) | proxies | likely | Wrong choice applied on a receiving PC |
| D8 | Main thread changes rules state while the rules worker thread runs | `Choreographer` / element and RNG changes | likely | Timing-dependent divergence |
| D9 | Level events run on local UI events and an 8 ms per-frame budget | `LevelEventsController` | likely | Event effects can apply at different points on different PCs |
| D10 | "Infuse any element" without a picker uses each PC's own random generator | `CAbilityInfuse` | verified; latent (all current cards show the picker) | Different element per PC |
| D11 | Entering a scenario advances the campaign dice outside any command | `FHGlue.SetCurrentScenario` | likely | Drift if a client skips or rolls back |
| D12 | Prop rotations from each PC's own random generator are stored in synced state | `CAbilityCreate` etc. | confirmed | Harmless "Minor desync" noise that masks real drift (only the first minor desync is reported) |
| D13 | Closing a story popup resumes the rules engine on that PC only | story messages | confirmed | Low |
| D14 | Line of sight uses floating-point sin/cos | LOS | speculative | Could differ across platforms (e.g. Linux/Proton) |

## Randomness

| # | Bug | Status | Effect |
|---|---|---|---|
| R1 ✅ | Attack modifier shuffle is off by one (Sattolo) and new decks always have a +0 at the bottom | verified | Nulls and x2s ~6.25% of draws each instead of ~6.0% (`tools/amd_sim.py`) |
| R2 | The same off-by-one shuffle biases the loot deck and Restful Slippers choices | confirmed | Small parties get noticeably fewer "+2 at 2-3 players" material loot cards |
| R3 | WO-33 option A permanently deletes town guard cards drawn "for threat" | confirmed | Up to 8 cards gone per attack, possibly Success/Wrecked cards |
| R4 | Attack targets shuffled although the card gives a fixed order (WO-33/34/35/71; SO-23 also hits odd buildings) | confirmed | Wrong buildings attacked |
| R5 | SO-51 bet: 51% win chance instead of 50%; guaranteed with an Algox or Inox | confirmed | Low |
| R6 | Monster wild elements are picked at random; the rules let the party decide | confirmed | Low |
| R7 | Challenge deck reset without a shuffle (after 190.1) | likely | Challenges return in the order completed |
| R8 | Town guard advantage can compare a card with itself when the deck runs out | confirmed | Rare |
| R9 | Personal quests can be redrawn without limit by cancelling character creation | confirmed | Possibly intended |

## Combat

| # | Bug | Status | Effect |
|---|---|---|---|
| C1 | A reshuffle in the middle of a rolling-modifier chain duplicates the in-use rolling card | confirmed | The deck grows by a card each time, for the rest of the scenario |
| C2 | The second card of an advantage/disadvantage draw can reshuffle the first back in | confirmed | Rare: a card compared with itself |
| C3 | The 200-card guard on rolling chains checks a list that never grows | confirmed | A deck of only rolling cards would hang the game (latent) |
| C4 | Automatic advantage/disadvantage pick ignores the "ambiguous: first drawn" rule and already-applied rolling cards | confirmed | Can pick the smaller final result |
| C5 | Curse limit counted per deck, not per curse type | confirmed | A second enemy faction allows 20 monster curses |

## Monsters and rounds

| # | Bug | Status | Effect |
|---|---|---|---|
| M1 | A player summon created outside its summoner's turn acts that same round | likely | Rules: summons never act the round they appear |
| M2 | Monsters from start/end-of-turn spawners skip the rest of the round | likely | Rules: spawned monsters act that round |
| M3 | Short of standees, monsters placed in data order, not elites first by proximity | likely | Low |
| M4 | "Hold N rounds" objectives show N/N one round before completing | confirmed | Display only |
| M5 | Element use tracked per monster type, not per shared deck | speculative | Possibly scenario 60 only |
| M6 | Focus tie-break uses a wrong fallback distance for multi-hex/obstacle targets | confirmed (code), effect speculative | Low |
| M7 | AI's "ranged attack while adjacent" check ignores walls | likely | Wasted monster movement near doorways |
| M8 | Short rest can crash if every discarded card is excluded | speculative | Crash to menu |

## Campaign and outpost

| # | Bug | Status | Effect |
|---|---|---|---|
| O1 | Rebuilding a wrecked building charges the extra-build morale cost when the week's build is used | verified | -2 morale per rebuild; rules allow any number of rebuilds |
| O2 | PQ 14 ("a quest in each of 5 areas") completes with 5 quests in one area | confirmed | Quest done too early |
| O3 | Morale can go below 0 | confirmed | |
| O4 | Jeweler discount shown and checked but never charged; also limited to Jeweler items (text: all small items) | confirmed | Players pay full price |
| O5 | Inn allows 5 / 9 purchases a week at level 2 / 3 instead of 2 / 3 | confirmed | Too generous |
| O6 | "Week in this season" is `Week % 10`: the last week of each season is week 0 | verified | SO-28 wrecks 2 buildings in week 10; next-summer retirement sections land in winter |
| O7 | SO-10 merchant delivery is lost in year 2+ after paying 15 gold | confirmed | Gold lost for nothing |
| O8 | Crafted items sell for their whole component tree, not 2 gold per resource or item | confirmed (maybe intended) | Crafted items sell for more |
| O9 | Hazardous terrain at scenario level 6 deals 4 instead of 3 | confirmed (data) | Low |
| O10 | WO-07 section 47.1 placed one week early | likely | Low |
| O11 | PQ 6 can count up to 3 times in one outpost phase | likely | Quest progresses too fast |
| O12 | The low-morale section swap leaks into a new campaign started in the same session | likely | Low |

Full per-item details (code excerpts, rulebook references, suspects checked and found correct) are in the audit reports
kept outside the repo (they quote decompiled code).
