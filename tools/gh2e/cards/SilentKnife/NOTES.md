# Silent Knife (2nd edition): translation notes

All 28 cards rewritten against `tools/gh2e/sheets/SilentKnife.json` (Xipe's files were the starting point; his
originals are not kept here). Every text has its own `GH2E_SilentKnife_NN_T/B` key with the printed wording; no
`GH2E_X_*` key is used any more. The three Xipe keys found in no text table are replaced: `10126_T1` / `10126_T2`
(Crippling Poison top) -> `GH2E_SilentKnife_26_T` / `_26_T2`, `99098_B` (Smoke Bomb bottom) -> `_11_B` / `_11_B2`.

Engine facts used (read in the decompiled `ScenarioRuleLibrary.dll`, game build of 2026-10-21):
- Flanking = filter `TargetAdjacentAllies: "> 0"` (same parser case as `TargetAdjacentAlliesOfCaster`; Frosthaven's
  own Banner Spear key). Isolated = `TargetAdjacentAlliesOfTarget: "== 0"`. Both are the 1st edition Scoundrel's
  filters, passed in the 1e bench.
- Every conditional override whose filter passes is applied, strengths add; an override's negative XP is ignored
  (`CAttackSummary.TargetSummary`: `if (XP > 0)`), so "either but not both" needs mutually exclusive filters.
- `AddTargetPropertyToStrength` works only in an attack's own conditional overrides, not in an active bonus.
- `MoveTrap` measures `MoveTrapRange` from the mover (unless Vars `MoveAroundTile`) and only drops traps on empty hexes.
- Damage prevention without a use track prevents every time while the bonus lasts.

## Per card

