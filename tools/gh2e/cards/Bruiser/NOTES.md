# Bruiser (2nd edition): translation notes

29 cards, every one checked against `tools/gh2e/sheets/Bruiser.json` (values, marks, lost/round/persistent, XP,
areas, text rows). 28 started from Xipe's import, rewritten; Brute Force (28) is new. Layouts are plain Gloomhaven
style (gh_layout redraws them). Every printed rule has its own text key `GH2E_Bruiser_NN_T/B` in strings.tsv with the
card's 2e wording; Xipe's `GH2E_X_*` keys are no longer used.

## Class-wide decisions

- **Enhancement marks are written as Frosthaven slot keys** (`SquareEnhancements`, `CircleEnhancements`,
  `DiamondEnhancements`, `DiamondPlusEnhancements`, `PierceEnhancements`), straight from the sheet's marks. A plain
  `Enhancements: 1` would get gh_port's per-ability default (every attack a diamond, every move a circle), but 2e prints
  square attacks (Skewer, Leaping Cleave, Warding Strength...), square moves (Trample, Shield Bash...) and square heals
  (Eye for an Eye, Grab and Go), and 2e's stickers follow Frosthaven's shape rules. convert_enhancements leaves these keys
  alone; packcheck passes them. This is AUTHORING.md's "Enhancement mark shapes" rule; the slot key is written on every
  mark (also where it equals the converter's default), so no plain `Enhancements:` is left in the class.
- **Use slots:** XPTracker one entry per slot (AUTHORING.md); the engine pays entry i on the i-th trigger, which is the
  rulebook's "when the token passes an experience icon". Warding Strength [1,0,1,0,1,0] (Xipe had [0,1,0,1,0,1]),
  Juggernaut [0,1,0,1], Intimidating Growl [0,1].
- **"Undamaged" = at full hit points** (Sweeping Blow, Run Through): the sense Frosthaven itself uses ("Kill an undamaged
  monster", BattleGoal_Executioner) and Xipe's. Written as a per-target ConditionalOverride with filter
  `TargetMissingHP: "== 0"` (+1 Attack), the 1st edition Crushing Weight's shape.
- **"Gain XP 1 the next time you retaliate this round"** (Eye for an Eye top, Provoking Roar top, Selfish Retribution
  bottom): APPROXIMATION. ProcXP 1 on the card's round retaliate (the 1st edition Eye for an Eye's shape, play-tested):
  XP each time *this* retaliate triggers this round, where the card gives it once, for any retaliate. An XPTracker [1]
  would end the retaliate after one use. Active icon Positive_XP_Retaliate.
- No `$Self$` rows anywhere (house rule: heal/shield/retaliate are yours by default).
- affinities.txt is empty: the cards infuse Air 3 times (Overwhelming Assault, Whirlwind, Skirmishing Maneuver) and
  Earth once, under the 4+ rule.

## Per card

- 01 Trample: bottom written as AUTHORING.md / CARD_FIXES 20001 (plain Move, then Attack 3 with AllTargetsOnMovePath,
  Targeting All, Target -1, XPPerTarget 1). Move 4 [square].
- 02 Eye for an Eye: XP rule as above. Heal 3 [square].
- 03 Skewer: Attack 3 [square]; the area keeps the 1st edition line drawn diagonally, without its enhancement hex (the
  2e card has none).
- 07 Warding Strength: Xipe's ShieldAndRetaliate behaviour (Shield 1 + AbilityData Retaliate 1, advanced once per attack
  that deals damage). No Frosthaven card uses this behaviour; the engine has it (CShieldActiveBonus_ShieldAndRetaliate).
  TEST: the retaliate fires and the tracker advances per attack.
- 08 Shield Bash: the mark is on Move 2, not on Shield 1 (Xipe had it on the shield).
- 09 Leaping Cleave: no area hex mark (Xipe had one); the bottom Push 2 [square] range 1 is its own ability (Frosthaven's
  standalone push shape).
