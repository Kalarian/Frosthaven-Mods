# Berserker (2nd edition): translation notes

All 28 cards written from scratch from `tools/gh2e/sheets/Berserker.json` (no Xipe or Secretariat data for this class).
Shapes come from the 1st edition Berserker (`gh1e_yml` 319-347, converted in `mods/gloomhaven`) and Frosthaven's own
cards; engine behaviour was read in the decompiled `ScenarioRuleLibrary.dll` where it decided a shape.

## Shapes used for the class rules (all cards)

- **"If your current hit point value is half your maximum or less"**: Gloomhaven's requirement key `HPPercentOfMax:
  "<= 50%"` (1e used `"< 50%"`, its rule being "below half"). The game compares `Health <= ceil(max x 50%)`, so it is
  exact for the 2e "half or less" (all max HP values are even). A bonus to the ability is a `ConditionalOverrides`
  entry with `Requirements:` (non-Self for attack strength or conditions, which the game checks per target against the
  caster; `Self: True` for target count, move, heal and the added push, which are applied at the ability's start). An
  extra ability performed only when low is `StartAbilityRequirements` with `HPPercentOfMax` + `XP: 1` (1e Growing Rage).
- **"You may suffer damage N to ..."**: a consume whose `CostAbility` is a self Damage (1e Strength in Agony / Dazing
  Wound). **"You may suffer up to damage N; X = damage chosen"**: one consume per amount in a `ConsumeGroup` (1e).
- **"Gain XP 1 the next time you retaliate this round"** (Resolute Stand top, Unbridled Power bottom, Tireless
  Retribution top): the round retaliate's `ProcXP: 1` (1e Eye for an Eye, Frosthaven Venomous Barbs). APPROXIMATION:
  the game gives the XP on *every* retaliate of the round, not only the first (CActor.ApplyRetaliateToAttack: one
  `GainXP(ReferenceXP)` per retaliate). A once-only XP needs a plugin (an XPTracker [1] would end the retaliate itself).

## Card by card

- 01 Numb the Pain top: "suffer 1 less damage while at half or less, this round" = Frosthaven's `PreventPartialDamage`
  round bonus (Frozen Fist Thick Frost) with an active-bonus `Requirements: HPPercentOfMax`. The requirement on a
  prevent bonus is unverified in play (Pain Conduit's Reject the Gift uses active-bonus Requirements on a prevent, so
  the shape exists). Bench check wanted.
- 02 Shiny Distraction bottom: advantage = Frosthaven's own shape (Deepwraith Death Spiral: `Attack: 0`,
  `AttackHasAdvantage`, round BuffAttack), not 1e's `Advantage: True`; disadvantage = `AttackersGainDisadvantage`
  (Shattersong Transparency).
- 04 Defiance of Death bottom: `PreventDamage` + `PreventOnlyIfLethal`, persistent, `XPTracker: [1]` (1e shape with one
  slot). Discarded (not lost) when spent, as the sheet's corner shows.
- 05 Resolute Stand top: the consume-Fire heal is its own ability before the retaliate (a `TNull` placeholder carries the
  consume, the 1e pattern).
- 06 Blood Pact top: the mandatory self-damage is its own first ability (From the Brink's TLower shape: self Damage,
  `CanSkip: False`, standard confirm), so it happens before the attack as printed and on the hit points before any
  retaliate; `RoundingType: ToFloor` for "rounded down" (the game's default rounds .5 up). Bottom: the start-of-turn damage has `StartAbilityRequirements: HP: "> 1"` (the game's
  absolute-HP requirement). No printed end: lasts until the card leaves the active area (lost), as 1e.
- 07 Cauterize bottom / 14 Into the Fight bottom: the optional self-damage pays for a condition-only ability (Wound +
  Immobilize via `Wound: True` + `Conditions: [Immobilize]`, as Frosthaven's Deathwalker Vengeful Storm; Stun range 1).
  Into the Fight's Fire infusion is the consume's own `Infuse: Fire` (optional, inside the "you may").
- 09 Unbridled Power top / 19 Seeing Red bottom / 27 Burning Hatred top: "target enemies up to two hexes away instead"
  = consume override `Range: 1` (adds to the melee range 1) with `TreatAsMelee: True` on the attack (1e Burning Hatred),
  so it stays a melee attack.
- 10 From the Brink top: remove negative conditions (Blinkblade Systems Reboot's `RemoveConditions: "All"` list);
  recover one lost card only at exactly 1 HP (`RecoverLostCards: 1`, `CanSkip`, requirement `HP: "== 1"`); set HP to
  half max rounded up = a Heal of `max(0, MaxHealthHalfRoundedUp - Health)` (Frosthaven's `BasedOn: Vars`, Pain Conduit
  Down to the Dirt) plus, only when above half, a self Damage of the excess. APPROXIMATION: lowering HP is "damage" in
  the engine (it can trigger Bathe in Blood / Vengeful Barrage, which a 2e HP set would not). Then Strengthen.
- 11 Glass Hammer top: 1e shape (`PercentageOfCurrentHP` x1, `GainXPPerXDamageDealt: 5`, `SetHPTo: 1`).
- 12 Growing Rage top: APPROXIMATION. No "whenever you retaliate" trigger exists; used Frosthaven's
  `DuringActionAbilityOnAttacked` with the attacker adjacent (Infuser Coalescing Darkness' `TriggerActor.IsInRangeOf`),
  i.e. it heals whenever an adjacent enemy attacks you, even if you have no retaliate active. The "repeat once if still
  at half or less" is exact: a Self conditional override +1 Heal when `Health + 1 <= MaxHealthHalfRoundedUp`.
  NEEDS PLUGIN for an exact on-retaliate trigger.
- 15 Final Fight top: `EndTurnAbility` persistent bonus performing Attack 5 with `AttackHasAdvantage`,
  `XPTracker: [0,1,1,1]` (this turn + the next three), lost after. NEEDS PLUGIN: "you become exhausted when this card
  moves to your lost pile" is text only (no Frosthaven shape ties exhaustion to the tracker running out; 1e's
  `ExhaustSelf` exhausts at the end of the action that uses it). As written the card is stronger than printed.
  Bottom: the round icon inside the consume box = the consume's own round +1 Attack bonus (1e Vengeful Barrage bottom).
- 16 Tireless Retribution bottom: 1e Unstoppable Destruction's `ImprovedShortRest: True` (same rule), icon
  `Positive_Tide_Rest` (also in icons.json, as 1e's CLASS_ICONS).
- 17 Unstoppable Destruction top: consume override adds a Damage 2 sub-ability with `AllTargetsAdjacentToParentTargets`
  + `TreatAsNonSubAbility` (the Rock Slide / Virulent Strain lessons in CARD_FIXES) and a filter
  `not adjacent to you` (Vars `target.GetDistanceTo(self) > 1`, Frosthaven's filter-Vars names). Unverified in play.
- 20 Vengeful Barrage top: 1e shape (`DuringActionAbilityOnDamaged`, triggered after the damaging ability), 6 slots
  XP on 2/4/6, plus the "while an enemy is adjacent" check as active-bonus Vars (Pain Conduit Agony of Others' enemy
  count, range 1).
- 21 Flurry of Axes / 27 Burning Hatred bottom: "card stack with red X" read as Permanently Lost (as other 2e sheets).
- 22 Devil Horns: 1e area without its enhanceable hex (the 2e card prints no hex mark).
- 23 Careless Charge top: APPROXIMATION. An optional consume: gain Wound (self `CostAbility`) for +X Attack, X =
  `HexesMovedThisAction` as an override `StatIsBasedOnX`. The two conditions (moved in a straight line; not already
  wounded) are not enforced: the text tells the player (consumes take no requirements; `MoveWasStraightLine` only
  checks a referenced previous ability). NEEDS PLUGIN for enforcement. Unclear rule settled: gaining Wound is optional
  (the player may skip the consume). Bottom: Retaliate 0 with `StatIsBasedOnX: CasterAdjacentEnemies`, round.
  Unverified whether X is re-read on each attack or fixed when played (bench check wanted).
- 24 Fueled by Pain top: consume override adds a self Heal sub-ability with `BasedOn: DamageInflictedByParent`.
  Bottom: plain Move then its own Attack with `AllTargetsOnMovePath` + `Targeting: All` (the Trample lesson) and a
  `TargetHasNegativeCondition: Wound` filter (Blinkblade Sand in the Hourglass).
- 25 Bone Breaker bottom: APPROXIMATION. 1e shape, `Conditions: [StopFlying]` on the attack; the character token and
  its persistent icon are not modelled (the card is discarded; the loss of flying lasts as the engine's StopFlying does).
  Unclear rule (no printed end) left as the 1e port had it.
- 26 Spiked Armor top: "enemies damaged by your retaliate this round gain Wound" = `Conditions: Wound` on this card's
  round retaliate (Frosthaven's Venomous Barbs gives a retaliate a condition). APPROXIMATION: other retaliate sources
  that round don't wound. "Negate the next damage this round" = Pain Conduit Reject the Gift's round
  `PreventFirstDamage`, `XPTracker: [0]`. Unclear rule settled: the prevent comes after the retaliate's optional
  self-damage, so the card's own 2 damage is not negated by it. Bottom: Strengthen added through a Self conditional
  override, so the half-HP check is made before the heal.
- 27 Burning Hatred bottom: APPROXIMATION. "Repeat any number of times, damage +1 each time": the first pass (suffer 1,
  Move 1, Attack 2) plus one optional consume group offering 1, 2 or 3 more repeats, paid up front (2, 2+3, 2+3+4
  damage), each adding Move 1 + Attack 2 per repeat. Capped at 4 passes in all; the damage is taken before the extra
  moves and attacks, not between them. "Cannot be negated" is not modelled (a prevent effect could stop it).
  NEEDS PLUGIN for an open-ended repeat.
- 28 Bathe in Blood top: `DuringActionAbilityOnDamaged` (immediate) with an AddActiveBonus Damage 1 on all enemies
  with Wound (`Targeting: All`, `TargetHasNegativeCondition: Wound`). Bottom: Pull and Muddle are their own abilities.

## Perks (perks.py)

- Perk 3 "Reckless Blows": APPROXIMATION. The +1 card's "you may suffer 2 damage to add +2 Attack" can't be offered by
  a modifier (its abilities run after the attack's damage); it is a plain +1 (AMC_Basic_P1). NEEDS PLUGIN for the choice.
- Perk 7 "Blood Frenzy": new modifier AMC_GH2E_Berserker2_SelfHalfHP_T2 (x2, no shuffle as a class card, then self
  damage of half the current HP rounded down, after the attack).
- Perk 2: new modifier AMC_GH2E_Berserker2_Heal1Self_P0 (+0 Heal 1 self; Frosthaven only has +1 and rolling ones).
- Perk 10 "Defy Death" (2 linked boxes = Cost 2): Frosthaven's own lethal-damage-sets-HP-to-1 (Boneshaper perk 6:
  `PreventDamageSetTargetHealthTo: 1` with the `PreventAndDamageActiveBonusCaster` behaviour), one use, attacks only.
  NEEDS PLUGIN: the "perform Attack 2 targeting the attacker" part is missing.
- Perk 11 "Taunting Fury" (3 linked boxes = Cost 3): NEEDS PLUGIN. Purchasable, no effect yet (end-of-turn control of
  every adjacent enemy to attack the Berserker).
- Masteries: none written (MASTERIES = []); "Kill 20 enemies with your retaliate" and "end all your turns at half HP or
  lower without exhausting" have no existing pattern (would need plugin tags like the 1e Berserker's).

## Shared changes wanted

- Plugin: an on-retaliate trigger (Growing Rage top; would also make the once-only "XP the next time you retaliate"
  exact for Resolute Stand, Unbridled Power, Tireless Retribution).
- Plugin: exhaust when Final Fight's top leaves the active area for the lost pile.
- Plugin: Careless Charge's straight-line + not-wounded conditions; Burning Hatred's open-ended repeat with growing,
  unpreventable damage; the Berserker perk 3 choice, perk 10's counter-attack and perk 11's control.
- Bench checks (first play): Numb the Pain top (HP requirement on a prevent bonus), Careless Charge (both halves),
  Unstoppable Destruction top (splash filter), From the Brink top (heal / lower amounts), Growing Rage top.
- gh_port's summary line counts the two new attack-modifier files as "masteries" ("11 perks and 2 masteries"):
  cosmetic.

## Check

`GH2E_SKIP_RULECHECK_BUILD=1 python tools/gh2e/check.py Berserker2`: packcheck 28 cards, 0 problems, 0 warnings;
rulecheck 42 ok; no gh_port warnings; CHECK PASSED Berserker2.
