# Elementalist (2nd edition): translation notes

All 29 cards were written by hand from `tools/gh2e/sheets/Elementalist.json`. There is no Xipe data for this class. Area
strings come from Secretariat, converted to doubled coordinates with y up. Shapes are reused from the 1st edition
Elementalist port and from Frosthaven's own cards (Infuser, Frozen Fist, Geminate, Crashing Tide, Pyroclast, Banner
Spear, Boneshaper). Enhancement marks use the printed shape: `SquareEnhancements` / `CircleEnhancements` where the sheet
differs from the converter's default (Malleable Evocation heal circle, Shaping the Ether attack circle, the [square]
Move 2 bottoms, Infernal Vortex / Arresting Gale / Burial bottom [square] attacks, Arresting Gale Ward circle, Flash of
Brilliance heal square, summons' HP and Move square).

Nothing here has been run in the game. Every shape below that Frosthaven's own cards don't already use is marked
**untested**. Those need a bench run first.

## Class-wide shapes

- **Split / mixed infusions** (Fire|Ice and so on): an Infuse ability with the element picker, written as Infuser
  Diamondization writes it (`Infuse: [A, B]`, `ShowElementPicker`, `ValidateActionHasHappened`). Inside a consume, it
  is a non-sub-ability infuse under the consume's override.
- **Split consumes** (consume Air|Earth, Fire|Ice): two consume entries with the same effect and
  `ConsumeOnlyOneElement: True`. This is Geminate's own shape: the consume bar lets only one flagged entry be selected.
- **Consume X: ..., infuse X** (fixed element): `Infuse: [X]` on the consume entry itself. AbilityConsume parses it and
  CPhaseAction adds it to the action's end-of-turn infusions. Packcheck and rulecheck accept it. **Untested in play**:
  Frosthaven's own cards use a SubInfuse instead.
- **Consume any: infuse any**: Frozen Fist Ice Uppercut's sub-ability infuse with the picker.
- **"Consume X: \<separate ability\>"** (Dual Nature, Gravel Guard, Empowering Flame, Obsidian Shards, Winter's Edge,
  Prismatic Barrage, Primal Duality): Frosthaven's `Consume:` ability type (Boneshaper Rotting Multitude, Crashing Tide
  Mighty Claws). XP goes on the Consume, infusions are Infuse abilities inside it.
- **"Add +1 Move if one or more elements are strong or waning"** (Stoking Hail, Infernal Vortex, Frigid Torrent, Dual
  Nature bottoms): a ConditionalOverride (Pain Conduit Down to the Dirt shape) requiring
  `ElementInfusionBoardManager.GetAvailableElements().Count > 0`. **Untested** expression. The method exists and the
  Infuser's perk 10 uses it.
- **"+1 Target / +1 Heal / +1 Attack (and XP 1, infuse) if you consume two or more elements"** (Malleable Evocation
  top, Shaping the Ether top, Prismatic Barrage bottom, Eye of the Hurricane bottom). APPROXIMATION: an extra consume
  entry with no element whose `Requirements` Vars need
  `PhaseManager.CurrentPhaseAction.ActionAugmentationsToConsume.Where(obj.Elements.Count > 0).Count >= 2`. Blinkblade
  Rushed to the End has a consume entry with no element. CPhaseAction.ActionAugmentationsAvailableForCurrentAbility
  checks consume Requirements. So the player clicks this bonus after toggling two element consumes; it isn't
  automatic. If an element is untoggled afterwards, the bonus stays. **Untested**: it needs a check that the button
  shows and refreshes after each toggle. Fallback: text only.
- **Safeguard** (Malleable Evocation top, Prismatic Barrage bottom, both on the heal target): the data stand-in until
  the plugin has a real Safeguard condition. The consume override adds a sub-ability under the heal
  (`UseParentTargets`). That sub-ability is an `AddActiveBonus` with a Null effect, which puts the bonus on the target
  (CAbilityAddActiveBonus.ApplyToActor -> baseCard.AddActiveBonus(this, actor, caster)). The bonus is
  `GenericOnNegativeCondition`, `PreventCondition: True`, with a one-slot track (`XPTracker: [0]`). It checks
  `TriggerTarget == ActiveBonus.Actor`, the holder, rather than Caster, because an ally can hold it. Its list line is
  `GH2E_SAFEGUARD` with the icon `Positive_DamagePrevent`. The generic event is global (CGenericActiveBonus.FireEvent),
  so the holder check is what limits it. **Untested.**
