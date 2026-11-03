# Quartermaster (2nd edition): translation notes

30 cards, all hand-written (no Xipe data), checked against `tools/gh2e/sheets/Quartermaster.json` (values, marks,
lost/round/persistent, XP, areas, text rows), with Secretariat's action trees for areas and the 1st edition
Quartermaster (gh1e_yml 205-232, converted pack 20205-20232) for shapes. Every printed rule has a text key
`GH2E_Quartermaster2_NN_T/B` (shared ones: `_SUPPLIES10/20`, `_SPEND_SELF/GROUP`, `_RECOVER1`) in strings.tsv with
the card's 2e wording. Each card file's header comment says what was decided for that card.

## Class systems

### Supplies = a character resource (resources.yml, character_extra.yml)
- Resource `Supplies` (Gloomhaven CharacterResources format), `MaxAmount: 50` (the engine clamps:
  CCharacterResource.Amount), real numbers (gain 10 = +10), tooltip `GH2E_SUPPLIES_TOOLTIP`.
- Start with 10: `StartScenarioAbilities` in character_extra.yml (the Plagueherald2 shape, NullTargeting + AutoTrigger).
- **Gain N supplies** (cards 05, 08, 11, 15, 18, 24, 30): its own `Null: True` ability (Filter Self, standard confirm)
  with `ResourcesToAddOnAbilityEnd: [["Supplies", N]]`; the engine adds it when the ability has happened
  (CAbility.AbilityEnded). A separate ability, not hung on the move, so a skipped move doesn't lose the supplies.