- 10 Spare Dagger: top unmarked; bottom Attack 2 [diamond] and Pierce 1 [square] (PierceEnhancements).
- 11 Sweeping Blow: Attack 2 unmarked (only the area hex mark); undamaged as above.
- 12 Fearsome Taunt: Push 3 [square]. Bottom redirect is the 1st edition Provoking Roar's Redirect (round) with its
  Frosthaven icon Positive_Redirect_Ally_Attack_Aura (CLASS_ICONS Brute BRedirect); Gloomhaven's "Brute" icon is gone.
  The card's "you may" choice isn't offered: the engine redirects as the 1st edition card did.
- 13 Provoking Roar: Pull + Range + Muddle is Frosthaven's own pull-that-muddles (Banner Spear's Taunting Howl). XP rule
  as above.
- 14 Intimidating Growl: the move buff is the Drifter's Sustained Momentum shape (Move 1, Filter Self, persistent
  BuffMove, XPTracker [0,1]), no AutoTrigger. It is placed after the printed Move 2, so that Move 2 is not one of "your
  next two move abilities" (the abilities resolve top to bottom; the sheet note suggested the Move 2 counts: I read the
  dashed slot as only where the token starts). Persistent, not lost.
- 15 Juggernaut: Frosthaven's PreventDamage N with an N-slot tracker (Boneshaper's Life in Death), any source of damage.
- 16 Hook and Chain: the 1st edition card's shape; no enhancement marks at all on the 2e card.
- 17 Unstoppable Charge: the +2 Attack / XP 1 is a ConditionalOverride on the attack with Requirement
  HexesMovedThisTurn >= 1 (the 1st edition Hatchet's Fearsome Efficiency shape), not Xipe's separate round bonus. Move 4
  [square]; Stun [diamond] target all range 1 (`Stun: {Amount: 1, DiamondEnhancements: 1}`, Blinkblade's Flashing Flurry).
- 18 Whirlwind: the 1st edition card's shape with 2e numbers.
- 19 Push Through: a round BuffAttack (+1) with EntireAction and XPTracker [0] (the Drifter's Fierce Barrage counts its
  uses per action this way), so it ends after your next attack ability instead of lasting the round (Xipe). TEST: every
  target of a multi-target attack gets the +1 (the tracker ends the bonus on the first target; the attack summary is
  computed before that, so it should hold).
- 21 Defensive Tactics: APPROXIMATION / NEEDS PLUGIN. Frosthaven has no trigger on "you gain Shield or Retaliate from a
  card"; the top is a persistent Shield 1 + Retaliate 1 (Xipe; the 1st edition Defensive Tactics shape), so it also
  applies in rounds where you gain neither. Card text as printed.
- 22 Run Through: the enhancement hex is beyond the line (8,0,E; Xipe put it on the last red hex 6,0). Undamaged as
  above. The bottom push carries the Immobilize (Conditions on the push).
- 23 Immovable Phalanx: the 1st edition toggle bonus, plus Restriction OncePerTurn ("once each turn"), icon
  Positive_Damage_Move.
- 24 Let Fly: APPROXIMATION / NEEDS PLUGIN. "Each enemy occupying a hex the target enters suffers damage 4 and Wound":
  Frosthaven's push has no hook for the figures the pushed target passes through. Written as Xipe read it, damage 4 +
  Wound on all enemies adjacent to the target, in the shape the 1st edition Cragheart's Blind Destruction passed with
  (inline damage sub-ability with AllTargetsAdjacentToParentTargets). TEST: whether the damage reads the target's hex
  before or after the push.
- 25 Selfish Retribution: XP rule as above; Xipe's flat XP 1 on the bottom removed.
- 26 Frenzied Onslaught: Heal X now Filter Self (Xipe had none).
- 27 Crippling Offensive: the top is not lost (Xipe had it lost). Muddle target all range 1 unmarked.
- 28 Brute Force (new): top Shield 2 [square] plus a round DuringActionAbilityOnAttacked bonus (Frosthaven's Energy
  Conversion / Stress Vents) whose ability is Move 1 with a Push 2 sub-ability (range 1, 1 target, Filter [Enemy, Ally],
  TreatAsNonSubAbility); the engine stacks it for you after each attack on you has dealt its damage
  (CDuringActionAbilityActiveBonus_TriggerAbilityOnAttacked.OnBeingAttackedPostDamage). No Frosthaven card moves the hero
  on an enemy's turn: **the riskiest shape of the class, bench it first** (move on a monster's turn; push an ally).
  Fallback if it misbehaves: keep the Shield 2 and drop the reaction. Bottom: +1 to every push and pull is Frosthaven's
  BuffForcedMovement (Snowdancer's Gathering Force, BuffValue 1). NEEDS PLUGIN: "after a push/pull of an enemy, it and
  all enemies adjacent to it suffer damage 1" is text only. Icons Positive_Attack_Enemy_Move (reaction),
  Positive_ForceMove_Damage (bottom).
- 29 Face Your End: exact. A ConditionalOverride whose filter compares HP (`Vars: ValidateLowerHP: target.Health <
  self.Health`, Frosthaven's filter Vars with target and self, as Metal Mosaic's Heat Conduction); Xipe had a fixed
  Health <= 13. TEST: the +3 and XP appear only against a target with fewer current HP.

## Perks (perks.py)

All 12 perks from the "Inox Bruiser" sheet, 18 checks. Names made up in BRUTE_PERKS style.
- 3 "Spiked Hide": APPROXIMATION. The sheet's card goes to your active area and gives Retaliate 2 on the next attack
  by an adjacent enemy; Frosthaven's nearest existing card is its rolling +0 Retaliate 2 (AMC_Retaliate2_P0_R, Metal
  Mosaic perk 4). The description keeps the sheet's wording.
- 6 "Shoulder Charge": new modifier AMC_GH2E_Bruiser_Push2_P2 (+2, Push 2), as Frosthaven's AMC_Push1_P2.
- 10 "Scavenger" (linked, Cost 2): gh_crossover._once_per_quest toggle; Loot 1, then RefreshItems 1 on a Body item
  (Spent or Consumed) gated by `Ability.ParentAbility.LootTokenCount > 0` (the loot's money-token count). Untested
  expression: bench it.
- 11 "Rested and Ready": the 1st edition Brute's crossover perk, word for word.
- 12 "War Cry": APPROXIMATION. Frosthaven's scenario-start advantage perk (Crashing Tide 10, Deepwraith 12) as an aura
  on every character (Boneshaper 06's aura filter), valid while RoundNumber == 1: advantage on every attack in round 1,
  not only each character's first attack.
- Masteries: the second sheet mastery = the Brute's Relentless Onslaught (plugin streak tag) with 7 damage; the plugin
  counts damage on the Bruiser's turns where the sheet says "during each of those rounds". The first ("in 3 different
  scenarios, kill an enemy that you pushed or pulled that round") has no existing pattern and is not written: the class
  has one mastery.

## Shared changes wanted

1. **Plugin: "XP the first time a retaliate triggers this round"** (once, from any retaliate source) for Eye for an Eye,
   Provoking Roar, Selfish Retribution. Now ProcXP (XP per trigger of the card's own retaliate).
2. **Plugin: Defensive Tactics trigger**: on the first Shield/Retaliate gained each round from an ability card or
   modifier, gain Shield 1 + Retaliate 1 for the round. Now always on.
3. **Plugin: Let Fly push collision**: damage 4 + Wound to each enemy in a hex the pushed target enters. Now: enemies
   adjacent to the target.
4. **Plugin: Brute Force bottom damage**: after you push/pull an enemy at least one hex, damage 1 to it and every enemy
   adjacent to it. Now text only.
5. **Plugin/modifier: Spiked Hide perk card** (rolling card that stays in the active area until an adjacent enemy's
   attack, then Retaliate 2 for that attack).
6. **Mastery: "kill an enemy you pushed or pulled that round, in 3 different scenarios"** (a plugin tag like the Red
   Guard's EnemiesForcedToMove, counted across scenarios).
7. gh_port prints "12 perks and 2 masteries" for this class: the count includes the new attack-modifier file (cosmetic).

## Check

`GH2E_SKIP_RULECHECK_BUILD=1 python tools/gh2e/check.py Bruiser`: packcheck 29 cards, 0 problems, 0 warnings;
rulecheck 43 ok; no missing text keys; no gh_port warnings. **CHECK PASSED Bruiser.**

In-game tests to run first (shapes no Frosthaven card uses): Brute Force top reaction, Warding Strength bottom, Push
Through bottom, Let Fly top damage, Face Your End bottom, the Scavenger and War Cry perks.