- **Grant one ally X**: ControlActor on an ally, using JotL Signs of the Void's verified shape (the ally's
  Shield/Retaliate with `Filter: Self`, `UseOriginalActor`, `ActiveBonusDuration: Round`).

## Per card

- 01 Ice Spikes: as printed.
- 02 Malleable Evocation: Strengthen, Ward and Bless are `Conditions` on the heal (Boneshaper Wailing from Beyond
  shape). Safeguard and the two-or-more bonus use the class-wide shapes above. Bottom "move one waning element to the
  strong column" is the Infuser Desperate Throw bottom (`InfuseOnlyWaning: True` over all six elements).
- 03 Formless Power: top is as printed. Bottom: APPROXIMATION using the 1st edition's verified shape (a toggle BuffAttack
  with `Consuming: Any`, `EntireAction`, `Restriction: OncePerAction`). The card says "at the start of each of your
  turns, consume any for +1 Attack on all attacks that turn". In the port you consume when attacking instead, once per
  action. A turn with two attack actions can therefore consume twice, where the card consumes once for both.
- 04 Shaping the Ether: the Air Push 3 is the 1st edition's inline push. Two-or-more bonus as above.
- 05 Stoking Hail, 07 Infernal Vortex, 08 Frigid Torrent, 10 Dual Nature: as printed (the +1 Move is the conditional
  above). Infernal Vortex top is the 1st edition Lava Eruption shape: `Target: -1` within range 4.
- 06 Magma Scales: top is a grant of Move 2 + Jump. Consume Air: Disarm is GRANTED, following Secretariat (the sheet
  lists it as unclear). It is written as a sub-ability of the grant targeting one enemy adjacent to the granted ally
  (`AllTargetsAdjacentToParentTargets` + `TreatAsNonSubAbility`, the Care Package fix shape), plus infuse Air.
  **Untested.** Bottom: grant Shield 2 + Retaliate 2 for the round (Signs of the Void shape).
- 09 Pulsing Cores: top uses the consume-Fire `AreaEffect` override (Geminate Smoldering Hatred overrides an area this
  way) with the triangle "0,0,R|2,0,R|1,1,R". Bottom: APPROXIMATION, **NEEDS PLUGIN** for the exact rule. It is a
  six-slot EndTurnAbility that infuses ANY element with the picker (the 1st edition Shaping the Aether shape), XP on
  slots 2/4/6. The card restricts each turn to Fire|Ice, then Air|Earth, then Light|Dark, repeating; nothing in the
  data can rotate the choice per slot. The card text is left as printed.
- 11 Lingering Essence: **NEEDS PLUGIN**. "Whenever an enemy consumes an element, you may infuse that element" has no
  active-bonus trigger in Frosthaven. Enemy consumes are only kept per monster set in
  ElementInfusionBoardManager.s_EnemyConsumedElements. The card is written as a persistent active bonus that does
  nothing (a Null EndRoundAbility), so it sits in the active area for the rest of the scenario as the 2e rule says (not
  lost, no slots). Plugin hook: on an enemy's consume, offer the bonus holder an infusion of that element.
- 12 Flash of Brilliance: top is RecoverDiscardedCards 1 with `Vars: CardLevel: 1` (Metal Mosaic Memory Drive / Stamina
  Potion), Range 3, `Filter: [Self, Ally]`. **Untested** on an ally target: the recover-select message goes to the
  targeted actor. Bottom is as printed. Its infuse any goes through convert_any_infusion.
- 13 Encompassing Shadow: top is as printed. Bottom: AttackersGainDisadvantage as a round aura (`IsAura`, Range 1,
  `AuraFilter: [Self, Ally]`), which no Frosthaven card does with this ability (Deathwalker uses the aura for its
  summons). **Untested.**