- **01 Single Out**: as Xipe (= 1e shape). Bottom +3 on isolated targets, 4 slots, XP on 2 and 4, lost.
- **02 Flanking Strike / 13 Open Wound / 10 Backstab**: flanking override as 1e. Backstab: consume Dark +2, XP 1.
- **03 Swift Bow**: 1e loot-the-path (Loot 0, AllTargetsOnMovePath), passed in the 1e bench.
- **05 Sinister Opportunity**: three overrides (+1 flanking, +1 isolated, advantage + XP 1 when both).
- **06 Venom Shiv**: bottom = Blinkblade Twin Strike's round pierce buff limited to `AttackType: Ranged`.
- **08 Practiced Reflexes** (approximation): Frosthaven has no "may choose not to spring traps" move (only the perk
  flag `ChooseToNotTriggerTrap`), so the bottom Move 3 **jumps** (traps passed over don't spring; it also passes
  enemies/obstacles, the last hex's trap still springs). "Destroy one trap in a hex you entered, XP 1": `DisarmTrap`
  with `AllTargetsOnMovePath`, `AbilityXP: 1` (XP only when a trap is destroyed). Exact form wants a plugin: a move
  flag that offers `ChooseToNotTriggerTrap` for this move only (the engine already has the confirm dialog,
  `CObjectProp.TryChooseToSkipTrap`), then no Jump.
- **09 Special Mixture**: bottom Poison gets its diamond mark (Xipe had none).
- **11 Smoke Bomb**: top = 1e Smoke Bomb (one-slot x2 while Invisible); bottom control = 1e Sinister Opportunity bottom
  (Move 1, RestrictRange 1). Both passed in the 1e bench.
- **12 Trickster's Reversal** (approximation, top): the Drifter's Accurate Strikes shape (persistent BuffAttack that
  fires only on a shielded target via Vars `ActorTarget.CalculateShield() > 0`), melee only, one slot XP 1, +2 Attack
  and **Pierce 99 instead of "+X where X = the Shield"**: same damage (Attack + X - X), except that a x2 modifier
  doubles X on the card and not here. Not lost (sheet). Bottom = 1e bottom.
- **14 Stick to the Shadows**: bottom "adjacent to no enemies" = Blinkblade Phasing Blades' requirement
  (`ValidActorsInRangeOfRequirementRange: "== 0"`, range 1, enemies); "lose Invisible at end of round" =
  `DecrementTrigger: Rounds` + round bonus (Drifter Accurate Strikes' advantage shape). Xipe's version kept the
  Invisible past the round.
- **15 Hidden Daggers**: as Xipe.
- **16 Flintlock**: top: an optional step before the attack (`TMoney`, NullTargeting on an enemy within range 2,
  offered only while she holds a money token) places a money token (GHMoneyTokens, see Money tokens below); the
  attack's +2 Attack, Pierce 2 override applies only to that enemy (`TargetTargetedByCasterPreviousAbility` +
  `SpecificAbilityNames: [TMoney]`). Base Attack 3 as printed (Xipe printed Attack 4). Bottom: Frosthaven's Trapper
  Honeypot top shape (loot stores itself in a temp var `SilentKnifeFlintlockLoot`); the trap offers only the hexes
  where that loot picked up a loot token (temp var `GHMoneyLootedTiles`, filled by the plugin) that are now empty.
- **18 Gruesome Advantage**: top = two exclusive overrides (flanking; isolated and not flanking), each +2 XP 1
  (Xipe gave no XP). Bottom (untested composite): Pyroclast Feed the Beast's one-slot round
  `DuringActionAbilityOnAttackFinished` bonus + Drifter Shockwave's melee check + a poison check on the target
  (`EnemyTarget.Tokens.HasKey(CCondition.ENegativeCondition.Poison)`), adding Attack 2, Range 3, Poison. It fires
  after the attack on the target, not strictly "after the attack ability"; the poison is checked after the hit.
- **20 Serrated Arrow**: bottom = 1e Spring the Trap bottom's shape (x2 on flanked + isolated), passed in the 1e bench.
- **21 Dance of Daggers** (top; Xipe's file held Cull the Weak's top): round PreventDamage, attack sources only = all
  attack damage negated; the added effects = a round `ImmunityTo` the conditions and push/pull (Crashing Tide Dug In;
  side effect: also blocks them from non-attack sources this round). The counterattack = Metal Mosaic Stress Vents'
  `DuringActionAbilityOnAttacked` reaction: a real Attack 2 stacked next to the monster's attack, aimed at the attacker
  when it is adjacent (no range printed = melee, user decision B), XP 1 per trigger (ProcXP). Bench it during a
  monster's turn (a forced attack there is untested). Lost (Xipe: not lost). Bottom = Drifter advantage.
- **22 Cull the Weak**: bottom targets any enemy with current HP <= 2 (Xipe also required a normal monster).
- **23 Stiletto Storm**: second attack behind a requirement on the first: `Type: PreviousAbility`,
  `AbilityHasHappened`, `ActorsTargetedByAbility: "== 0"` over targets that were NOT isolated (all isolated -> go).
  Untested; adjacency is read after the first attack (a target killed by it is judged as it lies). No mark on either
  Attack 2 (Xipe had marks). Bottom Attack 3 **SquareEnhancements** (printed square).
- **24 Spring the Trap**: top = Blinkblade Systems Reboot (IgnoreStun + RemoveConditions All), the heal also
  IgnoreStun. Bottom (approximation + NEEDS PLUGIN): no designation before the move; after Move 5 Jump, `MoveTrap`
  takes any trap within range 6 to an empty hex adjacent to you (MoveTrapRange 1 from the mover). Springing it on an
  enemy in that hex + XP 1 is impossible (traps only move to empty hexes) - NEEDS PLUGIN.
- **25 Visage of the Inevitable**: top Attack 4 **SquareEnhancements** (printed square). Disarm + XP 1 by override;
  Invisible on self as a sub-ability behind a SubAbility requirement counting flanked-and-isolated targets
  (Deepwraith Morbid Camouflage shape; Xipe's self-Invisible override couldn't work). Bottom Poison all adjacent
  as a condition ability (Xipe: Attack 0).
- **26 Crippling Poison**: two persistent bonuses (Poison on all attacks; +1 on poisoned targets), XP 2, lost.
  This +1 adds to e.g. Single Out's +3 (re-audit 2026-11-02: CAttackSummary folds with `Math.Max` only bonuses with
  the explicit AttackType Default, "your default attack"; these have none, so they already add, house rule or not).
- **27 Hired Help**: Frosthaven's own `ChangeAllegiance: ChangeToType: Ally` (scenario 119's Infuser conversion) on
  an adjacent normal enemy: a real permanent ally monster acting on its own. Offered only while she holds a money
  token; the plugin takes one (GHMoneyLose) and puts her character token on it (resource `SilentKnifeHiredHelp`);
  when it dies one loot token drops in its hex. Lost, XP 2 (Xipe:
  per-turn control, not lost). The physical "blue base" sentence is left out of the text. Bottom Disarm range has no
  mark (Xipe had one).