- **Spend supplies** (the dashed box: cards 02, 09, 14, 17, 24, 27; Set Up Shop top's trigger) = one optional
  ability: `NullTargeting` on the printed target(s), `AbilityXP: 1`, `StartAbilityRequirements` Vars
  `ValidateSupplies: Actor.GetCharacterResourceAmount("Supplies") >= 10` (the mat's "at least 10"), `MiscVars:
  GH2E_QMSpend: 1`. The plugin (GHQuartermaster.cs) does the mat's rule on the first target: all supplies spent (track
  to 0), then the player may Recover any of the targets' spent or lost items with combined gold cost <= the amount
  spent, ONE shared pool for all targets (GH2e FAQ), one yes/no per affordable item. Only items with a printed gold
  cost (ItemCard `Cost` > 0; FAQ), never the class items.
- Optional box as its own ability: the dashed box is "you may"; a top-level ability gets the standard skip. Sharpening
  Kit's box ("Recover the target's items") targets the heal's target (`TargetTargetedByCasterPreviousAbility` +
  `SpecificAbilityNames: [THeal]`, the Feral Aid / Divided Mind filter).
- Display: the class's status icon carries the supplies number (plugin), tooltip "Supplies: N".

### The Quartermaster item supply (9 class items, plugin GHQuartermaster.cs)
- Items: `items/QM1..QM9` from the user's image of the item cards (exact text, area, infusions; all lost-icon), pack
  ItemCards 33201-33209: Slot QuestItem (no slot requirement), TotalInGame 1, Cost 0 (no printed gold cost), no
  campaign item entry (never in the shop), Frosthaven item art of a similar item (Cruel Dagger, Long Spear, ...).
  Proficiency bottom's +1 copies: 33217 Iron Plate (Shield 3), 33218 Barbed Strip (Retaliate 3).
- Every "give / gain <item>" is a `NullTargeting` on the printed target (`IsTargetedAbility: False`: giving is not
  targeted, FAQ; `ActorType: Player`: only characters, FAQ) with `MiscVars` `GH2E_QMGive: N` (item number as on the
  card codes QM1-QM9), `GH2E_QMGive2` (a second item), `GH2E_QMGiveOr` (the player picks one of two) and
  `GH2E_QMGiveXP` (the "to gain XP 1", paid only when something was given: no half XP). Random draws:
  `GH2E_QMGiveRandom`, Booster Pack `GH2E_QMBoosterPack`, Giant Club `GH2E_QMGiantClub`.
- Supply = the items no living character holds unused. A used item is back in the supply at once (FAQ) and leaves the
  holder's item bar at the end of the turn or at the next give; refreshes never bring one back.

## Per card (only decisions; the rest is the sheet as printed)

- 05 Bladed Boomerang: ring area copied from the sheet's coordinates (empty centre 0,0; grey -1,-1), as the other 2e
  classes copy them.
- 06 Impaling Spear bottom: "target 1 enemy within 2 hexes" with no range icon = a ranged attack, `Range: 2` ("within N
  hexes" follows range rules, rulebook p21: user decision B).
- 08 Cleave and Thrust: the hex mark down-right of the grey hex (1,-1) from the image (GHS lacks it).
- 09 Iron Bulwark bottom / 28 Armed to the Teeth top: "Grant ... : Shield" = a round shield on the figures adjacent
  when played (Banner Spear Hold the Line, Shattersong Cloaking Refraction: Range 1, Target 99 / 1, Filter
  [Self, Ally]), not the 1st edition aura that follows you.
- 10 Proficiency top: a persistent +1 melee BuffAttack marked GH2E_QMProficiencyTop; the plugin makes it count only
  from the first lost-icon item used on each of your turns to the turn's end. The 30+ gold recover's random class item
  is offered by the plugin's supplies spend. Icon Positive_AttackPlus_Item.
- 10 Proficiency bottom (GH2E_QMProficiencyBottom): the plugin hands out / swaps in the +1 copies of Barbed Strip and
  Iron Plate; "whenever you rest, gain Barbed Strip or Iron Plate" = a self give on each short or long rest (both rest
  behaviours, as Crashing Tide Cleansing Swell lists them), the player picking which.
- 13 Scroll of Recall top: Pull on one ally (Filter Ally, Snowdancer Cross Winds pulls allies); the heal Filter
  [Self, Ally]. Bottom pull carries the Muddle (Conditions), Filter Enemy.
- 14 Sharpening Kit: heal Filter Ally (allies and summons, not self); bottom = a round +1 Attack on you and on each ally
  adjacent when played (user decision C; not an aura that follows you).
- 17 Quiver of Arrows bottom: the 1st edition aura (BuffTarget, ranged, allies within 1) + XPTracker [0,1,0,1].
- 20 Giant Club bottom: the plugin reads the move's path (CAbilityMove.AllPlayerArrayIndexOnPath, the Quartermaster the
  last mover), asks per ally adjacent to an entered hex and per item (up to four), then +10 supplies per ally given.
- 21 Scroll of Judgment top: the mandatory self damage first, as printed (CanSkip False, standard confirm), then the
  enemies' damage 2. Bottom reaction: round
  DuringActionAbilityOnAttacked (Energy Conversion / Stress Vents behaviour) with one slot carrying the XP ([1]),
  its ability recovers one spent item. TEST: the item pick on a monster's turn; the slot ends the bonus.
- 22 Set Up Shop top: persistent, NOT lost, no slots: stays to the end of the scenario (rules question: 2e may let
  you drop it; Frosthaven has no "remove a persistent card" action). Gain 10 at each turn end = EndTurnAbility with
  an auto Null +10 (marked GH2E_QMSetUpShop). The plugin: an ally's move / teleport ability ending adjacent to you asks
  your player to spend supplies on that ally's items (10+ needed); while the bonus is active you can't perform move
  abilities (CAbility.ImmuneToAbility answers yes for your own Move abilities: the game's own "cannot use move
  abilities" refusal, no condition icon).
- 22 Set Up Shop bottom: "at the end of the round, Heal 2 all allies within 1" = round EndRoundAbility bonus. TEST:
  the round bonus is still there in the end-of-round phase (Frosthaven's own EndRoundAbility cards are persistent).
- 23 Scroll of Lightning top: mandatory damage 1 to all allies and enemies adjacent to the target = inline Damage
  sub-ability, AllTargetsAdjacentToParentTargets, Filter [Enemy, Ally], CanSkip False (Bruiser Let Fly / 1st edition
  Blind Destruction shape).
- 24 Continual Supply bottom: EndTurnAbility gated by bonus Vars `ValidateNoSupplies:
  TriggerActor.GetCharacterResourceAmount("Supplies") == 0` (PhaseManager only triggers an end-turn bonus whose
  Validate vars pass, CActiveBonus.SetupTriggerContext; so slots advance only on 0-supply turns), XPTracker [1,0,1];
  the turn it's played counts (dashed first slot, the sheet's question). UNVERIFIED expression.
- 26 Tools of War bottom: "+10 supplies if the loot took a money token" = PerformVars on the loot with its own
  `LootTokenCount > 0` (money tokens, CAbilityLoot). UNVERIFIED expression.
- 29 Reinforced Steel: top = EndTurnAbility give of one random class item to an ally within 2 (skippable); bottom =
  the 1st edition Reinforced Steel top (EndRoundAbility recover one spent item), exact.
- affinities.txt empty: Earth, Air and Ice are infused once each.

## Perks (perks.py)

All 11 perks, 18 checks. Names made up.
- 1 Muddling Strike: new AMC_GH2E_Quartermaster2_Muddle_P0 (as Frosthaven's AMC_Disarm_P0).
- 2 Restock: new AMC_GH2E_Quartermaster2_Supplies10_P0 (+0, auto Null ability adding 10 supplies). UNVERIFIED: a Null
  ability inside a modifier.
- 4 Spare Armor / 6 Quick Draw: new AMC_GH2E_Quartermaster2_GainArmor_P0 (+0) / AMC_GH2E_Quartermaster2_GainScroll_R
  (rolling): a self auto NullTargeting giving the chosen one of the two items (plugin).
- 8 Well Equipped: IgnoreMinusOneItemEffects + remove one +0.
- 9 Deep Pockets: the plugin adds one small-item slot (CharacterController.GetMaxUnlockedSmallItems and
  GetMaxSmallItems +1 for perk Quartermaster209).
- 10 Guild Connections (2e Well-Connected, no function outside 2e per the GH2e FAQ): the user's equivalent, the party
  buys items 5 gold cheaper while this character is in it (TradingController.GetDiscountedCost -5); reputation dropped.
- 11 Resupply (linked, Cost 2): ExecuteAbilitiesAfterLongRest with an auto Null +10 supplies.
- Masteries: both, answered by the plugin's own records (FrosthavenMods_QMSupplySpends: three 50-supply spends that
  recovered 50+ gold of lost items; FrosthavenMods_QMItemEveryRound: a class item given in every round with a turn
  that wasn't a long rest). The records live in memory: a scenario reloaded mid-way starts them again.

## Shared changes wanted

1. (minor) build.py keeps the 1st edition character file's HeroStats and Complexity (2e mat: Melee 4, Ranged 3,
   Mobility 3, Support 4, Defense 4, Control 2; complexity 5 of 5); classes.json has the 2e role bars.

## Check

`GH2E_SKIP_RULECHECK_BUILD=1 python tools/gh2e/check.py Quartermaster2`: 30 of 30 cards; packcheck 30 cards,
0 problems, 7 warnings (all item 7 above); rulecheck 46 ok, 0 with problems; character resource Supplies written;
no missing text keys; no gh_port warnings. **CHECK PASSED Quartermaster2.**

Bench first (shapes no Frosthaven card uses, or unverified expressions): a spend box (gate at <10, track to 0, XP,
skip keeps supplies), Continual Supply bottom (slot only on 0-supply turns), Giant Club and Tools of War supply gains,
Scroll of Judgment bottom reaction, Set Up Shop (both end-turn bonuses, bottom end-round heal), RefreshItems 2,
Restock perk modifier.
- Perk Restock: modifier rewritten, `AMC_GH2E_Quartermaster2_Supplies10_P0` is now a self NullTargeting (Frosthaven's AMC_Resonance_P1, auto, no animation) with the same ResourcesToAddOnAbilityEnd Supplies 10 (a Null ability on a modifier card stops the game when drawn). Exact; bench the supply gain.

## "!" boxes (mandatory triggers, 2e rulebook p.35)

Drawn with Frosthaven's own `- MandatoryTrigger: Layout:` box (Banner Spear Javelin, Boneshaper Malicious
Conversion for two elements in one box), found on the card images (local/gh2e-data/cards/images/QM2), the
corner XP/lost/persistent boxes aside (Frosthaven draws its own corner). Each boxed effect is mandatory in the
data: half infusions always happen, element pickers and self/ally effects have `CanSkip: False` or are plugin
rules with no prompt.

- 13 Scroll of Recall top: infuse Earth
- 21 Scroll of Judgment top: "Suffer 2."
- 22 Set Up Shop top: "You cannot perform move abilities."
- 23 Scroll of Lightning top: "All allies and enemies adjacent to the target suffer 1."; infuse Air
- 27 Scroll of Blizzards top: infuse Ice
