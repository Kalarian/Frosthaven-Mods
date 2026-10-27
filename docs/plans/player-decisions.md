# Research: decisions the digital game makes for the players

Status: researched 2026-10-27, nothing built. Scope: every place where the board game says "the party decides" (or
leaves a choice to a player) and Frosthaven digital decides on its own. Summons first, then everything else found.
Sources: the official rulebook (page numbers below) and the decompiled game (ScenarioRuleLibrary, Assembly-CSharp).

## The rules that give the choice to the players

| Rule (rulebook) | What the board game says |
|---|---|
| p.6 universal rule | "The party decides ambiguity." Everything below that has no explicit tie rule falls back to this. |
| p.31 summons | Summons obey automated monster rules (Move +0, Attack +0). "If a character summon cannot find a focus, the summoner may choose for the summon to move toward them instead." "The summoner may voluntarily remove the summon from the map at any time except during another ability." |
| p.41 focus | Fewest movement points, then closest by range, then earliest in initiative order. Beyond that: ambiguity (p.6). |
| p.41-42 movement | Fewest movement points to maximise attacks, fewest disadvantaged attacks, fewest negative hexes. Several hexes can satisfy all of that: ambiguity, the party picks the hex. |
| p.32 push / pull | "When a monster performs a push or a pull, the party decides the direction, but the target must move as far as possible." |
| p.43 wild / mixed elements | "If a monster infuses or consumes a wild or mixed element, the party decides which element." |
| p.43 heals | Monster heals the ally with the greatest missing health. No tie rule: the party. |
| p.43 positive conditions | Closest ally without the condition, then earliest initiative. |
| p.43 monster summons | Adjacent to the summoner "and as close to an enemy as possible". Ties: the party. |
| p.44 spawns | Blocked spawn hex: "the closest empty hex". Ties: the party. |
| p.19 initiative ties | Characters: second card, then the party decides (separate plan: `long-rest-turn-order.md`). |
| p.27 advantage ambiguity | Board game: the first drawn card. (No choice in the rules; the digital game has an optional setting that gives one, see below.) |
| p.26 rolling modifiers | "The drawn modifiers can then be applied in any order." |

Summons and scenario allies (p.46) follow all the monster rules above, so every "monster" line applies to a character's
summon too, with the summoner's player as the natural decider.

## What the digital game does (decompiled)

### Already a player choice in the digital game (nothing to build)
- Placing a character summon: the summoner picks the hex (`CAbilitySummon`, player branch).
- Summon with no focus: the game asks "move toward the summoner?" with Confirm / Skip (`CAbilityMove` ->
  `CReturnToSummoner_MessageData`, `Choreographer.Handle_ReturnToSummoner`; online it waits for the summoner's client).
- Wild elements of characters and of character summons: the element picker is shown; for a summon it goes to the
  summoner (`Choreographer.Handle_ShowElementPicker`).
- Advantage: with the game's own "choose advantage modifiers" setting (`GameState.ChooseAdvantageModifiers`, a global
  option synced to all players) the attacker picks between the two cards when they differ. More than the rulebook gives.
- Long rest: the card to lose is chosen by the player.

### Decided by the game (the list)

| # | Decision | Digital behaviour | Where | Hits |
|---|---|---|---|---|
| 1 | **Which hex a summon / monster moves to** when several are equally good | The AI sorts attack hexes by: traps on the path, reachable, number of targets, disadvantaged attacks, movement points, extra-target priority, then **path cost as tie-break and list order**. The game even counts the tied best hexes (`SortAttackTilesAndFindNumberOfBests`) and then takes the first. | `CAIMoveNew.CompareAttackTiles`, `FindPathToFocus` | every summon and monster turn; this is the big one (which side of the enemy, next to which ally, on which trap-free hex) |
| 2 | **Area attack placement** for a summon / monster with an area | Same scoring, then distance to focus, distance to any target, then first found | `CAIMoveNew.EvalAoEAttackTile` | monsters with areas, summons with areas |
| 3 | **Extra targets** of a multi-target attack when more candidates qualify than targets | By list order (`priority = i`), the list sorted by the initiative comparer | `CAIMoveNew` 1228 / 1484 | multi-target monsters and summons |
| 4 | **Focus ties** past the rulebook's chain (equal movement, range and initiative) | Falls through to actor ID (`ActorInitiativeComparer` last step) | `GameState.ActorInitiativeComparer` | two long-resting characters, two monsters of one type as a summon's targets |
| 5 | **Push / pull direction by a monster or summon** | The game first looks for a push tile **with a trap or hazardous terrain** and takes it; otherwise the farthest tile, first in list order | `CActor.Push`, `CActor.Pull` (`FindTileWithTrapOrHazardousTerrain`) | 11 vanilla monster types push or pull (Abael Herder, Algox Snowspeaker, Ancient Artillery, Chaos Demon, Earth Demon, Lurker Mindsnipper, Ooze, Savvas Icestorm, Shrike Fiend, Steel Automaton, Wind Demon); the rule says the party decides the direction |
| 6 | **Wild element infused / consumed by a monster** | No picker on monster cards (`ShowPicker` false): the game picks the first eligible element | `CAbilityInfuse` 220-222, `CAbilityConsumeElement` | Imp and Black Imp (Phase Out infuses Any; Rotten Sting and Dark Charm consume Any) |
| 7 | **Monster heal target ties** | Greatest missing health, then initiative, then ID | `CAbilityHeal.SortValidActorsInRange` | few monsters (none with ally heals in the vanilla data; self heals have no choice) |
| 8 | **Monster positive-condition target ties** | Sorted by initiative, then ID | `CAbilityAddCondition.ShouldSortValidActorsInRangeByInitiative` | matches the rulebook until the final tie |
| 9 | **Monster summon placement ties** | Shortest path to an enemy, then list order | `CAIMoveNew.SelectTilesBasedOnTargetingFocus` | 7 summoning monsters (Abael Herder, Deep Terror, Living Doom, Lurker Wavethrower, Ooze, Savvas Icestorm, Savvas Lavaflow) |
| 10 | **Spawn hex when the spawn point is blocked** | Rings around the spawner, first empty tile in ring order | `CSpawner.FindClosestEmptyTileList` / `FindEmptyTileInList` | scenario spawns |
| 11 | **Removing a summon voluntarily** | No way to do it: `RemoveHeroSummon` runs only on death | `GameState` 3735 | every summoner (board game p.31 allows it any time outside an ability) |
| 12 | **Order of simultaneous bonuses** on an attack (several active bonuses, items, shields) | Fixed priority (`GetBonusPriority`) | `CAbilityAttack` 1071 | rarely matters |
| 13 | **Order of rolling modifiers** (e.g. rolling +1 and a x2) | Not verified: no ordering code found; needs a bench with a rolling +1 and a x2 draw | `CActor` 4600-4700 | any character with rolling perks |

