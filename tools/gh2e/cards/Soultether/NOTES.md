# Soultether (Gloomhaven 2nd edition Summoner): translation notes

All 31 cards written from scratch from `tools/gh2e/sheets/Soultether.json` (no Xipe or Secretariat card data exists
for this class). Libraries: the 1st edition Summoner (raw and converted), Frosthaven's Boneshaper (summon grants, summon
auras, death/kill triggers), HIVE (Recall: replay a lost summon card, grant Teleport adjacent to you), Metal Mosaic perk
10 (lethal-only negation), Infuser / Metal Mosaic (on-attacked triggers).
Every text key is our own (`GH2E_Soultether_*`). Enhancement marks follow the print: a [square] on a Move, Attack, Heal,
Range, Target, Shield, Retaliate or Push is written as `SquareEnhancements`; the converter's defaults give the rest
(attack diamond, heal / positive condition diamond-plus, negative condition diamond).
"Unverified" = passes packcheck and rulecheck, never seen in the game: each is worth a bench run.

## Class-wide decisions

- **Grants to summons** ("Grant one/two of your summons: ..."): Frosthaven's Command the Wretched shape (ControlActor,
  `Target 1/2, Range 99, Filter OwnSummon`, `ControlActorType Player`), the summon's own value `+0/+1/+2`; fixed values
  ("Move 2", "Teleport 3", "Push 2") with `StrengthIsBase`. "Consume X: grant two of your summons instead" = `Target: 1`
  (+1) override on the control (Frosthaven's Critical Failure / Transfer of Essence); riders on a granted attack =
  overrides naming the attack inside the control (Frosthaven's Dark Tidings).
- **Teleport N** = Frosthaven's `Teleport2`, with the printed marks: square on 11 Burning Tether, 13 Drifting
  Stingers, the 22 Living Mountain grant and the 29 Interplanar Mastery grant (Frosthaven's Deepwraith Skewer the
  Flesh has a square Teleport2 slot; packcheck's PLUS_ONE has Teleport2), circle on 06, 18, 22's own, 25, 27, 31.
- **"X of your summons" with no range** (02 Forged Ferocity bottom, all grants): any distance (Range 99).
- **Ranged positive conditions** (08 Strengthen, 18 Ward): self or an ally, as a ranged heal. 27's "target 1 ally" is
  an ally only.
- **Melee summons** carry `Range: 1` (Frosthaven's default, the 1st edition summon files and the other 2e classes;
  confirmed by the lead).

## Cards

- **01 Ethereal Gust, 06 Mighty Bond, 07 Bonded Might, 15 Unwavering Hand, 23 Primal Infusion top, 28 Otherworldly
  Rage top**: plain grants (above). 07 bottom: "consume Air: Move 2" with no base move = a Null the consume fills
  (Gloomhaven's Barbaric Instincts). 06: consume Dark +2 Teleport = a Strength override on Teleport2 (Crashing Tide
  Sodden Soil). 15 bottom: the 1st edition Unwavering Hand bottom (control an enemy: Move 1), mark kept.
- **03 Living Night** bottom: "next two attacks targeting your summons: attacker gains disadvantage, then suffers
  damage 1" = two persistent auras on your summons, two slots each: `AttackersGainDisadvantage` (Deflecting Maneuver's
  bonus) and a hidden `DuringActionAbilityOnAttacked` damage 1 to the attacker (Stress Vents / Coalescing Darkness).
  APPROXIMATION: two trackers that should advance together. Unverified. Persistent, not lost (discarded at the end).
- **08 Burning Passion** bottom: full negation of the next damage to a summon this round = `PreventFirstDamage`
  (PreventDamage 99, Banner of Valor) as a round aura on your summons, one use.
- **09 Unending Dominance** top: recover one lost card + Invisible self; "on the next summon action you perform, any
  summons summoned by that action gain Invisible" = a one-slot trigger on your own ability end
  (`DuringActionAbilityOnAbilityEnded`, Shattersong Feedback) valid for a performed Summon ability, giving Invisible to
  **up to two of your summons, picked by you** (APPROXIMATION: the data can't name "the summons that action summoned";
  NEEDS PLUGIN for the exact rule). Unverified. Bottom: Move 2, grant Move 2 (both square marks).
- **10 Shadow Dart** bottom: Frosthaven's Decaying Will bottom word for word (BlockRetaliate aura on your summons).
- **12 Primal Assault** top: three separate consumes on the granted attack (Wound / Pierce 2 / Curse).
- **14 Biting Wind** top: Push 1 [square] as an inline push sub-ability; consume Air overrides both the attack (+1, +1
  Range) and the push (+1). Bottom: the grant is Move +0, then Push 2 on all adjacent enemies (Kinetic Transfer's push
  shape), each pushed target then suffers damage 2 (damage sub-ability with `UseParentTargets`). Read as part of the
  grant (the sheet's unclear note). Unverified.
- **16 Intervening Apparitions** top: consume Fire = an `AreaEffect` override (`0,0,G|2,0,R|1,1,R|3,1,R`) on the
  granted attack; unverified on a control's attack. Bottom: Metal Mosaic perk 10's lethal-only negation
  (`PreventFirstDamage` + `PreventOnlyIfLethal`, its `AbilityData` Invisible + Stun) as a persistent aura on your
  summons, slots [1,0,1]. Unverified: that Invisible + Stun land on the saved summon from an aura.
- **17 Void Imbuement** top: persistent BuffAttack (+1, Pierce 1) aura on your summons, slots [0,0,1], not lost.
- **18 Shaping the Void** bottom: Heal 1 on all your summons within range 1 (`Target -1, Filter OwnSummon`).
- **19 Deadly Mischief**: the Covetous Imp's "you gain the money token directly" is text only. **NEEDS PLUGIN** (no
  "gain a money token" in Frosthaven's data).
- **20 Strength in Numbers** top: "+1 Heal on each target adjacent to two or more allies" = ConditionalOverrides
  `TargetAdjacentAllies: ">= 2"` (Pinning Charge's test) on the heal; unverified on a heal (who counts as the target's
  ally). Bottom: the 1st edition `StatIsBasedOnX ActorCount`, counting `TargetType: OwnSummon` (the 1st edition counted
  every allied summon), Range = base 1 + X with `AddTo` (Hail of Spears). Unverified.
- **21 Volatile Flame** top: Curse is a condition ability; consume Dark adds Stun by a `Conditions` override on it.
- **24 Tear the Fabric** bottom: Shield 1 and Retaliate 1 for the round on every one of your summons (direct, as the
  1st edition Forged Ferocity bottom, rather than a control making each summon perform them).
- **25 Oozing Manifestation**: see Slime Spirit. Bottom: Poison, range 2 [square on the range].
- **26 Divided Mind** top: consume Air "grant the target of the heal: Push 1, Range 1, Immobilize" = a Null the consume
  fills with a grant aimed at the heal's own target (`TargetTargetedByCasterPreviousAbility` + `SpecificAbilityNames
  [THeal]`, the 1st edition Eye of the Void fix), the Immobilize a `UseParentTargets` sub-ability of the push.
  APPROXIMATION: when you heal yourself the grant finds no target. Unverified.
- **28 Otherworldly Rage** bottom: HIVE Recall top (`SelectAndPlayCard` from the lost pile, summon actions only,
  `BlockXP`); "its summons gain Ward, Invisible, Strengthen" = Ward + Invisible + Strengthen on **up to two of your
  summons, picked by you** (APPROXIMATION as 09; NEEDS PLUGIN for the exact rule). Then Invisible self. Unverified:
  where the replayed card ends up.
- **29 Interplanar Mastery** bottom: the grant performs Teleport 3 and Shield 1 (round) on the summon; consume Dark
  "all adjacent enemies suffer damage 1 and gain Muddle" read as adjacent to the granted summon (the row sits in the
  grant; the sheet's unclear note) = a damage sub-ability around the control's target added by the consume
  (`AllTargetsAdjacentToParentTargets` + `TreatAsNonSubAbility`). Unverified: that it is aimed after the teleport.
- **30 Imposed Will** bottom: two persistent `DuringActionAbilityOnAttacked` auras on your summons (TriggerActor =
  attacker, TriggerTarget = the summon, as Stress Vents), both requiring the summon has no Ward
  (`Tokens.HasKey(CCondition.EPositiveCondition.Ward) == false`): damage 1 to the attacker, then (hidden) Ward on the
  summon. Unverified: the order of the two, and whether the Ward test sees the Ward before or after the attack used it.
- **Rules question settled**: Unending Dominance's single dashed slot = one use (the bonus ends after the first summon
  action); Imposed Will top's "Attack -1" = the enemy's own attack -1, melee (as Mindthief2 Silent Scream).

## Summons (`summons/`, 12)

| Summon | ID | Model | Notes |
|---|---|---|---|
| Iron Beast | IronBeast2ID | IronBeast | HP 7, Move 2, Attack 1, Shield 1, Retaliate 1 |
| Shadow Wolf (x2) | ShadowWolf2ID | ShadowWolf | Pierce 1; death trigger: infuse Dark + Invisible on the other living Shadow Wolves (Flesh Bomb's `DuringActionAbilityOnDeath`, `HeroSummonClasses` filter). Unverified |
| Thorn Shooter | ThornShooter2ID | ThornShooter | attack slot = Poison only: **Attack 0 + Poison** (decision: GHS stores attack = poison; an attack of 0 still draws a modifier) |
| Rift Spirit | RiftSpiritID | **MysticAlly** (stand-in: an ethereal ranged spirit) | Attack 0 + Muddle, range 3; end of turn Shield 1 (round) on one ally within 3 = `SpecialAbilities` (Repair Drone). APPROXIMATION: the summon's AI picks the ally ("you control the grant abilities" NEEDS PLUGIN) |
| Floating Jellyfish | FloatingJellyfishID | **NailSphere** (stand-in: the 1st edition Summoner's floating spiked sphere) | Flying; +3 Move while Air is strong or waning = hidden BuffMove with an element Requirement. Unverified on a summon's AI move |
| Covetous Imp | CovetousImpID | **SandDevil** (stand-in: a small devil) | money token rule: **NEEDS PLUGIN** (text on the card) |
| Ember Sprite | EmberSpriteID | **BurningAvatar** (stand-in: a flying fire figure) | Flying; Retaliate 1 range 3 aura on allies within 3 while Fire is strong or waning (Lashing Tendrils' Retaliate aura + Banner range aura + element Requirement). Unverified |
| Rock Colossus | RockColossus2ID | RockColossus | Muddle, Retaliate 2; Heal 1 self at end of turn (`SpecialAbilities`); +1 to incoming heals (Voice from Below's `BuffIncomingHeal`, persistent) |
| Slime Spirit | SlimeSpirit2ID | SlimeSpirit | Poison; first lethal damage negated once (Metal Mosaic perk 10, one-slot). The second Slime Spirit it should summon: **NEEDS PLUGIN** |
| Void Eater | VoidEater2ID | VoidEater | kill while Dark strong/waning: you Curse one enemy of your choice (monsters share one modifier deck) via `DuringActionAbilityOnKill` (Drifter Fortitude, killer checked by class). Unverified |
| Fey Subjugator | FeySubjugatorID | **HealingSprite** (stand-in: the 1st edition Summoner's sprite) | Flying, range 4; after attacking a normal/elite enemy, that enemy performs Attack -1 (melee) = control sub-ability on the summon's attack target (`AttackOverride`, `UseParentTargets`, AI-driven `ControlActorType Ally`). Unverified |
| Prismatic Unicorn | PrismaticUnicornID | BlackUnicorn | Pierce 2; end of turn infuse Fire/Air/Dark with the element picker (`SpecialAbilities`). Unverified who picks |

The sheet's "Rift Spirit Move 2", "Jellyfish Move 1 / Attack 3", "Ember Sprite Move 3 / Attack 1 / Range 3", "Unicorn
Move 4" carry no mark, so no slot.

## Perks (`perks.py`, 13 perks, 18 checks)

- Composure (1), Rending Summons (1), Venomous Summons (1), Hexing Strikes (1, AMC_Curse_P1), Unfazed (1,
  IgnoreNegativeScenarioEffects): Frosthaven's own modifiers.
- **Planar Shift** (3): new `AMC_GH2E_Soultether_P0_GrantTeleport2` = Frosthaven's AMC_P0_ControlSummonMove2 with a
  Teleport2 2 grant. Unverified.
- **Veiled Servant** (3): new `AMC_GH2E_Soultether_P1_InvisibleSummon` (+1, Invisible on one of your summons; the
  AMC_Strengthen_Ally_P1 shape, performed by you via TryToPerformAsSummoner). Unverified.
- Blazing Gale / Smoldering Shadow / Night Wind (1 each): new +2 element-picker modifiers (Frosthaven has +1/+0 ones).
- **Restful Guard** (1): HIVE perk 8's long-rest hook (`PreCalcInitiativeAbility`, `IsLongRest`): Shield 1 (round) on
  all your summons once the cards are chosen. Unverified.
- **Summoner's Call** (1): the once-per-scenario toggle (gh_crossover `_once_per_quest`, rewritten so its ability aims
  at a summon) with HIVE Recall's grant: Teleport2 limited to the hexes adjacent to you. Unverified.
- **Phase Out** (2 linked = Available 1, Cost 2): Boneshaper perk 7's once-a-scenario choice at initiative ordering
  (`PreCalcInitiativeAbility`, OncePerRound, single use, skippable): Invisible on all your summons. APPROXIMATION: it is
  offered after the players' cards are chosen, before the monsters' cards are revealed.
- Masteries: none. "3 different summon actions before your first rest, cards kept all scenario" and "6 different summon
  abilities in the first rounds of 6 scenarios" fit no existing tag (the 1st edition Menagerie tag counts a whole
  scenario).

## Affinities

`[ Fire, Air, Dark ]`: Air infused on 5 halves, Dark on 5, Fire on 4 (gh_port agrees: no warning).

## Class description

No mat exists (no class notes): `Soultether_DESCRIPTION` / `_ADVENTURE_DESCRIPTION` in strings.tsv are our own short
text. HP, hand size and traits are GHS's (classes.json).

## Shared changes wanted

1. **Plugin** (NEEDS PLUGIN): Covetous Imp's money token on kill (19); the Slime Spirit's second spirit on its saved
   death (25); "summons summoned by that action" for Unending Dominance top (09) and Otherworldly Rage bottom (28),
   now "up to two of your summons, picked by you"; the player choosing the Rift Spirit's Shield target (05); masteries
   (two new tags: summon actions before the first rest kept all scenario; different summon abilities in round 1 across
   scenarios).

## Check

`GH2E_SKIP_RULECHECK_BUILD=1 python tools/gh2e/check.py Soultether` -> **CHECK PASSED Soultether** (31 of 31 cards,
12 summons, 13 perks; packcheck 0 problems / 0 warnings, rulecheck 62 ok, no gh_port warnings, no missing text keys).