- 14 Gravel Guard: top is a grant of Move 2, Shield 1 (round) and "Muddle, target all, range 1". The Muddle is GRANTED
  (Secretariat's reading; sheet unclear), so it hits enemies adjacent to the ally. Bottom: Consume Fire -> Trap
  (generic `Trap`, Damage 2, range 3), then Damage 1 to all enemies adjacent to the trap, written with Rock Slide's
  fixed shape (`AllTargetsAdjacentToParentTargets` under a create/trap parent). **Untested** for a trap parent.
- 15 Wave of Frost: area "2,3,R|0,3,E|1,2,R|3,2,R|2,1,R|4,1,R|3,0,E", from Secretariat's (0,1)(1,0)(1,1)(1,2)(2,2) with
  enhance hexes (0,0)(1,3), converted from odd-row offset coordinates.
- 16 Arresting Gale: bottom Ward targets you or an ally within range 3. Consume Earth gives +1 Target and infuses Earth.
- 17 Empowering Flame: bottom "Grant one ally within range 2: (Ice) Retaliate 2, range 2, round" is a plain Retaliate on
  an ally (Range 2, RetaliateRange 2, round) inside the Consume Ice ability, not a ControlActor. The effect is the
  same. The Ice consume carries the infuse Ice.
- 18 Burial: area "0,2,R|1,1,R|3,1,R|0,0,R" (Secretariat's (0,0)(0,1)(0,2)(1,1)).
- 19 Chain Lightning: the 1st edition's `ChainAttack` / `ChainAttackRange: 2` / `ChainAttackDamageReduction: 1` (the
  mechanics map still lists the chain targeting UI as "check"). The card's "+2 Range per enemy already attacked" isn't
  in the data. It only matters because each next target must be within range 2 of the previous one, which
  ChainAttackRange already enforces.
- 20 Obsidian Shards: bottom is a Consume Ice+Earth ability. Inside it is a PreventFirstDamage aura (Banner of Valor
  shape: `PreventDamage: 99`, Range 1, `AuraFilter: [Self, Ally]`) with a 2-slot track `[0,1]`, plus the Ice|Earth
  picker. The persistent bonus isn't lost, so the card discards after the second use. The layout is a text row: an
  `- Active:` element can't name an ability nested inside a Consume (rulecheck: "Active ability name not found").
  **Untested.**
- 21 Winter's Edge: bottom "Strengthen, Bless: target 2 allies, range 3" follows Secretariat: Strengthen yourself, Bless
  two allies within range 3 (the sheet lists it as unclear; the target pill follows the Bless icon). The grant Attack 3
  is a ControlActor attack with `StrengthIsBase`.
- 22 Pragmatic Reinforcement: consume Light gives the heal target a level 1 recover (`UseParentTargets`, CardLevel 1)
  plus an Air|Earth picker. Consume Air|Earth is a split consume giving Shield 2 for the round to the heal target (the
  1st edition sub-ability shape).
- 23 Duskstorm: area "1,1,R|3,1,E|0,0,R|2,0,R". Consume Dark gives Invisible to all allies in the red hexes (Banner
  Spear Boldening Blow's `CustomAbility.ActorsOnRedHexes` targeting) plus a Fire|Ice picker. Fire|Ice is a split
  consume.
- 24 Elemental Juggernaut / 28 Ethereal Manifestation: summons, see below.
- 25 Confluence of Elements: three `[Any, Any]` consumes (the 1st edition Simulacrum shape). The third adds two separate
  any-element pickers.
- 26 Prismatic Barrage: top consume Ice is a Stun override. Consume Fire and consume Earth are separate Consume abilities
  (their own Attack 2, range 4). The hazardous tile is `CreateTerrain: HotCoals` (CAbilityCreateTerrain spawns a
  hazardous prop for HotCoals/Thorns) in `ParentAbility.ActorsToTarget.Select(Tile)` with `TileFilter: FeaturelessHex`.
  **Untested** expression. Bottom has Safeguard and the two-or-more bonus (with infuse Light) as above.
- 27 Eye of the Hurricane: consume Ice attacks all enemies adjacent to the heal target (`AllTargetsAdjacentToParentTargets`
  + `TreatAsNonSubAbility`). Consume Air: "all enemies at exactly range 2 from the heal target" uses Custom targeting
  vars (enemies within 2 of the heal target that aren't within 1, via `GameState.GetAllEnemiesInRange`). **Untested**
  expression. If it fails, fall back to Range 2 + `MinRange: 2` from the Elementalist (exact only when you heal
  yourself). A sub-ability attack under a heal parent is the 1st edition card's own shape; the Eye of the Void note in
  CARD_FIXES warns that attack subs under non-attack parents can throw, so bench this card early.
- 29 Primal Duality: top is three optional Consume abilities. Fire+Ice is Damage 5 with Muddle, range 4 (a Damage
  ability, no area). Air+Earth is a grant of Shield 2 + Retaliate 2 for the round. Light+Dark is Attack 2, Target 2,
  range 4, Curse, `AttackHasAdvantage`. Bottom: consume Light recovers any one discarded card (Filter Self) and infuses
  Dark. Consume Dark gives `Teleport2: 4` (Blinkblade Breakneck Speed) and infuses Light. Loot 1 with `[Any, Any]`:
  +1 Loot as a Range +1 override (the 1st edition Gravel Vortex shape).

## Summons

- Chromatic Construct (`ChromaticConstructID`, new): HP 7, Move 3, Attack 0, Range 1 (melee in Frosthaven's data; Bone
  Horde and the 1st edition golems write melee as 1). Model stand-in: **LavaGolem** (an elemental golem from the 1st
  edition pack). "+1 Attack per strong or waning element" is Frosthaven's Bone Horde shape: a hidden persistent
  BuffAttack in OnSummonAbilities with `StatIsBasedOnX` = `ElementInfusionBoardManager.GetAvailableElements().Count`.
  **Untested.** Risk: the summon AI might not attack with a printed Attack 0 (the bonus applies at attack time).
- Elemental Conduit (`ElementalConduitID`, new): HP 3, Move 3, Attack 2, Range 3, Flying. Model stand-in:
  **ManaSphere**, the 1st edition Elementalist's own summon with the same stats. "After each attack, infuse any" is
  the Mana Sphere's own `Attack: Infuse: Any`, exactly as the 1st edition pack ships it. Whether a summon's Any
  infusion offers the picker is the same open question as for the 1st edition Mana Sphere.

## Perks (perks.py)

All 12 perks from the sheet (18 boxes). The four "-1 -> +0 X/Y" and three "+0 -> +1 X/Y" perks are new
`AMC_GH2E_Elementalist2_*` modifiers in Frosthaven's AMC_FireAir_P1 shape (infuse one of two with the picker). Wild
rolling is a new rolling infuse-any modifier.
- "At the start of each scenario, you may infuse any": APPROXIMATION. No perk flag runs an ability at scenario start, so
  it is a one-slot StartTurnAbility applied at scenario start: an infuse-any at the start of your first turn.
- "Whenever you long rest, consume any: infuse any": ExecuteAbilitiesAfterLongRest with a Consume ability, in
  Frosthaven's AMC_ConsumeInfuseWild shape.
- "Fire, Ice, Air and Earth don't wane" (3 linked boxes, `Cost: 3`): APPROXIMATION. Frosthaven's only switch
  (`ElementsDontWane`, Tome of Elements) is all-or-nothing (GameState: strong-to-waning off for every element), so
  Light and Dark don't wane either. **NEEDS PLUGIN** to be exact.
- Masteries: left out (MASTERIES = []). Neither 2e mastery (consume 4+ elements in one turn five times; all six elements
  strong or waning at once in three scenarios) fits an existing battle-goal filter.

## Shared changes wanted

- Plugin: Lingering Essence hook (enemy consumes an element -> the holder may infuse it).
- Plugin: Pulsing Cores per-slot rotation (Fire|Ice, Air|Earth, Light|Dark).
- Plugin: real Safeguard condition (replaces the data stand-in on Malleable Evocation and Prismatic Barrage).
- Plugin: per-element "doesn't wane" for perk 12 (Fire/Ice/Air/Earth only).
- Plugin (optional): make the "two or more elements consumed" bonuses automatic instead of an extra consume button.
- GHMasteries: tags for "consume 4+ elements in one turn (count 5)" and "all six elements strong or waning at once (3
  scenarios)" to add the two masteries.
- Formless Power bottom: a start-of-turn consume that buffs the whole turn (Snowdancer Cold Therapy's StartTurnAbility
  consume could carry a round BuffAttack) would make it exact. Not done because the 1st edition shape is verified.
- Bench these first (untested shapes): Malleable Evocation / Prismatic Barrage (Safeguard on an ally, two-or-more
  button), Eye of the Hurricane top (attacks around the heal target), Prismatic Barrage top (hazard in target hex),
  Obsidian Shards bottom, Gravel Guard bottom, Encompassing Shadow bottom aura, Chromatic Construct's attack bonus.

## Check

`GH2E_SKIP_RULECHECK_BUILD=1 python tools/gh2e/check.py Elementalist2`: **CHECK PASSED Elementalist2** (29 of 29 cards,
packcheck 0 problems, rulecheck 52 ok, no gh_port warnings, no missing text keys).
