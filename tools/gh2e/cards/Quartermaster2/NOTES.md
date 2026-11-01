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
- **Spend supplies** (the dashed box: cards 02, 09, 14, 17, 24, 27 and Set Up Shop top) = one optional ability:
  `RefreshItems: 1`, `SlotStateToRefresh: [Spent, Consumed]` on the printed target(s), `AbilityXP: 1`,
  `StartAbilityRequirements` Vars `ValidateSupplies: Actor.GetCharacterResourceAmount("Supplies") >= 10` (the mat's
  "at least 10"), and `PerformVars: OnAbilityEnded: SpendSupplies: AbilityHasHappened &&
  TargetingActor.RemoveCharacterResource("Supplies", 50, true)` (sets the track to 0; removing more than held deletes
  the resource). Engine facts used: PerformVars "OnAbilityEnded" fires for every ability (CAbility 2070), `&&`
  short-circuits (CExpression And), so nothing is spent when the recover was skipped or cancelled (no target with a
  spent/lost item: CAbilityRefreshItemCards cancels, AbilityHasHappened stays false, no XP either). Frosthaven itself
  calls actor methods from PerformVars (Deepwraith Mantle of Dread, Metal Mosaic Energy Conversion, AMC_Pressure) and
  from requirement Vars (Blinkblade's TargetingActor.HasActiveBonusFlag). **UNVERIFIED in play: bench first.**
  Not used: `ResourcesToTakeFromTargets` on yourself (it takes and gives back to the caster: net zero),
  `ConsumeResources` (only a UI count, never removes anything).
- **APPROXIMATION: the gold budget.** The mat: spend all supplies, then Recover any of the target's items whose combined
  gold cost <= the amount spent. Written: each target recovers **one** spent or lost item, whatever the amount (needs
  10+). NEEDS PLUGIN for the budget (Frosthaven's RefreshItems has no cost filter; its `SpecificItems` var could carry
  a list, not a knapsack).
- Optional box as its own ability: the dashed box is "you may"; a top-level ability gets the standard skip. Sharpening
  Kit's box ("Recover the target's items", attached to the heal) is therefore a separate ability on one adjacent ally,
  not a sub-ability of the heal (a sub-ability couldn't be declined, and spending empties the track); the player picks
  the healed ally.
- Display: the resource shows only as the class's status icon (gh_port sets `Sprite: GHClass_Quartermaster2`);
  the amount on the figure is a plugin matter (GHStatusIcons), as for the Plagueherald's Plague.

### The Quartermaster item supply (9 named class items) = NEEDS PLUGIN, approximated
No engine equivalent, and no item stats exist in any source (sheet, GHS, Xipe: the item cards weren't read). Every
"give X to <target>" / "gain X" / random-draw is written as **Frosthaven's own item refresh** on the same target:
`RefreshItems: N` (N = number of items named), `SlotStateToRefresh: [Spent, Consumed]` - "an extra item use" for the
target, the 1st edition Quartermaster's whole identity (Restock, Side Pouch, Reforge), Frosthaven's AMC_RefreshItem.
Consequence: a give does nothing when the target has no spent or lost item (early in a scenario). Card text stays
as printed. Per card: 01 (self 2 + each adjacent ally 1, for "5 random, at most 3 to you"), 03, 04, 08, 11, 12, 13,
15 (two gives), 16 (2), 18, 19 (two), 20, 22, 23, 25, 26 (2), 28 (2 + 1), 29.
- **XP "to gain XP 1" for giving** is the half's XP (`XP: 1` beside the abilities: 03 bottom, 04, 08 (+ its corner XP
  1 = 2), 15, 18, 26, 28 top): in the real game a give almost always happens; with the refresh stand-in it often
  can't, and the XP would be lost with it.
- RefreshItems with Strength 2 (01, 16, 26, 28): no Frosthaven card uses 2 (they use 1 or All). TEST: the item picker
  offers two picks.

## Per card (only decisions; the rest is the sheet as printed)

- 05 Bladed Boomerang: ring area copied from the sheet's coordinates (empty centre 0,0; grey -1,-1), as the other 2e
  classes copy them.
- 06 Impaling Spear bottom: "target 1 enemy within 2 hexes" with no range icon = `Range: 2` + `TreatAsMelee: True`
  (2e: no range value = melee; reach as a targeting limit). Rules question (sheet "unclear"); my reading.
- 08 Cleave and Thrust: the hex mark down-right of the grey hex (1,-1) from the image (GHS lacks it).
- 09 Iron Bulwark bottom / 28 Armed to the Teeth top: "Grant ... : Shield" = a round shield on the figures adjacent
  when played (Banner Spear Hold the Line, Shattersong Cloaking Refraction: Range 1, Target 99 / 1, Filter
  [Self, Ally]), not the 1st edition aura that follows you.
- 10 Proficiency top: APPROXIMATION. The 1st edition Proficiency's shape (persistent BuffAttack with AttackEffect
  BuffIfAnItemUsedInAction +1) plus `AttackType: Melee`: +1 on melee attacks of an action in which any item was used,
  where 2e says the first lost-icon item each turn gives +1 to every melee attack that turn. Second rule (30+ gold
  recover gives a random class item): text only, NEEDS PLUGIN. Icon Positive_AttackPlus_Item.
