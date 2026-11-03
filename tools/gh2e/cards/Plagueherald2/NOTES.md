# Plagueherald (2nd edition): translation notes

All 29 cards were written from scratch from `tools/gh2e/sheets/Plagueherald.json`. There is no Xipe or Secretariat
data for this class. The shapes come from Frosthaven's own cards (Deathwalker, Deepwraith, Crashing Tide, Drifter, Pain
Conduit), the 1st edition Plagueherald / Hatchet / Mindthief / Voidwarden ports and the engine itself (a decompile of
ScenarioRuleLibrary 1.0.34121, read for this port). Lost icons, XP, use slots and enhancement mark shapes were checked
against the sheet card by card. A printed square on an Attack, Move or Heal is written as `SquareEnhancements`:
Biting Gnats, Delirium's Danse, Rot Maggots, Nightmarish Affliction, Cloying Miasma, Wilt and Wither, Creeping Lethargy,
Rabid Bite (controlled Move 1) and Convert the Flock (controlled Move 2).

## Commanded enemies

Controlled enemies are driven by the player (Frosthaven rulebook p33, "the commanding figure decides how the ability is
performed"; approved rule B): Spread the Plague, Rabid Bite, Convert the Flock and the Xorn's Last Gift perk use
`ControlActorType: Player` (the 2nd edition Mindthief's shape: the controlled monster becomes a Player-type figure for
the action, so the move and attack ask the commanding player; GetControllingPlayer gives the input to that player).
The monster AI no longer picks the move or the targets. Rabid Bite's "this movement must move toward a monster that does
not have Plague": the controlled Move 1 may only end on a hex adjacent to its start that is closer (in hexes along a
path around walls) to at least one other monster without Plague (Frosthaven's `MoveRestrictionType:
MustEndOnSpecificTiles`, Trapper Lure of the Snare's shape).

## Plague tokens: how they are built

- **Resource.** Plague is a character resource `Plague` (`resources.yml`, Gloomhaven's CharacterResources format,
  `MaxAmount: 3`). The engine caps each actor at MaxAmount, so **an enemy can't hold more than 3** (class mat rule).
  `TargetHasCharacterResource: ["Plague"]` in a filter means "an enemy that has Plague", the way the Hatchet's cards check
  The Favorite.
- **The class supply of 12 (plugin GHPlague).** `ResourcesToGiveToTargets` moves a resource out of the giver's own
  stock, so the plugin makes the giver of a Plague give (the Plagueherald, an ally, a controlled monster) "hold" the
  supply while the engine's give site runs: the target gains one, nobody loses one. No supply on the Plagueherald, no
  refill, no "Plague 3" on his status row. Every Plague placed counts the tokens on all living non-player figures; with
  12 placed, the Plagueherald's player first picks the placed token to remove (one Remove / Next box per enemy that has
  Plague; one candidate = no question). A figure holding 3 takes no more and no token is removed for it.
- **Tokens leave with a dying enemy.** `DropOnDeath` is off, so no prop drops (they go back to the supply).
- **Per-token scaling on attacks.** "+1 Attack for each Plague the target has" is a per-target Vars bonus:
  `ConditionalOverrides` + `StatIsBasedOnX` (`BasedOn: Vars`, `AddTo`), Frosthaven's Crashing Tide Submerge shape. The
  value is `TargetActor.GetCharacterResourceAmount("Plague")`, a public CActor method that the expression engine calls
  by reflection (CVarsContext.CallObjectMethod). UNVERIFIED in play.
- **Per-token scaling on damage and on -Attack / -Move auras (plugin GHPerPlague, MiscVars `GH2E_PerPlague: True`).**
  A marked Damage ability deals its printed damage once per Plague on each target (the target's damage summary is
  raised before the damage); a marked attack/move aura applies its -X once per Plague on the attacking/moving enemy:
  Virulent Strain bottom, Wilt and Wither top, Rot Maggots bottom, Disease Vector bottom, Unwilling Sacrifice top,
  Blistering Buboes bottom, Creeping Lethargy top, Festering Sores bottom.
- **Plague added by others.** Controlled monsters' commanded attacks (Spread the Plague top, Rabid Bite top, Convert the
  Flock top) carry `ResourcesToGiveToTargets: [Plague, 1]` and give from the class supply. Virulent Strain top's ally
  aura is marked `GH2E_GivePlague`: plugin GHPlagueByAlly gives the attacked enemy one Plague when a use slot is spent.

## Rules questions settled (sheet "unclear")

- **"Target all" with no printed range** (Biting Gnats top and bottom, Delirium's Danse bottom, Crippling Malady bottom,
  Disease Vector top, Creeping Lethargy top, Vile Pestilence top): every enemy in the scenario (`Targeting: All`). This is
  how the 1st edition digital port reads the same wording (Airborne Toxin bottom). Plague is the scope on all of them
  except Biting Gnats bottom. The heal (Creeping Lethargy) covers you and all allies.
- **Single-target text effects with no range** (Path of the Pox top, Blistering Buboes top, Unwilling Sacrifice top,
  Accelerated End bottom, Spread the Plague top, Rabid Bite top, Convert the Flock bottom, Xorn's Embrace bottom, and the
  "any range" attacks): `Range: 99` (line of sight still applies), as Boneshaper's Range 99 controls. Xorn's Embrace top
  grants all poisoned allies with no range.
- **Virulent Strain top:** any of your poisoned allies' attacks use the slots (an aura over all poisoned allies).
- **Mass Extinction top:** each kill chains a new attack on another enemy with Plague, at +2 Attack each time.
- **Sickening Strength top:** not built (see below).
- **Tainted Offering bottom:** Recover read as recover (Sunkeeper 2e's level-1 recover shape).
- **Rot Maggots top:** "each ally adjacent to the target" means your allies, because 2e poisons allies on purpose. The
  1st edition card meant the target's allies (CARD_FIXES 20304).

## The "give one ally Poison" costs

- **Optional costs** (Delirium's Danse top, Gift of Rot bottom, Nightmarish Affliction top, Tainted Offering top): written
  as Frosthaven's cost-consume, a `Consumes` entry with a `CostAbility` (Poison on an ally within range 3) and the
  overrides or abilities it buys. Drifter's Break Through and Deathwalker's Anger of the Dead use this shape.
- **Ally pick.** The class mat allows only an ally without Poison. The pick checks this with a filter Vars
  (`not target.Tokens.HasKey(CCondition.ENegativeCondition.Poison)`), as Deepwraith's Soul Hunger checks conditions.
- **Comorbid Sting bottom:** an optional Poison on an ally. Its sub-ability then controls the same ally
  (UseParentTargets) for the granted Attack 2 + Poison, the 1st edition Voidwarden's Gift of the Void shape.
- **Nightmarish Affliction bottom:** the 1st edition Grim Bargain shape, a toggle +1 Target bonus with a CostAbility.
  Ranged attacks only, 6 slots.
- **Gift of Rot top, Tainted Offering bottom, Xorn's Embrace top:** "heal as if they did not have Poison" uses
  `AdditionalHealData: IgnoreTokens: True` (CActor.Healed skips the Poison removal and heals). APPROXIMATION: it applies
  every time (the card says "you may"), and it also skips Wound/Bane/Brittle removal.

## Decisions per card

- **01 Comorbid Sting:** see costs above. The granted attack has no range (melee).
- **03 Virulent Strain:**
  - Top: a +1 Attack aura on allies with Poison, 3 slots (XP on slot 1). The Plague on their attacks: GHPlagueByAlly.
  - Bottom: 1 damage per Plague (GHPerPlague).
- **04 Contagious Corpse:**
  - Top: the kill-gated sub-ability (`ActorsKilledByAbility`, Hatchet's Fearsome Efficiency) adds one Curse to the
    monster deck (`AddModifiersToMonsterDeck`, Voidwarden's Taunting Fate) and gives XP 1.
  - Bottom: Shield 2 for the round on one ally within range 3 that has Poison.
- **05 Path of the Pox:**
  - Top: 3 damage, then 1 damage to the enemies adjacent to it (TreatAsNonSubAbility, CARD_FIXES 20301).
  - Bottom: the chain is three nested Plague sub-abilities. Each is aimed within range 3 / 2 / 1 of the previous target
    (`AllTargetsAdjacentToParentTargets` uses the ability's Range as the radius). "No enemy more than once": each link
    picks only an enemy without Plague. Untested nesting.
- **07 Blistering Buboes top:** four Damage abilities (4/3/2/1), each excluding the earlier ones' targets
  (`IgnorePreviousAbilityTargets`).
- **08 Festering Sores top:**
  - The persistent trigger is Deathwalker's "when an enemy dies" shape (When Your Time Comes bottom: a self aura over all
    enemies, `DuringActionAbilityOnDeath`, `TriggerOnCaster`), limited to enemies with Plague, one use. The card goes to
    the discard pile after the trigger.
  - Plugin GHFesteringSores: "you may" is a yes/no box (no: the card stays; no enemy within range 3 of the hex: not
    asked, the card stays); the two targets are picked among the enemies within range 3 of the hex where it died
    (Custom targeting, no line of sight: a standalone Plague, FAQ 8.11).
- **09 Wretched Yield top:** +1 Attack per token (see above). Heal X reads the attack's first target through
  `CustomAbility.FindAbilityInPhaseByName("TAttack").ActorsTargeted[0]`. UNVERIFIED expression.
- **10 Accelerated End:**
  - Top: a conditional override on "target has Plague" gives XP 1 and the extra Plague (Hatchet's Follow Through check).
  - Bottom: designate any enemy with Plague, then Poison all figures adjacent to it, allies and you included
    (2e p.19: "all figures" targets both).
- **12 Gift of Rot:** heal with IgnoreTokens; the bottom cost buys Drifter's round advantage bonus.
- **13 Spread the Plague:**
  - Top: control for one Attack 4 (1st edition Hostile Takeover).
  - Bottom: Poison + Plague on one enemy adjacent to a poisoned ally (custom target list), then the mandatory "remove
    Poison from that ally" as a sub-ability on a poisoned ally adjacent to that enemy (printed order).
- **14 Sickening Strength top:** Strengthen only. "May suffer 2 damage to immediately play one card from their hand"
  needs another player's extra card play. Text only, NEEDS PLUGIN.
- **15 Rot Maggots bottom:** an `EndTurnAbility` bonus with 4 slots (XP on slots 2 and 4), 1 damage per Plague.
- **17 Cloying Miasma bottom:** a round attack aura with `AttackHasAdvantage`, whose bonus filter names a target with
  Plague (1st edition Doomstalker's Expose, IsDoomed). UNVERIFIED that the aura passes the target filter.
- **18 Rabid Bite top:**
  - Strengthen, then control with Move 1 + Attack 5. Whether the strengthen boosts the controlled attack is up to the
    engine.
  - "Must move toward a monster without Plague": the player moves it (Player control); the end hex must be closer to
    a monster without Plague (MustEndOnSpecificTiles, see Commanded enemies).
- **19 Unwilling Sacrifice top:**
  - 2 damage per Plague (GHPerPlague). Plugin GHUnwillingSacrifice: after the damage the player removes tokens one box
    at a time (up to the Plague it had before the damage and the enemies adjacent to its hex; a kill doesn't stop it),
    then the spread template (TSpread, taken off the card when read) runs with that many targets among those adjacent
    enemies: 1 damage + Plague each.
- **20 Wilt and Wither bottom:** a persistent `EndTurnAbility` marker with two use slots (XP 1 each): it ends at the end
  of your next turn. While it is in play plugin GHWiltAndWither doubles every damage an enemy with Plague suffers.
- **21 Compounding Infection top:**
  - A persistent one-use DamageOnCurseModApplied bonus (the 1st edition Baneful Hex's curse-drawn trigger), marked
    `GH2E_CompoundingInfection`: plugin GHCompoundingInfection asks instead of dealing damage; yes = the attacker gains
    two Curses and two Plague and the card goes to the discard pile; no = it stays.
- **21 Compounding Infection bottom:** a persistent +1 Attack aura on character allies (`ActorType: Player`) with
  Poison.
- **23 Succumb to the Gift top:** on a kill, XP 1 and a 3 damage + Poison trap. The trap is placed on the attack's hex
  (Custom targeting on `ParentAbility.m_TilesSelected[0]`, as CARD_FIXES 20136) with the `PoisonMine` trap name (the 1st
  edition Tinkerer's). "Featureless" is not checked. Untested shape.
- **24 Mass Extinction top:** chain of kill-gated follow-up attacks (Attack 6, 8, ... 18, XP 1 each) on another enemy
  with Plague, eight attacks deep (GHNestedSubAbilities queues any depth).
- **25 Tainted Offering top:** the bought Attack 2 is made from your own hex, not from the ally's.
  APPROXIMATION / NEEDS PLUGIN.
- **26 Creeping Lethargy bottom:** a one-use damage negation (`PreventDamage: -1`, the Protective Blessing value,
  `XPTracker: [0]`) on one adjacent ally with Poison, for the round.
- **27 Convert the Flock:**
  - Top: control all normal/elite enemies with Plague for Move 2, Attack 5 + Poison + Plague (class supply, GHPlague).
  - Bottom: the target pick takes an enemy with Poison or Plague (filter Vars). XP 1 on a kill (`GainXP`). The optional
    Teleport to its hex is not built (NEEDS PLUGIN).
- **29 Xorn's Embrace:**
  - Top: one control over all allies with Poison, each granted Attack 3 Range 3, then Heal X = DamageInflictedThisAction
    (Geminate's Hirudotherapy) with IgnoreTokens. Untested with several allies.
  - Bottom: an `EndTurnAbility` persistent 3 damage on one enemy with Poison and Plague.

## Perks (perks.py)

- 18 checkboxes. Modifiers: Frosthaven's own (`AMC_Curse_P1`, `AMC_Immobilize_P1`, `AMC_Heal1_1Ally_R`, `AMC_Basic_*`),
  the shared `AMC_Stun_P1` (gh_crossover NEW_CARDS) and two new ones.
- **The two new modifiers:** `AMC_GH2E_Plagueherald2_Plague_P0` and `_Poison_R`, "the target or one enemy within range 2
  of the target gains Plague / Poison". They use the targeting of Frosthaven's AMC_Damage1Range1_P0. Untested.
- **Winged (Flying):** the 1st edition Winged Congregation persistent Fly bonus, applied at scenario start.
- **Purging Rest:** long-rest hook. Remove Poison from one ally (optional), then Heal 1 on yourself. APPROXIMATION: a
  second heal, not +1 on the rest heal.
- **Xorn's Last Gift (2 linked boxes):**
  - Built as a once-per-scenario toggle: during your turn, control one enemy with Plague for one turn.
  - "When it would die" and "adding Plague to its attacks" are NEEDS PLUGIN (there is no pre-death hook).
- **Masteries:** none written (no existing pattern for either). Plugin mastery tags are wanted.

## Shared changes wanted

- Done by the lead during this port:
  - build.py support for `resources.yml` and `character_extra.yml`.
  - The card glob skips those two files.
  - The GHClass_Plagueherald2 status icon.
- **Done (plugin GHPlague.cs, 2026-11-02):** per-token effects, Plague from others, the 12-token pool, Festering Sores
  range and "may", Compounding Infection trigger, Unwilling Sacrifice token removal, Wilt and Wither two-turn doubling.
- **Plugin: the rest.** Sickening Strength extra card play, Convert the Flock teleport, Tainted Offering "from the
  ally's hex", Xorn's Last Gift perk at death.
- **In-game bench wanted:**
  - The expressions (`GetCharacterResourceAmount` by reflection).
  - The cost-consumes with an ally pick.
  - Path of the Pox's nested chain.
  - The Plague status icon on enemies.

## Check

`GH2E_SKIP_RULECHECK_BUILD=1 python tools/gh2e/check.py Plagueherald2`: **CHECK PASSED**. 29 of 29 cards, packcheck 0
problems / 0 warnings, rulecheck 46 ok, no gh_port warnings, character resources: Plague.
- Perk Toxic Touch: modifier rewritten, `AMC_GH2E_Plagueherald2_Poison_R` gives Poison with a NullTargeting + `Conditions: [Poison]` + `IsNegative` (Frosthaven's Infuser Diamondization shape), same target-or-enemy-within-2 targeting as the Plague card (a Poison ability on a modifier card stops the game when drawn). Exact.

## Card-accuracy pass (2026-11-02): supersedes the APPROXIMATION / NEEDS PLUGIN notes above
Plugin GHPlagueherald2e.cs (each rule logs "GHRule <Name> card <id>"):
- Gift of Rot top, Tainted Offering bottom: the heal through Poison is the Plagueherald's choice per target
  (GH2E_HealThroughPoisonChoice); Xorn's Embrace top always (GH2E_HealThroughPoison). Heal through = hit points added,
  Wound/Bane/Brittle removed, the Poison token kept (IgnoreTokens is gone).
- Sickening Strength top: each strengthened character may suffer Damage 2 and play one card from their hand
  (templates PH2_SickDamage / PH2_SickPlay).
- Nightmarish Affliction bottom: every ranged attack ability of the holder uses a slot, paid or not.
- Succumb to the Gift top: the trap's PlacementTileFilter is FeaturelessHex (data).
- Tainted Offering top: the bought attack is made from the poisoned ally's hex (TargetingOverrideArrayIndex).
- Convert the Flock bottom: on a kill, a skippable Teleport onto the killed enemy's hex (data, Infuser shape).
- Purging Rest: +1 on the long rest's own heal when an ally's Poison is removed (no second heal).
- Xorn's Last Gift: the real "would die" rule through the engine's on-death path, once each scenario (hidden resource
  GH2EXornUsed).
- Masteries: FrosthavenMods_KillsWithoutModifierInTurn (3 kills in one turn of his, no modifier drawn by him) and
  FrosthavenMods_PoisonEveryRound (Poison given or removed by him in every round he took a turn).
