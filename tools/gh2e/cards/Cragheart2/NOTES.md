# Cragheart (2nd edition): translation notes

Sources: the sheet `tools/gh2e/sheets/Cragheart.json` decided every value, mark, corner icon and text. Xipe's files
were the starting point; the 1st edition port (`mods/gloomhaven/files/Global/Characters/Cragheart/`, all 60 halves
passed the G2 bench) and Frosthaven's own cards gave the shapes. All texts got own keys `GH2E_Cragheart2_NN_T/B...`
(Xipe's `GH2E_X_*` keys and the 1st edition `$120_T$`-style keys are no longer used; Xipe's orphan `99140T` is
Meteor's consume text, now `GH2E_Cragheart2_25_T`).

## Class-wide decisions

- **Enhancement shapes:** where the card prints a square on an Attack, Move or Heal (the converter's default would be
  diamond / circle / diamond-plus), the file writes `SquareEnhancements: 1` directly (it passes the converter
  unchanged): Avalanche B heal, Earthen Bulwark B heal, Crater B move, Earthen Clod T attack, Petrify B attack, Burrow
  T heal + B move, Sentient Growth T attack + B heal + B attack, Clear the Way T attack, Blunt Force B heal, Stone
  Pummel T attack, Dig Pit B heal. Everything else uses `Enhancements: 1`.
- **"!" boxes (mandatory triggers, 2e rulebook p.35):** friendly-fire damage rows that are abilities of their own get
  `CanSkip: False` (Rumbling Advance B, Unstable Upheaval T, Crater B x2, Solidify B, Meteor B). Infusions are
  mandatory already. The "!" tab itself isn't drawn (see shared changes).
- **Damage/conditions around created tiles** use `AllTargetsAdjacentToParentTargets: True` under the Create (the 1st
  edition Rock Slide fix; CAbilityTargeting has an explicit Create-parent branch): Crushing Grasp B, Earthen Bulwark T,
  Rock Slide T, Entomb T.
- **Lost / discard** taken from the corner icons; Xipe was wrong on Blunt Force, Rock Slide, Kinetic Assault, Stone
  Pummel, Brutal Momentum, Earth's Embrace and others (all now as the sheet).
- **Consume +N Target** is additive in Frosthaven overrides: Dig Pit's "+1 Target" is `Target: 1` (Xipe's 2 gave +2).

## Per card

- 01 Opposing Strike: as Xipe (area with both end hex marks; end-of-turn Heal 2 range 3, 5 slots XP 1/3/5; the
  bonus also fires at the end of the turn it is played = "this and your next four turns").