- **28 Watch it Burn**: top: Move 3 Jump + XP 1 only if >= 3 enemies were targeted (Shattersong Din of Battle's
  `ActorsTargetedByAbility` sub requirement; TreatAsNonSubAbility as Frosthaven's sub-ability moves); no mark on the
  Attack 2. Bottom (NEEDS PLUGIN): Attack 5 [diamond] as printed (Xipe: Move 3), but **as an ordinary bottom action**:
  "play from your hand as an extra action" has no Frosthaven form.

## Perks (perks.py)

- 4 "Pickpocket": new card `AMC_GH2E_SilentKnife_Pickpocket_P1` (+1, a self step marked `GHMoneyGainAdjacent`: one
  loot token when the attack's target is adjacent).
- 6 "Vanish": new card `AMC_GH2E_SilentKnife_Invisible_P1_R` (+1, Invisible self, rolling), as printed (Secretariat
  agrees on +1 rolling).
- 8 "Parting Gift": after each rest (short and long hooks), an optional step placing a money token in the hex of an
  enemy within range 3 (`GHMoneyPlace`), then Attack 1 on that enemy only (Boldening Blow's Custom targeting of the
  parent's target).
- 9 "Fence" (city event gold): a passive answered by the plugin (GHSilentKnife.cs): Frosthaven's outpost events stand
  for city events (QUESTIONS.md A8); on completing one she draws from her modifier deck as an Attack 4 (rolling cards
  add) and gains the damage as gold.
- 10 "Cloak of Invisibility" (linked): the 1e Scoundrel's once-per-scenario toggle, Cost 2.
- Masteries (GHSilentKnife.cs): 1 = FrosthavenMods_FlankedOrIsolatedOnly (every attacked target next to one of her
  allies or next to none of its own, 10+ attacks, checked as each attack happens); 2 = FrosthavenMods_MoneyTokensAtEnd
  (money tokens held at each scenario's end, 12+ counted in synced campaign state across scenarios, not necessarily in a
  row; completed from the scenario result at 3).

## affinities.txt

Empty: Dark is infused twice (Special Mixture, Smoke Bomb), Fire twice (Flintlock, Watch it Burn); none 4+.

## NEEDS PLUGIN (summary)

Dance of Daggers negating added effects (and a real Attack 2 counter). Built in GHSilentKnife.cs (rest-b, 2026-11-02):
Practiced Reflexes optional trap skip, Trickster's Reversal +X, Gruesome Advantage (Poison before the hit, after the
whole attack ability), Spring the Trap designation and springing on an enemy + XP, Watch it Burn play-from-hand, Fence,
both masteries.

## Money tokens (GHMoneyTokens.cs)

Frosthaven FAQ section 10: money tokens = loot tokens; returning a loot token to the scenario puts one of your loot
cards (not the random item) back on top of the loot deck; printed gold amounts stay as printed. Her money tokens = the
loot tokens she looted this scenario (character resource `SilentKnifeMoney`, counted by the plugin at Frosthaven's
loot-token pick-up). Placing or losing one: the count drops by one and one of her loot cards (her choice through
Frosthaven's loot-card picker when the values differ) goes back on top of the deck, its value leaving her loot; a placed
token is a real loot token in that hex. Gaining one = one loot-deck draw (Pilfer, Pickpocket).
Practiced Reflexes optional trap skip; Flintlock money-token cost; Spring the Trap springing the relocated trap on an enemy + XP; Hired Help token cost and extra
drop; Watch it Burn play-from-hand; perks 4, 8 (cost), 9.

## Shared changes wanted

None left from this list (built in GHSilentKnife.cs).

## Check

`GH2E_SKIP_RULECHECK_BUILD=1 python tools/gh2e/check.py SilentKnife` -> CHECK PASSED SilentKnife (packcheck 28 cards,
0 problems, 0 warnings; rulecheck 40 ok; no missing text keys; no gh_port warnings). Untested in game: the composites
marked above (Flintlock bottom, Gruesome Advantage bottom, Stiletto Storm top, Visage top Invisible, Hired Help top,
Trickster's Reversal top, Spring the Trap bottom, Watch it Burn top) want a bench run first.
- Re-audit fixes (2026-11-02, fix-reaudit-B): Dance of Daggers top's round ImmunityTo carries GH2E_AttackEffectsOnly: the
  plugin leaves it out of every immunity check that isn't an attack on her (conditions put on by a non-attack ability,
  traps, her own effects land). Mastery 1's counts are the hidden resources GH2EFlankAttacks / GH2EFlankBad (saved with
  the scenario). Spring the Trap's designation is used once and forgotten; with none, no trap can be relocated.

## "!" boxes (mandatory triggers, 2e rulebook p.35)

Drawn with Frosthaven's own `- MandatoryTrigger: Layout:` box (Banner Spear Javelin, Boneshaper Malicious
Conversion for two elements in one box), found on the card images (local/gh2e-data/cards/images/SC2), the
corner XP/lost/persistent boxes aside (Frosthaven draws its own corner). Each boxed effect is mandatory in the
data: half infusions always happen, element pickers and self/ally effects have `CanSkip: False` or are plugin
rules with no prompt.

- 09 Special Mixture top: infuse Dark
- 11 Smoke Bomb top: infuse Dark
- 16 Flintlock top: infuse Fire
- 28 Watch it Burn top: infuse Fire