Not taken away: initiative order of summons (summon order, as the rulebook), players acting before monsters on ties,
monster elite / standee order.

## Could a mod give the choice back?

Yes for all of 1-11. The game already has every piece a mod needs:

- **A pause in an AI turn for a player answer.** `Handle_ReturnToSummoner` is exactly that: a message from the rules
  library, Confirm / Skip buttons, and online an `ActionProcessor` wait until the controlling client answers. A mod can
  raise its own prompt the same way.
- **Who answers.** `FFSNetwork.GetControllingPlayer`: a summon is controlled by its summoner's client; a monster
  ability flagged `ShouldBePlayerControlled` goes to the host. So summon choices go to the summoner, monster choices
  (push direction, Imp elements, monster ties) to the host, which is the natural reading of "the party decides".
- **Tile selection UI.** `CActorIsSelectingMoveTile_MessageData` already highlights hexes for the player; for a
  non-player actor the game sends it and immediately moves on (`CAbilityMove`, `Type != Player` branch). A mod holds
  there when there is a tie and lets the player click one of the tied hexes.
- **Player control of a non-player actor** exists for mind-control cards (`CAbilityControlActor`,
  `UseControllingActorPlayerControl`): the controlling player performs the actor's listed abilities with the normal
  player UI. That is the route for a *fully manual summons* house rule, which goes beyond the rulebook (summons "are not
  controlled by their summoner"), so it would be a separate switch from the rules-as-written one.

### Build sketch (rules-as-written switch: "The party decides ties")

1. **Movement / area / extra-target ties (1-3):** after `CAIMoveNew.Process()` (postfix), re-run the game's own
   comparer over `finalFocusEval.attackTiles` without the move-cost tie-break (what `SortAttackTilesAndFindNumberOfBests`
   does) to collect the tied hexes; if more than one, raise the prompt to the controlling player with those hexes
   highlighted; on the answer set `actor.AIMoveFocusPath` / waypoints to the path for the chosen hex (the game's
   `pfContext.GetPathTo`) and the attack targets that hex gives. Default on Skip: the game's own pick. Area attacks the
   same way over `TPossibleAoEPlacement`s. Both choices are synced as one command so every PC resolves the same turn.
2. **Push / pull (5):** prefix on `CActor.Push` / `Pull` when the pusher is not a player: take
   `CAbilityPush.GetPushTiles`, keep only the tiles at the greatest distance (the "as far as possible" rule), and if more
   than one remains ask the pushed character's player (or the host) which; drop the trap-first preference under the
   switch. If only one tile is farthest, no prompt.
3. **Wild elements of monsters (6):** set `ShowPicker` on the Imps' Any abilities (the ability override path already
   exists: `abilityOverride.ShowElementPicker`), so the game's own picker appears, answered by the host.
4. **Heal / condition / monster-summon / spawn ties (7-10):** same prompt pattern on the sort or pick functions; lowest
   value, build last or not at all.
5. **Dismiss a summon (11):** a button on the summon's entry in the summon list (`UISummonListContainer`) that sends a
   synced command running the game's death path for the summon (`RemoveHeroSummon` + the summon card to the discard
   or lost pile as the rulebook says), blocked while an ability is running.
6. Every item behind its own host-controlled switch (every mod has a switch); the prompts default to the game's own
   decision so a skipped prompt changes nothing; co-op desync check with the CoopLog export.

### What to verify in game before building

- Whether the game's move-tile highlight can be held open for a non-player actor without the turn advancing (the
  `ReturnToSummoner` wait suggests yes).
- That a tie prompt in co-op reaches only the controlling client and the others wait (the `ActionProcessor` state).
- Item 13 (rolling modifier order) with a bench: a rolling +1 followed by a x2 should let the player choose +1 first.

## Priority (value for the players)

1. Push / pull direction (5): the digital game actively picks the trap; the rulebook gives the party the direction.
2. Summon and monster movement ties (1-2): the most frequent "why did it go there" moment.
3. Dismiss a summon (11): a rulebook right that is simply missing.
4. Imp wild elements (6): cheap, two monster types.
5. The rest (3, 4, 7-10, 12-13): small or rare.