- 02 Crushing Grasp: bottom damage now aimed around the created tile (Xipe's hit nobody, the Rock Slide trap).
- 06 Backup Ammunition: APPROXIMATION: +1 Target on the next four ranged attack abilities; the "single-target" limit
  has no bonus filter (1st edition shape, play-tested).
- 07 Earthen Bulwark: damage 1 + Ward to allies around the created tiles is a consume Earth (Xipe: always, and
  Strengthen); range 3 enhanceable.
- 08 Unstable Upheaval: no marks; consume = +1 range on the all-adjacent melee attack (1st edition shape).
- 11 Earthen Clod: bottom counts adjacent obstacles and objectives (`TargetAdjacentValidTilesFilter: ["Obstacle",
  "ObjectActor"]`, the converted Nowhere to Run's shape; Xipe: obstacles only).
- 12 Heaving Swing bottom: Frosthaven's own "suffer damage or destroy an adjacent obstacle" choice (Pyroclast
  Obsidian Shield, `Choose`), then a round +3 on the next melee attack (one use, `XPTracker: [0]`, as Hardened Spike).
  The cost is CanSkip False (as Obsidian Shield), so the bonus can't be had without paying. Bench first.
- 13 Petrify: rewritten (Xipe ships the 1st edition "kill" card): Attack 4 area, then an auto create on the hexes of
  the enemies this attack killed (`CustomAbility.ActorsKilledByThisAbility(ParentAbility).Select(obj.Tile)`,
  featureless only) with XP per kill (`StatIsBasedOnX XP: ActorsKilledByParentAbility`). Bench first.
- 14 Burrow: top infuses Earth (Xipe: none); bottom consume adds its own Immobilize on enemies moved through with
  `Targeting: All` (Trample lesson).
- 15 Mud Eruption: obstacles in the hexes the targets stood in when targeted (the attack's `TilesSelected`), placed
  after the pushes, featureless only (Xipe: one obstacle anywhere within 4). Bench first.
- 16 Sentient Growth: rule question settled: bottom "Heal 1, target all, range 1" includes yourself (a ranged heal may
  target its user; Xipe: allies only). Top heal has no mark (Xipe gave one).
- 17 Clear the Way bottom: NEEDS PLUGIN for the terrain part: obstacles and traps on the path are destroyed (1st
  edition shape); hazardous and difficult terrain stay. Top: 1st edition merged move-obstacle attack.
- 18 Blunt Force: consume gives Ward (Xipe: Retaliate, in a block that didn't parse); top lost.
- 19 Rock Slide bottom: APPROXIMATION / NEEDS PLUGIN for exactness: the 1st edition Rocky End merged destroy-attack:
  obstacles within range 2 of YOU are destroyed (card: of the designated enemy), Attack X = 2 x destroyed, range 4,
  Immobilize, on an enemy you pick. Top: no XP (Xipe had 1), Muddle added as its own sub-ability.
- 20 Kinetic Assault: bottom Ward [diamond+] self (Xipe: Shield 1), Retaliate 3 range 3 round; top adjacency damage.
- 21 Solidify: the 1st edition Unstable Upheaval bottom (Shield + UseParentTiles damage), one ally within 3.
- 22 Stone Pummel bottom: APPROXIMATION: the 1st edition persistent destroy-for-+2 bonus without a use track; any
  obstacle adjacent to you counts, not only one adjacent to both you and the target. Own help text key (the shared
  `GUI_StonePummel_Bottom_HelpBoxTooltip` says +3).
- 23 Dig Pit: the obstacle goes beside the created trap (Onyx Shards' `FindAbilityInPhaseByName` +
  `GetAdjacentTiles`; Xipe: beside you).
- 24 Cataclysm: the card's two area hex marks added.
- 25 Meteor: rewritten. Consume: a round bonus on Frosthaven's Drifter Shockwave (`DuringActionAbilityOnAttackFinished`,
  `AttackType: Ranged`): after each ranged attack every other figure adjacent to the target suffers 1. It is the
  half's first ability as an ability-level `Consume` (Infuser Gemstone Resonance). Then one destroy (up to 6 adjacent)
  and six Attack 2 range 3 abilities, the k-th gated on `CPhaseAction.ObstaclesDestroyedThisAction >= k`. Rule
  question settled: separate attacks, so several may hit the same enemy. No XP/infusion on the top (Xipe had both).
  Bench first.
- 26 Brutal Momentum: top counts obstacles/walls adjacent to YOU (`CAbilityFilter.ObstacleIsAdjacent +
  GetAdjacentWallCount`, as Pyroclast Under Pressure; Xipe: adjacent to the target) and adds to both Attack and Push.
  Bottom: start of next four turns Move 3 with a one-use turn bonus of +3 melee as the move's sub-ability (Xipe:
  missing, plus an extra Move 3 on play, removed). Bench first.
- 27 Rocky End: consume (missing in Xipe) appends an auto create on every empty hex of the attack's area
  (`ParentAbility.ValidTilesInAreaAffected`; Frosthaven appends an override's SubAbilities). Bottom: APPROXIMATION of
  "each featureless hex the target exits": the shortest path from the pushed target's start hex to its end hex, start
  included (`CustomAbility.GetTilesOnDirectPath`, falling back to the start hex). Bench first.
- 28 Entomb: top consume: two obstacles adjacent to the target and not adjacent to you (Bring Down the Mountain's
  targeting), damage 1 around them. Bottom (missing in Xipe): create beside you, destroy up to 6 within 3, then Ward
  to as many allies as obstacles were destroyed among the allies adjacent to a destroyed hex. APPROXIMATION: not paired
  one ally per obstacle. Bench first.
- 29 Wave of Destruction: top: Frosthaven's per-target `TargetDistanceRangeDiff` is |range - distance| (melee range 1),
  so the stored base is 2 to give the printed 1 + distance; the face shows "1". Bottom: NEEDS PLUGIN: a move that may
  enter obstacle hexes. APPROXIMATION now: Move 5 with Jump (as Xipe) that destroys every obstacle on its path, then
  +2 per destroyed obstacle on the next attack this turn. Jump also passes enemies/traps and can't end on the rock.
- 30 Earth's Embrace: no marks; bottom X = adjacent obstacles (Pyroclast Stone Armor's count; Xipe: destroyed this
  turn), persistent end-of-turn heal as Crashing Tide Dug In.

## Perks

All ten from the perk sheet (18 checks). New modifier `AMC_GH2E_Cragheart2_Obstacle_P1_R` (+1 rolling, obstacle in an
empty hex adjacent to the target, aimed like Frosthaven's AMC_Damage1Trap_P0). Boulder Leap: APPROXIMATION: the
once-per-scenario toggle destroys one obstacle within 5, then Teleport 5 (not tied to the freed hex). Earthen Rest:
after-rest hook, destroy within 3 with an Earth infusion sub-ability. Earthquakes: the 1st edition crossover's.
Masteries: none written (no existing battle-goal pattern for "obstacle every round" or the 8/8/8 one).

## Shared changes wanted

- gh_layout: draw the 2e "!" mandatory-trigger boxes (Frosthaven has a `- MandatoryTrigger: Layout:` element, see
  Crashing Tide Dug In); right now they are plain rows.
- Plugin: Wave of Destruction bottom (move into obstacle hexes, destroying them), Rock Slide bottom (destroy range
  measured from a designated enemy), Clear the Way bottom (destroy hazardous/difficult terrain entered).
- Plugin or filter: Backup Ammunition "single-target" restriction; Stone Pummel "adjacent to both you and the target".
- Bench first (custom expressions not yet seen in game): Heaving Swing B, Petrify T, Mud Eruption T, Meteor T,
  Brutal Momentum T+B, Rocky End T consume + B, Entomb T consume + B, Wave of Destruction B, Dig Pit T, Earth's
  Embrace B, perks Rock Spray / Boulder Leap / Earthen Rest.

## Check

`GH2E_SKIP_RULECHECK_BUILD=1 python tools/gh2e/check.py Cragheart2`: CHECK PASSED (30 of 30 cards, packcheck 0
problems 0 warnings, rulecheck 42 ok, no missing text keys, no gh_port warnings).
- Perk Rock Spray: the obstacle modifier crashed the game when drawn (a modifier card can't run Create: error screen in the 2e pass, 2026-11-01). Now Frosthaven's rolling +1 (AMC_P1_R); the obstacle is NEEDS PLUGIN.

## 2026-11-02: modifier abilities and Safeguard

- Perk Rock Spray (2026-11-02, supersedes the line above): `AMC_GH2E_Cragheart2_Obstacle_P1_R` is back; its Create runs after the attack through the plugin (GHModifierAbilities.cs: a modifier ability kind the game doesn't run is stacked as the drawer's next ability, the attack's target as TargetActor). Bench it.