- 10 Proficiency bottom: "+1 Retaliate on Barbed Strip, +1 Shield on Iron Plate" text only (NEEDS PLUGIN, no items);
  "whenever you rest, gain Barbed Strip or Iron Plate" = recover one spent/lost item on each short or long rest
  (the 1st edition Refreshment's rest trigger; both rest behaviours listed as Crashing Tide Cleansing Swell does).
- 13 Scroll of Recall top: Pull on one ally (Filter Ally, Snowdancer Cross Winds pulls allies); the heal Filter
  [Self, Ally]. Bottom pull carries the Muddle (Conditions), Filter Enemy.
- 14 Sharpening Kit: heal Filter Ally (allies and summons, not self); bottom = the 1st edition round aura at range 1.
- 17 Quiver of Arrows bottom: the 1st edition aura (BuffTarget, ranged, allies within 1) + XPTracker [0,1,0,1].
- 20 Giant Club bottom: APPROXIMATION / NEEDS PLUGIN. Allies adjacent to you after the move (not "to any hex entered
  during the move") each recover one item; "10 supplies per ally given" = PerformVars `10 * ActorsTargeted.Count` on
  that refresh. UNVERIFIED expression.
- 21 Scroll of Judgment top: the 1st edition shape (enemies first, then the mandatory self damage with AutoTrigger and
  CanSkip False; printed first, but a half's first ability never auto-triggers). Bottom reaction: round
  DuringActionAbilityOnAttacked (Energy Conversion / Stress Vents behaviour) with one slot carrying the XP ([1]),
  its ability recovers one spent item. TEST: the item pick on a monster's turn; the slot ends the bonus.
- 22 Set Up Shop top: persistent, NOT lost, no slots: stays to the end of the scenario (rules question: 2e may let
  you drop it; Frosthaven has no "remove a persistent card" action). Gain 10 at each turn end = EndTurnAbility with
  an auto Null +10. APPROXIMATION / NEEDS PLUGIN: "when an ally ends a move/teleport adjacent to you, you may spend
  supplies to Recover that ally's items" = at the end of each of your turns you may spend supplies to let one adjacent
  ally recover one item (second EndTurnAbility bonus, same gate and spend as the boxes). Frosthaven's ally-move
  triggers (Snowdancer Chilling Impact's aura) run the caster's ability on the other figure's turn: not risked for an
  item pick. **NEEDS PLUGIN: "You cannot perform move abilities" is text only (not enforced).** Self-Immobilize each
  turn was rejected (shows as a negative condition, removable by heals/items).
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
- 29 Reinforced Steel: top = EndTurnAbility refresh of one ally within 2 (APPROXIMATION for the random item); bottom =
  the 1st edition Reinforced Steel top (EndRoundAbility recover one spent item), exact.
- affinities.txt empty: Earth, Air and Ice are infused once each.

## Perks (perks.py)

All 11 perks, 18 checks. Names made up.
- 1 Muddling Strike: new AMC_GH2E_Quartermaster2_Muddle_P0 (as Frosthaven's AMC_Disarm_P0).
- 2 Restock: new AMC_GH2E_Quartermaster2_Supplies10_P0 (+0, auto Null ability adding 10 supplies). UNVERIFIED: a Null
  ability inside a modifier.
- 4 Spare Armor / 6 Quick Draw: APPROXIMATION, "gain the <item>" = Frosthaven's AMC_RefreshItem (+0 recover one spent
  or lost item) / new rolling AMC_GH2E_Quartermaster2_RefreshItem_R.
- 8 Well Equipped: IgnoreMinusOneItemEffects + remove one +0.
- 9 Deep Pockets (extra small item) and 10 Guild Connections (5 gold discount, +2 reputation): **NEEDS PLUGIN**,
  passive with no effect (no Frosthaven perk flag: EPerkFlag has AddOneAdditionalOneHandSlot only).
- 11 Resupply (linked, Cost 2): ExecuteAbilitiesAfterLongRest with an auto Null +10 supplies.
- Masteries: none (both need the supply spend and the item supply). gh_port's "3 masteries" line counts the 3 new
  modifier files (cosmetic, as for the Bruiser).

## Shared changes wanted

1. **Plugin: supplies display** - show the Supplies amount (0-50) on the Quartermaster's status icon / mat
   (GHStatusIcons), as for Plague.
2. **Plugin: the spend budget** - on a supply spend, recover any of the target's spent/lost items with combined gold
   cost <= supplies spent (now: one item per target), and Proficiency's "30+ gold recover gives a random class item".
3. **Plugin: the Quartermaster item supply** - 9 class items (stats from the item cards, still to be read from the
   images), one copy each, slotless, returned when used / at scenario end; "give/gain X" and random draws (Booster
   Pack, Reinforced Steel, Proficiency) would replace the RefreshItems stand-ins; Proficiency bottom's +1 Retaliate /
   +1 Shield on those items. The item card images aren't in the sheet sources yet: they need reading first.
4. **Plugin: Set Up Shop top** - the ally-ends-move-adjacent trigger, and enforcing "cannot perform move abilities".
5. **Plugin: Giant Club bottom** - allies adjacent to any hex entered during the move; up to four chosen items.
6. **Perks 9 and 10** (extra small item; shop discount + reputation) and the two masteries.
7. **packcheck false positive:** `Null: True` abilities warn "no ability kind found (keys: None, ...)" because PyYAML
   reads the key `Null` as YAML null; packcheck's check looks for the string "Null". Proposal: in
   `tools/packcheck.py` check_ability, treat a `None` key as the Null kind (`... "Null") or None in props`). The 7
   warnings here are only that (cards 05, 08, 11, 15, 18, 24, 30: the "gain supplies" Null abilities).
8. (minor) build.py keeps the 1st edition character file's HeroStats and Complexity (2e mat: Melee 4, Ranged 3,
   Mobility 3, Support 4, Defense 4, Control 2; complexity 5 of 5); classes.json has the 2e role bars.

## Check

`GH2E_SKIP_RULECHECK_BUILD=1 python tools/gh2e/check.py Quartermaster2`: 30 of 30 cards; packcheck 30 cards,
0 problems, 7 warnings (all item 7 above); rulecheck 46 ok, 0 with problems; character resource Supplies written;
no missing text keys; no gh_port warnings. **CHECK PASSED Quartermaster2.**

Bench first (shapes no Frosthaven card uses, or unverified expressions): a spend box (gate at <10, track to 0, XP,
skip keeps supplies), Continual Supply bottom (slot only on 0-supply turns), Giant Club and Tools of War supply gains,
Scroll of Judgment bottom reaction, Set Up Shop (both end-turn bonuses, bottom end-round heal), RefreshItems 2,
Restock perk modifier.
