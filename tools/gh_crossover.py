"""Frosthaven's official crossover sheets for Gloomhaven classes: the perks and masteries a Gloomhaven class gets when
played in Frosthaven (Cephalofair's "Gloomhaven classes in Frosthaven" character sheets), as Frosthaven rule files.
Gloomhaven's own perks are not used: Frosthaven's sheets reworked them (fewer "add" perks, more "replace", a long-rest
perk) and added two masteries per class, which Gloomhaven never had. Used by gh_port.py: files(cls) -> [(path, text)],
strings(cls) -> {key: English}.

Perks are plain Frosthaven perk files (attack-modifier cards swapped, Flags for the passive ones; see Global/Perks in the
game's ruleset). The text uses the game's own sprite markup, copied from its perk descriptions.

Masteries are battle-goal conditions over the game's per-round statistics (Campaign/Masteries in the game's ruleset).
The Brute's first (12 retaliate damage in one round) is pure data: the DealDamage filter with AbilityTypes [Retaliate]
summed per round. The second (three consecutive rounds of six different cards and 6+ damage) has no built-in filter
for "in a row", so the plugin answers that condition itself (GHMasteries.cs): the file tags the target with a marker in
EnemyFamilies, a free-text list the game never validates, and the plugin's prefix computes the streak from the damage
and ability records. Without the plugin the condition reads as "play three rounds".
"""

import re

BRUTE_PERKS = [
    # (available, name, description, {perk fields})  -- Perk_Brute_01 .. _10, 15 checks in all, as on the sheet
    (2, "Precision", 'Replace one <sprite name="-1"> with one <sprite name="+1">',
     {"CardsToRemove": "[AMC_Basic_M1]", "CardsToAdd": "[AMC_Basic_P1]"}),
    (2, "Shove", 'Replace one <sprite name="-1"> with three <sprite name="0"> <sprite name=Push> 1 <sprite name=Rolling>',
     {"CardsToRemove": "[AMC_Basic_M1]", "CardsToAdd": "[AMC_Push1_R, AMC_Push1_R, AMC_Push1_R]"}),
    (1, "Skewering Blows", 'Replace two <sprite name="0"> with two <sprite name="0"> <sprite name=Pierce> 3 <sprite name=Rolling>',
     {"CardsToRemove": "[AMC_Basic_P0, AMC_Basic_P0]", "CardsToAdd": "[AMC_Pierce3_R, AMC_Pierce3_R]"}),
    (2, "Heavy Hands", 'Replace one <sprite name="0"> with two <sprite name="+1">',
     {"CardsToRemove": "[AMC_Basic_P0]", "CardsToAdd": "[AMC_Basic_P1, AMC_Basic_P1]"}),
    (2, "Wide Swing", 'Replace one <sprite name="0"> with one <sprite name="0"> "+1 <sprite name=Target>" <sprite name=Rolling>',
     {"CardsToRemove": "[AMC_Basic_P0]", "CardsToAdd": "[AMC_AddTarget_R]"}),
    (2, "Stunning Blow", 'Add one <sprite name="0"> <sprite name=Stun> <sprite name=Rolling>',
     {"CardsToAdd": "[AMC_Stun_R]"}),
    (1, "Dirty Tricks", 'Add one <sprite name="0"> <sprite name=Disarm> <sprite name=Rolling> and one <sprite name="0"> <sprite name=Muddle> <sprite name=Rolling>',
     {"CardsToAdd": "[AMC_Disarm_R, AMC_Muddle_R]"}),
    (1, "Overpower", 'Add one <sprite name="+3">',
     {"CardsToAdd": "[AMC_P3]"}),
    (1, "Hardened", 'Ignore item <sprite name="-1"> effects and add one <sprite name="+1"> <sprite name=Shield> 1',
     {"CardsToAdd": "[AMC_Shield1_P1]", "Flags": "[Passive, IgnoreMinusOneItemEffects]"}),
    (1, "Rested and Ready", 'Whenever you long rest, add +1 <sprite name=Move> to your first move ability the following round',
     {"Flags": "[Passive, ExecuteAbilitiesAfterLongRest]", "Abilities": """\
    TRested:
        Move: 1
        MiscVars:
            PrintedProperties: list("Move")
        Filter: Self
        SkipAnim: True
        ActiveBonus:
            Duration: Persistent
            Behaviour: BuffMove
            XPTracker: [0]
            ListLayoutOverride: [$PERK_Brute_10_Bonus$, Positive_MovePlus]"""}),
]

BRUTE_MASTERIES = [
    ("Living Wall",
     "Cause enemies to suffer a total of at least <sprite name=Damage> 12 from retaliate during attacks targeting you in a single round",
     """\
# the retaliate damage the Brute dealt, summed per round (DealDamage with AbilityTypes keeps only retaliate records)
BattleGoalCondition:
    - Targets:
        Filter: DealDamage
        SubFilter: RoundCharactersOnly
        AbilityTypes: [Retaliate]
        CharacterIDs:
            ActorID: BruteID
        Value: 12"""),
    ("Relentless Onslaught",
     "Across three consecutive rounds, play six different ability cards and cause enemies to suffer at least <sprite name=Damage> 6 on each of your turns",
     """\
# Three rounds in a row with at least 6 damage to enemies on each of the Brute's turns and six different ability cards
# played over the three. No built-in filter checks rounds in a row: the Frosthaven Mods plugin answers this target
# (the marker in EnemyFamilies, GHMasteries.cs) from the damage and ability records. Value = rounds in a row,
# Amount = damage per turn, Targets = different cards. Without the plugin it reads as "play three rounds".
BattleGoalCondition:
    - Targets:
        Filter: CountXRounds
        SubFilter: Scenario
        EnemyFamilies: [FrosthavenMods_DamageStreak]
        CharacterIDs:
            ActorID: BruteID
        Value: 3
        Amount: 6
        Targets: 6"""),
]

# ---------------------------------------------------------------- group 1 (crossover sheets read 2026-10-25)
# Texts in the digital game's own perk wording ("Replace one <sprite name="-1"> with one <sprite name="+1">", "Once
# each quest"), with its sprite names (Poison, Muddle, Pierce, Invisible, CreateLight, Shield, Stun, Heal, Wound,
# Immobilize, Refresh, Target, Ward, RecoverIcon, Rolling). A perk drawn with a tall single box costs two checks.


def _once_per_quest(desc_key, icon, inner):
    """A "once each quest, during your turn" perk, Frosthaven's own pattern (Perk_BannerSpear_10, the Deepwraith's Skull
    Collection): a persistent toggle bonus added at scenario start; its AddActiveBonus content is the ability performed
    when the player toggles it during their turn; XPTracker [0] gives it a single use. The toggle's button in the
    "activate" bar takes its picture from the perk icon list by perk ID, which has no entry for a ported perk (it drew
    a white box), so the bonus names an active-ability icon itself (MiscVars OverrideIcon, as Skull Collection does)."""
    return f"""\
    TActive:
        AutoTrigger: True
        AddActiveBonus:
{inner}
            Filter: [Self]
        ActiveBonus:
            Duration: Persistent
            Behaviour: DuringTurnAbility
            XPTracker: [0]
            IsToggleBonus: True
            Hidden: True
            ListLayoutOverride: [${desc_key}$, {icon}]
            MiscVars:
                OverrideIcon: "'{icon}'"
        AnimOverload: PowerUp"""


SCOUNDREL_PERKS = [
    (1, "Light Step", 'Remove two <sprite name="-1">',
     {"CardsToRemove": "[AMC_Basic_M1, AMC_Basic_M1]"}),
    (1, "Steady Hand", 'Replace one <sprite name="-1"> with one <sprite name="+1">',
     {"CardsToRemove": "[AMC_Basic_M1]", "CardsToAdd": "[AMC_Basic_P1]"}),
    (2, "Venom Coating", 'Replace one <sprite name="-1"> with two <sprite name="0"> <sprite name=Poison> <sprite name=Rolling>',
     {"CardsToRemove": "[AMC_Basic_M1]", "CardsToAdd": "[AMC_Poison_R, AMC_Poison_R]"}),
    (1, "Composure", 'Replace one <sprite name="-2"> with one <sprite name="0">',
     {"CardsToRemove": "[AMC_Basic_M2]", "CardsToAdd": "[AMC_Basic_P0]"}),
    (2, "Vital Strike", 'Replace one <sprite name="0"> with one <sprite name="+2">',
     {"CardsToRemove": "[AMC_Basic_P0]", "CardsToAdd": "[AMC_Basic_P2]"}),
    (2, "Dirty Fighting", 'Replace two <sprite name="0"> with one <sprite name="0"> <sprite name=Muddle> <sprite name=Rolling> and one <sprite name="0"> <sprite name=Pierce> 3 <sprite name=Rolling>',
     {"CardsToRemove": "[AMC_Basic_P0, AMC_Basic_P0]", "CardsToAdd": "[AMC_Muddle_R, AMC_Pierce3_R]"}),
    (2, "Flurry", 'Add two <sprite name="+1"> <sprite name=Rolling>',
     {"CardsToAdd": "[AMC_P1_R, AMC_P1_R]"}),
    (1, "Vanish", 'Add one <sprite name="0"> <sprite name=Invisible>, self <sprite name=Rolling>',
     {"CardsToAdd": "[AMC_Invisible_R]"}),
    (1, "Unfazed", "Ignore negative scenario effects",
     {"Flags": "[Passive, IgnoreNegativeScenarioEffects]"}),
    (1, "Cloak of Invisibility", 'Once each quest, during your turn, perform <sprite name=Invisible>, self',
     {"Cost": 2, "Flags": "[Passive, ApplyActiveBonusesAtScenarioStart]",
      "Abilities": _once_per_quest("PERK_Scoundrel_10_Desc", "Invisible", "            Invisible: True")}),
]

SCOUNDREL_MASTERIES = [
    ("Pack Hunter", "Kill at least six enemies that are adjacent to at least one of your allies",
     """\
# Kills of enemies that had another character next to them when they fell. No built-in filter looks at who stood next
# to the victim: the Frosthaven Mods plugin answers this target (the marker in EnemyFamilies, GHMasteries.cs) from the
# kill records, which carry the number of players adjacent to the dying enemy. Value = kills needed.
BattleGoalCondition:
    - Targets:
        Filter: CountXRounds
        SubFilter: Scenario
        EnemyFamilies: [FrosthavenMods_KillsNearAllies]
        CharacterIDs:
            ActorID: ScoundrelID
        Value: 6"""),
    ("Lone Wolf", "Kill at least six enemies that are adjacent to none of your allies",
     """\
# The opposite count, from the same records (GHMasteries.cs). Value = kills needed.
BattleGoalCondition:
    - Targets:
        Filter: CountXRounds
        SubFilter: Scenario
        EnemyFamilies: [FrosthavenMods_KillsAwayFromAllies]
        CharacterIDs:
            ActorID: ScoundrelID
        Value: 6"""),
]

SUNKEEPER_PERKS = [
    (1, "Focus", 'Remove four <sprite name="0">',
     {"CardsToRemove": "[AMC_Basic_P0, AMC_Basic_P0, AMC_Basic_P0, AMC_Basic_P0]"}),
    (2, "Radiance", 'Replace one <sprite name="-1"> with two <sprite name="0"> <sprite name=CreateLight> <sprite name=Rolling>',
     {"CardsToRemove": "[AMC_Basic_M1]", "CardsToAdd": "[AMC_Light_R, AMC_Light_R]"}),
    (2, "Bulwark", 'Replace one <sprite name="-1"> with one <sprite name="0"> <sprite name=Shield> 1 <sprite name=Rolling>',
     {"CardsToRemove": "[AMC_Basic_M1]", "CardsToAdd": "[AMC_Shield1_R]"}),
    (1, "Smite", 'Replace one <sprite name="0"> with one <sprite name="+2">',
     {"CardsToRemove": "[AMC_Basic_P0]", "CardsToAdd": "[AMC_Basic_P2]"}),
    (2, "Zeal", 'Add two <sprite name="+1"> <sprite name=Rolling>',
     {"CardsToAdd": "[AMC_P1_R, AMC_P1_R]"}),
    (1, "Dazzle", 'Add one <sprite name="0"> <sprite name=Stun> <sprite name=Rolling>',
     {"CardsToAdd": "[AMC_Stun_R]"}),
    (2, "Mending Light", 'Add two <sprite name="0"> <sprite name=Heal> 1, self <sprite name=Rolling>',
     {"CardsToAdd": "[AMC_Heal1_R, AMC_Heal1_R]"}),
    (1, "Well Armored", 'Ignore item <sprite name="-1"> effects and add two <sprite name="+1">',
     {"CardsToAdd": "[AMC_Basic_P1, AMC_Basic_P1]", "Flags": "[Passive, IgnoreMinusOneItemEffects]"}),
    (1, "Unfazed", "Ignore negative scenario effects",
     {"Flags": "[Passive, IgnoreNegativeScenarioEffects]"}),
    # answered by the plugin (GHPerks.cs: the perk ID Sunkeeper10 on the healer)
    (1, "Shielding Light", "Whenever one of your heals would raise an ally's hit points above their maximum, that ally gains <sprite name=Ward>",
     {"Cost": 2, "Flags": "[Passive]"}),
]

SUNKEEPER_MASTERIES = [
    ("Unbreakable", "Reduce attacks targeting you by a total of <sprite name=Shield> 20 or more in a single round",
     """\
# The game's own filter: damage received that shields stopped, summed over a round (Amount = the total).
BattleGoalCondition:
    - Targets:
        Filter: ShieldDamage
        SubFilter: RoundCharactersOnly
        CharacterIDs:
            ActorID: SunkeeperID
        Amount: 20
        Value: 1"""),
    ("Child of the Sun", "Infuse or consume <sprite name=CreateLight> during each of your turns",
     """\
# Light infused or consumed on every turn the Sunkeeper takes. The game's filters count infusions or consumptions, not
# either: the Frosthaven Mods plugin answers this target (GHMasteries.cs) from the element records, for the element(s)
# in Elements, over the rounds with a turn of hers.
CheckAtEndOfScenario: True
BattleGoalCondition:
    - Targets:
        Filter: CountXRounds
        SubFilter: Scenario
        EnemyFamilies: [FrosthavenMods_ElementEveryTurn]
        Elements: Light
        CharacterIDs:
            ActorID: SunkeeperID
        Value: 1"""),
]

SAWBONES_PERKS = [
    (1, "Steady Hands", 'Remove one <sprite name="-2">',
     {"CardsToRemove": "[AMC_Basic_M2]"}),
    (3, "Open Wounds", 'Replace one <sprite name="-1"> with one <sprite name="0"> <sprite name=Wound> <sprite name=Rolling>',
     {"CardsToRemove": "[AMC_Basic_M1]", "CardsToAdd": "[AMC_Wound_R]"}),
    (2, "Precise Cuts", 'Replace one <sprite name="0"> with one <sprite name="+2">',
     {"CardsToRemove": "[AMC_Basic_P0]", "CardsToAdd": "[AMC_Basic_P2]"}),
    (2, "Hamstring", 'Replace one <sprite name="0"> with one <sprite name="+1"> <sprite name=Immobilize>',
     {"CardsToRemove": "[AMC_Basic_P0]", "CardsToAdd": "[AMC_Immobilize_P1]"}),
    (1, "Resupply", 'Add one <sprite name="0"> <sprite name=Refresh> one item',
     {"CardsToAdd": "[AMC_RefreshItem]"}),
    (2, "Surgical Strike", 'Add one <sprite name="+2"> <sprite name=Rolling>',
     {"CardsToAdd": "[AMC_P2_R]"}),
    (1, "Anesthetic", 'Add one <sprite name="0"> <sprite name=Stun> <sprite name=Rolling>',
     {"CardsToAdd": "[AMC_Stun_R]"}),
    (2, "Self-Care", 'Add one <sprite name="0"> <sprite name=Heal> 3, self <sprite name=Rolling>',
     {"CardsToAdd": "[AMC_Heal3_R]"}),
    # answered by the plugin (GHPerks.cs: the perk ID Sawbones09 on a Sawbones in play)
    (1, "Revitalizing Medicine", "Whenever an ally performs the <sprite name=Heal> of Medical Pack or Large Medical Pack, that character first loses one negative condition",
     {"Flags": "[Passive]"}),
]

SAWBONES_MASTERIES = [
    ("Bedside Manner", "On each of your turns, give an ally a card, heal an ally, grant an ally <sprite name=Shield>, or place a card in an ally's active area",
     """\
# An ally helped on every turn the Sawbones takes: a heal, shield, supply card, buff or recovery ability of hers with
# another character or summon among its targets. No built-in filter combines them: the Frosthaven Mods plugin answers
# this target (GHMasteries.cs) from the ability records.
CheckAtEndOfScenario: True
BattleGoalCondition:
    - Targets:
        Filter: CountXRounds
        SubFilter: Scenario
        EnemyFamilies: [FrosthavenMods_AllySupportEveryTurn]
        CharacterIDs:
            ActorID: SawbonesID
        Value: 1"""),
    ("Grievous Harm", "Deal at least <sprite name=Damage> 20 with a single attack ability",
     """\
# The damage one attack ability of hers caused to enemies, all its targets together. The plugin answers this target
# (GHMasteries.cs) from the damage records, grouped by round, card and ability. Amount = the damage.
BattleGoalCondition:
    - Targets:
        Filter: CountXRounds
        SubFilter: Scenario
        EnemyFamilies: [FrosthavenMods_SingleAbilityDamage]
        CharacterIDs:
            ActorID: SawbonesID
        Amount: 20
        Value: 1"""),
]

QUARTERMASTER_PERKS = [
    (2, "Steady Supply", 'Remove two <sprite name="-1">',
     {"CardsToRemove": "[AMC_Basic_M1, AMC_Basic_M1]"}),
    (1, "Armor Piercing", 'Replace two <sprite name="0"> with two <sprite name="0"> <sprite name=Pierce> 3 <sprite name=Rolling>',
     {"CardsToRemove": "[AMC_Basic_P0, AMC_Basic_P0]", "CardsToAdd": "[AMC_Pierce3_R, AMC_Pierce3_R]"}),
    (1, "Sweeping Blow", 'Replace one <sprite name="0"> with one <sprite name="0"> +1 <sprite name=Target>',
     {"CardsToRemove": "[AMC_Basic_P0]", "CardsToAdd": "[AMC_AddTarget]"}),
    (2, "Heavy Hands", 'Replace one <sprite name="0"> with one <sprite name="+2">',
     {"CardsToRemove": "[AMC_Basic_P0]", "CardsToAdd": "[AMC_Basic_P2]"}),
    (3, "Resupply", 'Add one <sprite name="0"> <sprite name=Refresh> one item',
     {"CardsToAdd": "[AMC_RefreshItem]"}),
    (2, "Drilled", 'Add two <sprite name="+1"> <sprite name=Rolling>',
     {"CardsToAdd": "[AMC_P1_R, AMC_P1_R]"}),
    (1, "Concussion", 'Add one <sprite name="0"> <sprite name=Stun> <sprite name=Rolling>',
     {"CardsToAdd": "[AMC_Stun_R]"}),
    (1, "Well Fitted", 'Ignore item <sprite name="-1"> effects and add two <sprite name="+1">',
     {"CardsToAdd": "[AMC_Basic_P1, AMC_Basic_P1]", "Flags": "[Passive, IgnoreMinusOneItemEffects]"}),
    # the sheet also asks for a persistent Quartermaster card in the active area; not enforced here (noted in the guide)
    (1, "Well Supplied", 'Once each quest, during your turn, <sprite name=RecoverIcon> up to four cards from your discard pile',
     {"Cost": 2, "Flags": "[Passive, ApplyActiveBonusesAtScenarioStart]",
      "Abilities": _once_per_quest("PERK_Quartermaster_09_Desc", "Recover", "            RecoverDiscardedCards: 4")}),
]

QUARTERMASTER_MASTERIES = [
    ("Supply Lines", "Spend, lose or refresh one or more items on each of your turns, without ever performing the top action of Reinforced Steel",
     """\
# An item used or refreshed on every turn the Quartermaster takes (the plugin answers this target from the item and
# ability records, GHMasteries.cs); failed the moment Reinforced Steel's top action (card 20223, abilities named T...)
# is performed (the plugin's second marker; Amount = the card).
CheckAtEndOfScenario: True
BattleGoalCondition:
    - Targets:
        Filter: CountXRounds
        SubFilter: Scenario
        EnemyFamilies: [FrosthavenMods_ItemsEveryTurn]
        CharacterIDs:
            ActorID: QuartermasterID
        Value: 1
BattleGoalFailCondition:
    - Targets:
        Filter: CountXRounds
        SubFilter: Scenario
        EnemyFamilies: [FrosthavenMods_UsedCardTop]
        CharacterIDs:
            ActorID: QuartermasterID
        Amount: 20223
        Value: 1"""),
    ("Pack Mule", "Loot six or more loot tokens in a single turn",
     """\
# The game's own filter: loot records in one round.
BattleGoalCondition:
    - Targets:
        Filter: Loot
        SubFilter: RoundCharactersOnly
        CharacterIDs:
            ActorID: QuartermasterID
        Value: 6"""),
]

# ---------------------------------------------------------------- Group 2 (typed 2026-10-27 from the crossover sheets
# in scratchpad/crossover/{cragheart,plagueherald,nightshroud,berserker}.jpg; names of the perks and masteries are ours,
# the sheets have none). Icon readings to confirm on the sheets when the classes are ported: the Cragheart's second perk
# (two +0 "push 2" rolling), the jagged diamond read as Curse (Plagueherald, Nightshroud), the "?" diamond as Muddle.
# Masteries tagged FrosthavenMods_* that GHMasteries.cs doesn't answer yet are marked NEW TAG: the plugin learns them
# with the class.

def _room_revealed(inner):
    """A perk acting on every enemy of a room the moment it is revealed: Frosthaven's own pattern (Perk_Snowdancer_09,
    muddle on the revealed room), its TargetingVars expression copied as is."""
    return f"""\
    ARoom:
{inner}
        AutoTrigger: True
        SkipAnim: True
        Target: -1
        Targeting: Custom
        TargetingVars:
            AddValidActorsInRange: TriggerMap?.Monsters?.Select(obj?.Actor)?.Where(obj != null) || AllEnemyMonsters"""


CRAGHEART_PERKS = [
    (3, "Steadfast", 'Replace one <sprite name="-1"> with one <sprite name="+1">',
     {"CardsToRemove": "[AMC_Basic_M1]", "CardsToAdd": "[AMC_Basic_P1]"}),
    (1, "Rockslide", 'Replace one <sprite name="0"> with two <sprite name="0"> <sprite name=Push> 2 <sprite name=Rolling>',
     {"CardsToRemove": "[AMC_Basic_P0]", "CardsToAdd": "[AMC_Push2_R, AMC_Push2_R]"}),
    (2, "Grasping Earth", 'Replace one <sprite name="0"> with one <sprite name="+1"> <sprite name=Immobilize>',
     {"CardsToRemove": "[AMC_Basic_P0]", "CardsToAdd": "[AMC_Immobilize_P1]"}),
    (2, "Crushing Blow", 'Replace one <sprite name="0"> with one <sprite name="+2">',
     {"CardsToRemove": "[AMC_Basic_P0]", "CardsToAdd": "[AMC_Basic_P2]"}),
    (2, "Dust Storm", 'Add one <sprite name="+2"> <sprite name=Muddle> and one <sprite name="0"> <sprite name=CreateAir> <sprite name=Rolling>',
     {"CardsToAdd": "[AMC_Muddle_P2, AMC_Air_R]"}),
    (1, "Earthen Might", 'Add four <sprite name="0"> <sprite name=CreateEarth> <sprite name=Rolling>',
     {"CardsToAdd": "[AMC_Earth_R, AMC_Earth_R, AMC_Earth_R, AMC_Earth_R]"}),
    (1, "Thick Skin", 'Ignore item <sprite name="-1"> effects',
     {"Flags": "[Passive, IgnoreMinusOneItemEffects]"}),
    (1, "Unfazed", "Ignore negative scenario effects",
     {"Flags": "[Passive, IgnoreNegativeScenarioEffects]"}),
    # the revealed room's enemies are moved 1 as a controlled move (a controlled move always ends in an empty hex)
    (1, "Earthquakes", "Whenever a new room is revealed, control all enemies in the newly revealed room: <sprite name=Move> 1, this movement must end in an empty hex",
     {"Cost": 2, "Flags": "[Passive, ExecuteAbilitiesAfterRoomRevealed, ApplyActiveBonusesAtScenarioStart]",
      "Abilities": _room_revealed("""\
        ControlActor:
            ControlActorType: Player
            Abilities:
                AQuake:
                    Move: 1
                    StrengthIsBase: True
        Filter: Enemy""")}),
]

CRAGHEART_MASTERIES = [
    ("Cornered", "Only attack enemies adjacent to obstacles or walls",
     """\
# NEW TAG: every attack's targets stood next to an obstacle or a wall (GHMasteries.cs, from the attack records and the map)
CheckAtEndOfScenario: True
BattleGoalCondition:
    - Targets:
        Filter: CountXRounds
        SubFilter: Scenario
        EnemyFamilies: [FrosthavenMods_AttacksNearObstaclesOnly]
        CharacterIDs:
            ActorID: CragheartID
        Value: 1"""),
    ("Shaped by Stone", "Damage or heal at least one ally each round",
     """\
# NEW TAG: each round, an ally took damage from the Cragheart or was healed by them (GHMasteries.cs)
CheckAtEndOfScenario: True
BattleGoalCondition:
    - Targets:
        Filter: CountXRounds
        SubFilter: Scenario
        EnemyFamilies: [FrosthavenMods_AllyDamageOrHealEveryRound]
        CharacterIDs:
            ActorID: CragheartID
        Value: 1"""),
]

PLAGUEHERALD_PERKS = [
    (1, "Steady Focus", 'Remove one <sprite name="-2">',
     {"CardsToRemove": "[AMC_Basic_M2]"}),
    (3, "Virulence", 'Replace one <sprite name="-1"> with one <sprite name="+1">',
     {"CardsToRemove": "[AMC_Basic_M1]", "CardsToAdd": "[AMC_Basic_P1]"}),
    (2, "Lethal Strain", 'Replace one <sprite name="0"> with one <sprite name="+2">',
     {"CardsToRemove": "[AMC_Basic_P0]", "CardsToAdd": "[AMC_Basic_P2]"}),
    (1, "Airborne", 'Add three <sprite name="+1"> <sprite name=CreateAir>',
     {"CardsToAdd": "[AMC_Air_P1, AMC_Air_P1, AMC_Air_P1]"}),
    (1, "Toxic Touch", 'Add three <sprite name="0"> <sprite name=Poison> <sprite name=Rolling>',
     {"CardsToAdd": "[AMC_Poison_R, AMC_Poison_R, AMC_Poison_R]"}),
    (1, "Paralysis", 'Add two <sprite name="0"> <sprite name=Immobilize> <sprite name=Rolling>',
     {"CardsToAdd": "[AMC_Immobilize_R, AMC_Immobilize_R]"}),
    (2, "Numbing Agent", 'Add one <sprite name="0"> <sprite name=Stun> <sprite name=Rolling>',
     {"CardsToAdd": "[AMC_Stun_R]"}),
    (1, "Hex", 'Add two <sprite name="0"> <sprite name=Curse> <sprite name=Rolling>',
     {"CardsToAdd": "[AMC_Curse_R, AMC_Curse_R]"}),
    (1, "Unfazed", 'Ignore negative scenario effects and add one <sprite name="+1">',
     {"CardsToAdd": "[AMC_Basic_P1]", "Flags": "[Passive, IgnoreNegativeScenarioEffects]"}),
    (1, "Xorn's Boon", "Once each quest, during your turn, each enemy that has <sprite name=Poison> suffers <sprite name=Damage> 1 and gains <sprite name=Muddle>, and each ally who has <sprite name=Poison> suffers <sprite name=Damage> 1 and gains <sprite name=Strengthen>",
     {"Cost": 2, "Flags": "[Passive, ApplyActiveBonusesAtScenarioStart]",
      "Abilities": _once_per_quest("PERK_Plagueherald_10_Desc", "Poison", """\
            Damage: 1
            Target: -1
            Targeting: All
            Filter:
                TargetType: Enemy
                TargetHasNegativeCondition: Poison
            SubAbilities:
                TMuddle:
                    Muddle: True
                    UseParentTargets: True
                TAllies:
                    Damage: 1
                    Target: -1
                    Targeting: All
                    Filter:
                        TargetType: Ally
                        TargetHasNegativeCondition: Poison
                    SubAbilities:
                        TStrengthen:
                            Strengthen: True
                            UseParentTargets: True""")}),
]

PLAGUEHERALD_MASTERIES = [
    ("Silent Killer", "Kill at least five enemies with non-attack abilities",
     """\
# NEW TAG: kills whose killing ability was not an attack (GHMasteries.cs, from the kill records). Value = kills needed.
BattleGoalCondition:
    - Targets:
        Filter: CountXRounds
        SubFilter: Scenario
        EnemyFamilies: [FrosthavenMods_KillsByNonAttack]
        CharacterIDs:
            ActorID: PlagueheraldID
        Value: 5"""),
    ("Pandemic", "Perform three different attack abilities that target at least four enemies each",
     """\
# NEW TAG: distinct attack abilities (by card and ability name) that targeted four or more enemies (GHMasteries.cs).
BattleGoalCondition:
    - Targets:
        Filter: CountXRounds
        SubFilter: Scenario
        EnemyFamilies: [FrosthavenMods_AttacksWithFourTargets]
        CharacterIDs:
            ActorID: PlagueheraldID
        Value: 3"""),
]

NIGHTSHROUD_PERKS = [
    (2, "Light Step", 'Remove two <sprite name="-1">',
     {"CardsToRemove": "[AMC_Basic_M1, AMC_Basic_M1]"}),
    (1, "Shadow Step", 'Replace one <sprite name="-2"> with one <sprite name="-1"> <sprite name=CreateDark>',
     {"CardsToRemove": "[AMC_Basic_M2]", "CardsToAdd": "[AMC_Dark_M1]"}),
    (1, "Wide Reach", 'Replace one <sprite name="0"> with one <sprite name="0"> +1 <sprite name=Target> <sprite name=Rolling>',
     {"CardsToRemove": "[AMC_Basic_P0]", "CardsToAdd": "[AMC_AddTarget_R]"}),
    (2, "Confusion", 'Replace two <sprite name="0"> with three <sprite name="0"> <sprite name=Muddle> <sprite name=Rolling>',
     {"CardsToRemove": "[AMC_Basic_P0, AMC_Basic_P0]", "CardsToAdd": "[AMC_Muddle_R, AMC_Muddle_R, AMC_Muddle_R]"}),
    (2, "Fade", 'Replace one <sprite name="+1"> with one <sprite name="+1"> <sprite name=Invisible>',
     {"CardsToRemove": "[AMC_Basic_P1]", "CardsToAdd": "[AMC_Invisible_P1]"}),
    (2, "Nightfall", 'Add one <sprite name="+1"> <sprite name=CreateDark>',
     {"CardsToAdd": "[AMC_Dark_P1]"}),
    (1, "Hex", 'Add two <sprite name="0"> <sprite name=Curse> <sprite name=Rolling>',
     {"CardsToAdd": "[AMC_Curse_R, AMC_Curse_R]"}),
    (1, "Dark Vigor", 'Add two <sprite name="0"> <sprite name=Heal> 1, self <sprite name=Rolling>',
     {"CardsToAdd": "[AMC_Heal1_R, AMC_Heal1_R]"}),
    (1, "Unfazed", 'Ignore negative scenario effects and add two <sprite name="+1">',
     {"CardsToAdd": "[AMC_Basic_P1, AMC_Basic_P1]", "Flags": "[Passive, IgnoreNegativeScenarioEffects]"}),
    # Frosthaven's own pieces (2026-10-27): a once-per-scenario toggle (as the Scoundrel's), DiscardCards for the cost,
    # then SelectAndPlayCard from the pool (the OwnedUnused pile: the class's cards not taken into the hand) limited to
    # actions with an active bonus, as Hail of Spears limits its pick to summon actions. APPROXIMATION: used during the
    # Nightshroud's turn rather than at scenario start, and any active-bonus action qualifies, not only persistent ones.
    (1, "Empowering Night", "Once each scenario, during your turn, you may discard two cards to add a card with an action containing a persistent symbol from your pool to your hand and immediately play it, performing that action",
     {"Cost": 2, "Flags": "[Passive, ApplyActiveBonusesAtScenarioStart]",
      "Abilities": _once_per_quest("PERK_Nightshroud_10_Desc", "Recover", """\
            DiscardCards: 2
            SubAbilities:
                EPlay:
                    AutoTrigger: True
                    Filter: Self
                    SelectAndPlayCard:
                        CardPile: OwnedUnused
                        Amount: 1
                        MinRequiredCards: 1
                    MiscVars:
                        IsActionValid: Action.Abilities.Any(obj.AbilityType == CAbility.EAbilityType.AddActiveBonus)""")}),
]

NIGHTSHROUD_MASTERIES = [
    ("Unseen", "Have <sprite name=Invisible> at the start or end of each of your turns",
     """\
# NEW TAG: invisible at the start or the end of every turn (GHMasteries.cs, from the turn records)
CheckAtEndOfScenario: True
BattleGoalCondition:
    - Targets:
        Filter: CountXRounds
        SubFilter: Scenario
        EnemyFamilies: [FrosthavenMods_InvisibleEveryTurn]
        CharacterIDs:
            ActorID: NightshroudID
        Value: 1"""),
    ("Creature of the Night", "Infuse or consume <sprite name=CreateDark> during each of your turns",
     """\
CheckAtEndOfScenario: True
BattleGoalCondition:
    - Targets:
        Filter: CountXRounds
        SubFilter: Scenario
        EnemyFamilies: [FrosthavenMods_ElementEveryTurn]
        Elements: Dark
        CharacterIDs:
            ActorID: NightshroudID
        Value: 1"""),
]

# ---------------------------------------------------------------- group 3: Jaws of the Lion (crossover sheets read 2026-10-27)
# The four JotL classes' Frosthaven sheets, against the digital game's own JotL perk texts (LangUpdate.csv). Icon
# readings checked against the game's sprites (fh_conditions contact sheet) and Gloomhaven Secretariat's transcription
# of the sheets (data/fh-crossover/label/en.json). The special perks (Hasty Pick-up, Brilliant Aegis, Grave Defense,
# Remodeling) are text-only passives until the plugin answers them when each class is ported.

# attack-modifier cards Frosthaven has no file for: written into the pack beside the perks (files())
NEW_CARDS = {
    "AMC_Stun_P1": 'MathModifier: "+1"\nAbilityOverrides:\n    NegCon:\n        ParentName: AMC_Stun_P1\n        Conditions: [Stun]\n',
    "AMC_Air_P2": 'MathModifier: "+2"\nAbilities:\n    InfuseAbility:\n        Infuse: [Air]\n',
    "AMC_Light_P2": 'MathModifier: "+2"\nAbilities:\n    InfuseAbility:\n        Infuse: [Light]\n',
    "AMC_Earth_P2": 'MathModifier: "+2"\nAbilities:\n    InfuseAbility:\n        Infuse: [Earth]\n',
    "AMC_Ice_P1": 'MathModifier: "+1"\nAbilities:\n    InfuseAbility:\n        Infuse: [Ice]\n',
    "AMC_FireLight_P1": 'MathModifier: "+1"\nAbilities:\n    InfuseAbility:\n        Infuse: [Fire, Light]\n        ShowElementPicker: True\n',
    "AMC_Heal1_1Ally_P1": 'MathModifier: "+1"\nControlTargetingActor: True\nAbilities:\n    AHeal:\n        Heal: 1\n        Filter: Ally\n        Targeting: Range\n        Range: 99\n        AnimOverload: Heal\n',
}

HATCHET_PERKS = [
    (2, "Steady Hands", 'Remove two <sprite name="-1">',
     {"CardsToRemove": "[AMC_Basic_M1, AMC_Basic_M1]"}),
    (1, "Stunning Blow", 'Replace one <sprite name="0"> with one <sprite name="0"> <sprite name=Stun>',
     {"CardsToRemove": "[AMC_Basic_P0]", "CardsToAdd": "[AMC_Stun_P0]"}),
    (1, "Stunning Strike", 'Replace one <sprite name="+1"> with one <sprite name="+1"> <sprite name=Stun>',
     {"CardsToRemove": "[AMC_Basic_P1]", "CardsToAdd": "[AMC_Stun_P1]"}),
    (1, "Jagged Edge", 'Replace one <sprite name="0"> with one <sprite name="+1"> <sprite name=Wound>',
     {"CardsToRemove": "[AMC_Basic_P0]", "CardsToAdd": "[AMC_Wound_P1]"}),
    (1, "Tainted Edge", 'Replace one <sprite name="0"> with one <sprite name="+1"> <sprite name=Poison>',
     {"CardsToRemove": "[AMC_Basic_P0]", "CardsToAdd": "[AMC_Poison_P1]"}),
    (1, "Pinning Throw", 'Replace one <sprite name="0"> with one <sprite name="+1"> <sprite name=Immobilize>',
     {"CardsToRemove": "[AMC_Basic_P0]", "CardsToAdd": "[AMC_Immobilize_P1]"}),
    (1, "Dizzying Blow", 'Replace one <sprite name="0"> with one <sprite name="+2"> <sprite name=Muddle>',
     {"CardsToRemove": "[AMC_Basic_P0]", "CardsToAdd": "[AMC_Muddle_P2]"}),
    (3, "Heavy Throw", 'Replace one <sprite name="+1"> with one <sprite name="+3">',
     {"CardsToRemove": "[AMC_Basic_P1]", "CardsToAdd": "[AMC_Basic_P3]"}),
    (3, "Whirling Axe", 'Add one <sprite name="+2"> <sprite name=CreateAir>',
     {"CardsToAdd": "[AMC_Air_P2]"}),
    # Frosthaven's once-per-scenario toggle (as the Scoundrel's and Quartermaster's): consume Air, then the same
    # resource recovery Executioner's Axe performs (RecoverResources on the enemy holding either Favorite). Verify on the bench.
    (1, "Hasty Pick-up", "Once each scenario, during your turn, if the Favorite is in a hex on the map, you may <sprite name=ConsumeAir> to return it to its ability card",
     {"Flags": "[Passive, ApplyActiveBonusesAtScenarioStart]",
      "Abilities": _once_per_quest("PERK_Hatchet_10_Desc", "Recover", """\
            Consume:
                Elements: [Air]
                Abilities:
                    CRecover:
                        RecoverResources: True
                        Targeting: All
                        Target: -1
                        ResourcesToTakeFromTargets:
                            - ["TheFavorite", 1]
                            - ["TheNewFavorite", 1]
                        FilterContainer:
                            Filter1:
                                TargetType: Enemy
                                TargetHasCharacterResource: ["TheFavorite"]
                            Filter2:
                                TargetType: Enemy
                                TargetHasCharacterResource: ["TheNewFavorite"]
                        AnimOverload: PowerUp""")}),
]

HATCHET_MASTERIES = [
    ("Always Armed", "Throw or recover the Favorite during each of your turns",
     """\
# NEW TAG: the Favorite (the Hatchet's resource token: ResourcesToAddOnAbilityEnd puts it on the target,
# ResourcesToTakeFromTargets takes it back) left or returned on every turn (GHMasteries.cs)
CheckAtEndOfScenario: True
BattleGoalCondition:
    - Targets:
        Filter: CountXRounds
        SubFilter: Scenario
        EnemyFamilies: [FrosthavenMods_FavoriteEveryTurn]
        CharacterIDs:
            ActorID: HatchetID
        Value: 1"""),
    ("Keep It Moving", "During each round in which there is at least one enemy on the map at the start of your turn, throw the Favorite at an enemy or return it to your ability card",
     """\
# NEW TAG: the same change of place, counted per round and only in rounds that start with an enemy on the map
CheckAtEndOfScenario: True
BattleGoalCondition:
    - Targets:
        Filter: CountXRounds
        SubFilter: Scenario
        EnemyFamilies: [FrosthavenMods_FavoriteEveryRoundWithEnemies]
        CharacterIDs:
            ActorID: HatchetID
        Value: 1"""),
]

REDGUARD_PERKS = [
    (2, "Steady Guard", 'Remove two <sprite name="-1">',
     {"CardsToRemove": "[AMC_Basic_M1, AMC_Basic_M1]"}),
    (1, "Unshaken", 'Remove one <sprite name="-2">',
     {"CardsToRemove": "[AMC_Basic_M2]"}),
    (1, "Barbed Sickle", 'Replace one <sprite name="0"> with one <sprite name="+1"> <sprite name=Wound>',
     {"CardsToRemove": "[AMC_Basic_P0]", "CardsToAdd": "[AMC_Wound_P1]"}),
    (1, "Chain Hold", 'Replace one <sprite name="0"> with one <sprite name="+1"> <sprite name=Immobilize>',
     {"CardsToRemove": "[AMC_Basic_P0]", "CardsToAdd": "[AMC_Immobilize_P1]"}),
    (1, "Shield Wall", 'Replace one <sprite name="0"> and one <sprite name="+1"> with two <sprite name="+1"> <sprite name=Shield> 1, self',
     {"CardsToRemove": "[AMC_Basic_P0, AMC_Basic_P1]", "CardsToAdd": "[AMC_Shield1_P1, AMC_Shield1_P1]"}),
    (2, "Burning Blade", 'Replace one <sprite name="+1"> with one <sprite name="+2"> <sprite name=CreateFire>',
     {"CardsToRemove": "[AMC_Basic_P1]", "CardsToAdd": "[AMC_Fire_P2]"}),
    (2, "Radiant Blade", 'Replace one <sprite name="+1"> with one <sprite name="+2"> <sprite name=CreateLight>',
     {"CardsToRemove": "[AMC_Basic_P1]", "CardsToAdd": "[AMC_Light_P2]"}),
    (2, "Kindled Light", 'Add one <sprite name="+1"> <sprite name=CreateFire>/<sprite name=CreateLight>',
     {"CardsToAdd": "[AMC_FireLight_P1]"}),
    (1, "Heavy Armor", 'Ignore negative item effects and add two <sprite name="+1">',
     {"CardsToAdd": "[AMC_Basic_P1, AMC_Basic_P1]", "Flags": "[Passive, IgnoreMinusOneItemEffects]"}),
    # APPROXIMATION (the game has no "when attacked, you may" prompt for perks): a toggle the Red Guard may switch on
    # during her turn, consuming Light, for Shield 1 and attackers' disadvantage until the round ends (Frosthaven's own
    # AttackersGainDisadvantage bonus, the Deathwalker's). Verify on the bench; a plugin prompt later if the user wants the sheet's timing.
    (1, "Brilliant Aegis", "During your turn, you may <sprite name=ConsumeLight> to gain <sprite name=Shield> 1 and have attackers gain disadvantage against you until the end of the round",
     {"Flags": "[Passive, ApplyActiveBonusesAtScenarioStart]", "Abilities": """\
    TActive:
        AutoTrigger: True
        AddActiveBonus:
            Consume:
                Elements: [Light]
                Abilities:
                    CShield:
                        Shield: 1
                        Filter: Self
                        ActiveBonusDuration: Round
                        AnimOverload: PowerUp
                    CDisadvantage:
                        AttackersGainDisadvantage: 1
                        Range: 0
                        Target: -1
                        Filter: Self
                        ActiveBonus:
                            Duration: Round
                            Hidden: True
            Filter: [Self]
        ActiveBonus:
            Duration: Persistent
            Behaviour: DuringTurnAbility
            IsToggleBonus: True
            Hidden: True
            ListLayoutOverride: [$PERK_RedGuard_10_Desc$, Positive_Shield_Disadvantage]
            MiscVars:
                OverrideIcon: "'Positive_Shield_Disadvantage'"
        AnimOverload: PowerUp"""}),
]

REDGUARD_MASTERIES = [
    ("Patient Guardian", "Kill at least five enemies during their turns",
     """\
# NEW TAG: kills the Red Guard caused on an enemy's turn (retaliate, wound, a trap it was pulled onto...) (GHMasteries.cs)
BattleGoalCondition:
    - Targets:
        Filter: CountXRounds
        SubFilter: Scenario
        EnemyFamilies: [FrosthavenMods_KillsDuringEnemyTurns]
        CharacterIDs:
            ActorID: RedGuardID
        Value: 5"""),
    ("Sickle and Chain", "Force each enemy in the scenario to move at least one hex, forcing at least six enemies to move",
     """\
# NEW TAG: enemies pushed or pulled by the Red Guard at least once; Value = enemies, and every enemy that was in the
# scenario must be among them (GHMasteries.cs)
CheckAtEndOfScenario: True
BattleGoalCondition:
    - Targets:
        Filter: CountXRounds
        SubFilter: Scenario
        EnemyFamilies: [FrosthavenMods_EnemiesForcedToMove]
        CharacterIDs:
            ActorID: RedGuardID
        Value: 6"""),
]

VOIDWARDEN_PERKS = [
    (1, "Steady Will", 'Remove two <sprite name="-1">',
     {"CardsToRemove": "[AMC_Basic_M1, AMC_Basic_M1]"}),
    (1, "Unwavering", 'Remove one <sprite name="-2">',
     {"CardsToRemove": "[AMC_Basic_M2]"}),
    (2, "Mending Touch", 'Replace one <sprite name="-1"> with one <sprite name="0"> <sprite name=Heal> 1, ally',
     {"CardsToRemove": "[AMC_Basic_M1]", "CardsToAdd": "[AMC_Heal1_1Ally]"}),
    (3, "Mending Hands", 'Replace one <sprite name="0"> with one <sprite name="+1"> <sprite name=Heal> 1, ally',
     {"CardsToRemove": "[AMC_Basic_P0]", "CardsToAdd": "[AMC_Heal1_1Ally_P1]"}),
    (1, "Blight", 'Replace one <sprite name="0"> with one <sprite name="+1"> <sprite name=Poison>',
     {"CardsToRemove": "[AMC_Basic_P0]", "CardsToAdd": "[AMC_Poison_P1]"}),
    (2, "Hex", 'Replace one <sprite name="0"> with one <sprite name="+1"> <sprite name=Curse>',
     {"CardsToRemove": "[AMC_Basic_P0]", "CardsToAdd": "[AMC_Curse_P1]"}),
    (1, "Cold Void", 'Add two <sprite name="+1"> <sprite name=CreateIce>',
     {"CardsToAdd": "[AMC_Ice_P1, AMC_Ice_P1]"}),
    (1, "Dark Void", 'Add two <sprite name="+1"> <sprite name=CreateDark>',
     {"CardsToAdd": "[AMC_Dark_P1, AMC_Dark_P1]"}),
    (1, "Focused Will", 'Add one <sprite name="+3">',
     {"CardsToAdd": "[AMC_Basic_P3]"}),
    (1, "Unfazed", "Ignore negative scenario effects",
     {"Flags": "[Passive, IgnoreNegativeScenarioEffects]"}),
    # Frosthaven's after-rest perk hooks (both rests), a consume of Ice or Dark (the element picker) and a Ward on one
    # poisoned ally; the ability can be skipped. Verify on the bench (the picker on a consume, the condition filter).
    (1, "Grave Defense", "Whenever you rest, you may <sprite name=ConsumeIce> or <sprite name=ConsumeDark> to give <sprite name=Ward> to one ally who has <sprite name=Poison>",
     {"Flags": "[Passive, ExecuteAbilitiesAfterShortRest, ExecuteAbilitiesAfterLongRest]", "Abilities": """\
    BConsume:
        Consume:
            Elements: [Ice, Dark]
            ShowElementPicker: True
            Abilities:
                CWard:
                    Ward: True
                    Filter:
                        TargetType: Ally
                        TargetHasNegativeConditions: [Poison]
                    Range: 99
                    Target: 1
                    Targeting: Range
                    AnimOverload: PowerUp
        CanSkip: True
        CanUndo: False"""}),
]

VOIDWARDEN_MASTERIES = [
    ("Puppet Master", "Cause enemies to suffer <sprite name=Damage> 20 or more in a single turn with granted or commanded attacks",
     """\
# NEW TAG: damage to enemies in one of the Voidwarden's turns by attacks she granted or commanded (ControlActor /
# granted ability records), summed per turn (GHMasteries.cs)
BattleGoalCondition:
    - Targets:
        Filter: CountXRounds
        SubFilter: Scenario
        EnemyFamilies: [FrosthavenMods_CommandedDamageInTurn]
        CharacterIDs:
            ActorID: VoidwardenID
        Value: 20"""),
    ("Touched by the Void", "Give at least one ally or enemy <sprite name=Poison>, <sprite name=Strengthen>, <sprite name=Bless>, or <sprite name=Ward> each round",
     """\
# NEW TAG: one of the named conditions applied by the Voidwarden to anyone in every round she acts (the condition
# names ride along in the marker list) (GHMasteries.cs)
CheckAtEndOfScenario: True
BattleGoalCondition:
    - Targets:
        Filter: CountXRounds
        SubFilter: Scenario
        EnemyFamilies: [FrosthavenMods_ConditionGivenEveryRound, Poison, Strengthen, Bless, Ward]
        CharacterIDs:
            ActorID: VoidwardenID
        Value: 1"""),
]

DEMOLITIONIST_PERKS = [
    (2, "Steady Fuse", 'Remove two <sprite name="-1">',
     {"CardsToRemove": "[AMC_Basic_M1, AMC_Basic_M1]"}),
    (1, "Trimmed Deck", 'Remove four <sprite name="0">',
     {"CardsToRemove": "[AMC_Basic_P0, AMC_Basic_P0, AMC_Basic_P0, AMC_Basic_P0]"}),
    (1, "Toxic Fumes", 'Replace one <sprite name="-2"> with one <sprite name="0"> <sprite name=Poison>',
     {"CardsToRemove": "[AMC_Basic_M2]", "CardsToAdd": "[AMC_Poison_P0]"}),
    (2, "Concussion", 'Replace one <sprite name="0"> with one <sprite name="+2"> <sprite name=Muddle>',
     {"CardsToRemove": "[AMC_Basic_P0]", "CardsToAdd": "[AMC_Muddle_P2]"}),
    (1, "Bigger Blast", 'Replace one <sprite name="+1"> with two <sprite name="+2">',
     {"CardsToRemove": "[AMC_Basic_P1]", "CardsToAdd": "[AMC_Basic_P2, AMC_Basic_P2]"}),
    (2, "Incendiary", 'Replace one <sprite name="+1"> with one <sprite name="+2"> <sprite name=CreateFire>',
     {"CardsToRemove": "[AMC_Basic_P1]", "CardsToAdd": "[AMC_Fire_P2]"}),
    (2, "Seismic", 'Replace one <sprite name="+1"> with one <sprite name="+2"> <sprite name=CreateEarth>',
     {"CardsToRemove": "[AMC_Basic_P1]", "CardsToAdd": "[AMC_Earth_P2]"}),
    (1, "Shrapnel", 'Add two <sprite name="0"> all adjacent enemies suffer <sprite name=Damage> 1',
     {"CardsToAdd": "[AMC_Damage1Range1_P0, AMC_Damage1Range1_P0]"}),
    (1, "Hardened", 'Ignore negative scenario effects and remove one <sprite name="-1">',
     {"CardsToRemove": "[AMC_Basic_M1]", "Flags": "[Passive, IgnoreNegativeScenarioEffects]"}),
    # Frosthaven's after-rest perk hooks and the Cragheart's obstacle creation (Create RockSingle, as Avalanche). The
    # sheet's condition "while adjacent to a wall or obstacle" has no requirement key in the engine (the known ones:
    # OccupyingObstacleHex, DoesntHaveNegativeCondition, MoveWasStraightLine...): APPROXIMATION, placed after any rest;
    # a plugin check later if the user wants it exact. Verify on the bench.
    (1, "Remodeling", "Whenever you rest, you may place an obstacle in an empty hex within <sprite name=Range> 2 (of you)",
     {"Flags": "[Passive, ExecuteAbilitiesAfterShortRest, ExecuteAbilitiesAfterLongRest]", "Abilities": """\
    BCreate:
        Create: RockSingle
        Range: 2
        Target: 1
        CanSkip: True
        CanUndo: False
        SpawnDelay: 2.9
        AnimOverload: PowerUp"""}),
]

DEMOLITIONIST_MASTERIES = [
    ("Heavy Hitter", "Deal <sprite name=Damage> 10 or more with each of three different attack actions",
     """\
# NEW TAG: attack actions (a card half) whose total damage reached Amount, counted by distinct card half; Value =
# different actions needed (GHMasteries.cs)
BattleGoalCondition:
    - Targets:
        Filter: CountXRounds
        SubFilter: Scenario
        EnemyFamilies: [FrosthavenMods_AttackActionsWithDamage]
        CharacterIDs:
            ActorID: DemolitionistID
        Value: 3
        Amount: 10"""),
    ("Clean Sweep", "Destroy at least six obstacles. End the scenario with no obstacles on the map other than ones placed by allies",
     """\
# NEW TAG: obstacles the Demolitionist destroyed (Value), and at the end no obstacle left that an ally did not place
# (GHMasteries.cs)
CheckAtEndOfScenario: True
BattleGoalCondition:
    - Targets:
        Filter: CountXRounds
        SubFilter: Scenario
        EnemyFamilies: [FrosthavenMods_ObstaclesDestroyed]
        CharacterIDs:
            ActorID: DemolitionistID
        Value: 6"""),
]

# ---------------------------------------------------------------- group 4 (sheets read 2026-10-27, ahead of the port)
# Spellweaver and Mindthief typed from the sheets (C:\Claude\crossover-sheets) and checked against Gloomhaven
# Secretariat's transcription; their special perks that need the plugin (Etheric Bond, Lying Low) are text-only passives.

SPELLWEAVER_PERKS = [
    (1, "Composure", 'Remove one <sprite name="-2">',
     {"CardsToRemove": "[AMC_Basic_M2]"}),
    (2, "Focus", 'Replace one <sprite name="-1"> with one <sprite name="+1">',
     {"CardsToRemove": "[AMC_Basic_M1]", "CardsToAdd": "[AMC_Basic_P1]"}),
    (1, "Winds and Stone", 'Replace one <sprite name="-1"> with one <sprite name="0"> <sprite name=CreateAir> <sprite name=Rolling> and one <sprite name="0"> <sprite name=CreateEarth> <sprite name=Rolling>',
     {"CardsToRemove": "[AMC_Basic_M1]", "CardsToAdd": "[AMC_Air_R, AMC_Earth_R]"}),
    (1, "Light and Shadow", 'Replace one <sprite name="-1"> with one <sprite name="0"> <sprite name=CreateLight> <sprite name=Rolling> and one <sprite name="0"> <sprite name=CreateDark> <sprite name=Rolling>',
     {"CardsToRemove": "[AMC_Basic_M1]", "CardsToAdd": "[AMC_Light_R, AMC_Dark_R]"}),
    (1, "Searing Touch", 'Replace one <sprite name="0"> with one <sprite name="+1"> <sprite name=Wound>',
     {"CardsToRemove": "[AMC_Basic_P0]", "CardsToAdd": "[AMC_Wound_P1]"}),
    (1, "Binding Frost", 'Replace one <sprite name="0"> with one <sprite name="+1"> <sprite name=Immobilize>',
     {"CardsToRemove": "[AMC_Basic_P0]", "CardsToAdd": "[AMC_Immobilize_P1]"}),
    (1, "Thunderclap", 'Replace one <sprite name="0"> with one <sprite name="+1"> <sprite name=Stun>',
     {"CardsToRemove": "[AMC_Basic_P0]", "CardsToAdd": "[AMC_Stun_P1]"}),
    (1, "Hex", 'Replace one <sprite name="0"> with one <sprite name="+1"> <sprite name=Curse>',
     {"CardsToRemove": "[AMC_Basic_P0]", "CardsToAdd": "[AMC_Curse_P1]"}),
    (2, "Inferno", 'Add one <sprite name="+2"> <sprite name=CreateFire>',
     {"CardsToAdd": "[AMC_Fire_P2]"}),
    (2, "Blizzard", 'Add one <sprite name="+2"> <sprite name=CreateIce>',
     {"CardsToAdd": "[AMC_Ice_P2]"}),
    (1, "Unfazed", "Ignore negative scenario effects",
     {"Flags": "[Passive, IgnoreNegativeScenarioEffects]"}),
    # answered by the plugin when the class is ported (on a short rest, Reviving Ether comes back from the discard first)
    (1, "Etheric Bond", "Whenever you short rest, if Reviving Ether is in your discard pile, first return it to your hand",
     {"Flags": "[Passive]"}),
]

SPELLWEAVER_MASTERIES = [
    ("Elemental Mastery", "Infuse and consume all six elements",
     """\
# NEW TAG: every element infused at least once and consumed at least once over the scenario (GHMasteries.cs, from the
# infusion and consumption records)
CheckAtEndOfScenario: True
BattleGoalCondition:
    - Targets:
        Filter: CountXRounds
        SubFilter: Scenario
        EnemyFamilies: [FrosthavenMods_AllElementsInfusedAndConsumed]
        CharacterIDs:
            ActorID: SpellweaverID
        Value: 1"""),
    ("Deep Reserves", "Perform four different loss actions twice each",
     """\
# NEW TAG: Value different card halves with a lost icon each performed Amount times (GHMasteries.cs, from the ability
# records' discard type)
CheckAtEndOfScenario: True
BattleGoalCondition:
    - Targets:
        Filter: CountXRounds
        SubFilter: Scenario
        EnemyFamilies: [FrosthavenMods_LossActionsRepeated]
        CharacterIDs:
            ActorID: SpellweaverID
        Value: 4
        Amount: 2"""),
]

MINDTHIEF_PERKS = [
    (1, "Nimble", 'Remove two <sprite name="-1">',
     {"CardsToRemove": "[AMC_Basic_M1, AMC_Basic_M1]"}),
    (2, "Hobbling Bite", 'Replace one <sprite name="-1"> with one <sprite name="0"> <sprite name=Immobilize> <sprite name=Rolling>',
     {"CardsToRemove": "[AMC_Basic_M1]", "CardsToAdd": "[AMC_Immobilize_R]"}),
    (1, "Steadied", 'Replace one <sprite name="-2"> with one <sprite name="0">',
     {"CardsToRemove": "[AMC_Basic_M2]", "CardsToAdd": "[AMC_Basic_P0]"}),
    (1, "Mind Pull", 'Replace two <sprite name="0"> with three <sprite name="0"> <sprite name=Pull> 1 <sprite name=Rolling>',
     {"CardsToRemove": "[AMC_Basic_P0, AMC_Basic_P0]", "CardsToAdd": "[AMC_Pull1_R, AMC_Pull1_R, AMC_Pull1_R]"}),
    (1, "Confusion", 'Replace two <sprite name="0"> with three <sprite name="0"> <sprite name=Muddle> <sprite name=Rolling>',
     {"CardsToRemove": "[AMC_Basic_P0, AMC_Basic_P0]", "CardsToAdd": "[AMC_Muddle_R, AMC_Muddle_R, AMC_Muddle_R]"}),
    (1, "Sharpened Claws", 'Replace two <sprite name="+1"> with two <sprite name="+2">',
     {"CardsToRemove": "[AMC_Basic_P1, AMC_Basic_P1]", "CardsToAdd": "[AMC_Basic_P2, AMC_Basic_P2]"}),
    (2, "Cold Snap", 'Add one <sprite name="+2"> <sprite name=CreateIce>',
     {"CardsToAdd": "[AMC_Ice_P2]"}),
    (2, "Frenzy", 'Add two <sprite name="+1"> <sprite name=Rolling>',
     {"CardsToAdd": "[AMC_P1_R, AMC_P1_R]"}),
    (1, "Stunning Blow", 'Add one <sprite name="0"> <sprite name=Stun> <sprite name=Rolling>',
     {"CardsToAdd": "[AMC_Stun_R]"}),
    (1, "Disarming Trick", 'Add one <sprite name="0"> <sprite name=Disarm> <sprite name=Rolling> and one <sprite name="0"> <sprite name=Muddle> <sprite name=Rolling>',
     {"CardsToAdd": "[AMC_Disarm_R, AMC_Muddle_R]"}),
    (1, "Unfazed", "Ignore negative scenario effects",
     {"Flags": "[Passive, IgnoreNegativeScenarioEffects]"}),
    # answered by the plugin when the class is ported (monster focus treats her as last in initiative order)
    (1, "Lying Low", "You are considered to be last in initiative order when determining monster focus",
     {"Flags": "[Passive]"}),
]

MINDTHIEF_MASTERIES = [
    ("Thought Thief", "Trigger the on-attack effect of four different Augments thrice each",
     """\
# NEW TAG: Value different augments whose on-attack abilities fired Amount times (GHMasteries.cs; the augment
# processing in CAbilityAttack is hooked for it)
CheckAtEndOfScenario: True
BattleGoalCondition:
    - Targets:
        Filter: CountXRounds
        SubFilter: Scenario
        EnemyFamilies: [FrosthavenMods_AugmentsTriggered]
        CharacterIDs:
            ActorID: MindthiefID
        Value: 4
        Amount: 3"""),
    ("Untouchable", "Never be targeted by an attack",
     """\
# NEW TAG: no attack ever targeted the character (GHMasteries.cs: the attack-target records; a miss counts as targeted)
CheckAtEndOfScenario: True
BattleGoalCondition:
    - Targets:
        Filter: CountXRounds
        SubFilter: Scenario
        EnemyFamilies: [FrosthavenMods_NeverAttacked]
        CharacterIDs:
            ActorID: MindthiefID
        Value: 1"""),
]

ELEMENTALIST_PERKS = [
    (2, "Steady Hands", 'Remove two <sprite name="-1">',
     {"CardsToRemove": "[AMC_Basic_M1, AMC_Basic_M1]"}),
    (1, "Composure", 'Remove one <sprite name="-2">',
     {"CardsToRemove": "[AMC_Basic_M2]"}),
    (1, "Trimmed Deck", 'Remove four <sprite name="0">',
     {"CardsToRemove": "[AMC_Basic_P0, AMC_Basic_P0, AMC_Basic_P0, AMC_Basic_P0]"}),
    (1, "Wide Reach", 'Replace one <sprite name="-1"> with one <sprite name="0"> +1 <sprite name=Target>',
     {"CardsToRemove": "[AMC_Basic_M1]", "CardsToAdd": "[AMC_AddTarget]"}),
    (1, "Thunderclap", 'Replace one <sprite name="0"> with one <sprite name="0"> <sprite name=Stun>',
     {"CardsToRemove": "[AMC_Basic_P0]", "CardsToAdd": "[AMC_Stun_P0]"}),
    (1, "Searing Touch", 'Replace one <sprite name="0"> with one <sprite name="+1"> <sprite name=Wound>',
     {"CardsToRemove": "[AMC_Basic_P0]", "CardsToAdd": "[AMC_Wound_P1]"}),
    (1, "Focused Power", 'Replace two <sprite name="+1"> with two <sprite name="+2">',
     {"CardsToRemove": "[AMC_Basic_P1, AMC_Basic_P1]", "CardsToAdd": "[AMC_Basic_P2, AMC_Basic_P2]"}),
    (1, "Fire Affinity", 'Add four <sprite name="0"> <sprite name=CreateFire>',
     {"CardsToAdd": "[AMC_Fire, AMC_Fire, AMC_Fire, AMC_Fire]"}),
    (1, "Ice Affinity", 'Add four <sprite name="0"> <sprite name=CreateIce>',
     {"CardsToAdd": "[AMC_Ice, AMC_Ice, AMC_Ice, AMC_Ice]"}),
    (1, "Air Affinity", 'Add four <sprite name="0"> <sprite name=CreateAir>',
     {"CardsToAdd": "[AMC_Air, AMC_Air, AMC_Air, AMC_Air]"}),
    (1, "Earth Affinity", 'Add four <sprite name="0"> <sprite name=CreateEarth>',
     {"CardsToAdd": "[AMC_Earth, AMC_Earth, AMC_Earth, AMC_Earth]"}),
    (1, "Unfazed", "Ignore negative scenario effects",
     {"Flags": "[Passive, IgnoreNegativeScenarioEffects]"}),
    # the long-rest half is the game's own perk hook (an infusion of any element, the picker); the scenario-start half
    # needs the plugin when the class is ported (no perk flag runs abilities at scenario start)
    (1, "Elemental Proficiency", "At the start of each scenario and whenever you long rest, infuse any one element",
     {"Flags": "[Passive, ExecuteAbilitiesAfterLongRest]", "Abilities": """\
    BInfuse:
        Infuse: [Any]
        Filter: Self
        AutoTrigger: True
        SkipAnim: True
        CanUndo: False"""}),
]

ELEMENTALIST_MASTERIES = [
    ("Elemental Focus", "Consume at least two different elements with each of four different attack abilities, without ever performing the bottom action of Formless Power or Shaping the Ether",
     """\
# NEW TAGS: Value different attack abilities each performed with two different elements consumed (GHMasteries.cs, the
# consumption records of the attack's action); failed by the bottom of Formless Power (20449) or Shaping the Ether (20450)
CheckAtEndOfScenario: True
BattleGoalCondition:
    - Targets:
        Filter: CountXRounds
        SubFilter: Scenario
        EnemyFamilies: [FrosthavenMods_AttacksConsumingTwoElements]
        CharacterIDs:
            ActorID: ElementalistID
        Value: 4
BattleGoalFailCondition:
    - Targets:
        Filter: CountXRounds
        SubFilter: Scenario
        EnemyFamilies: [FrosthavenMods_UsedCardBottom]
        CharacterIDs:
            ActorID: ElementalistID
        Amount: 20449
        Value: 1
    - Targets:
        Filter: CountXRounds
        SubFilter: Scenario
        EnemyFamilies: [FrosthavenMods_UsedCardBottom]
        CharacterIDs:
            ActorID: ElementalistID
        Amount: 20450
        Value: 1"""),
    ("Elemental Surge", "Infuse five or more elements during one of your turns, then consume five or more elements during your following turn",
     """\
# NEW TAG: a turn with Amount infusions followed by the character's next turn with Amount consumptions (GHMasteries.cs)
CheckAtEndOfScenario: True
BattleGoalCondition:
    - Targets:
        Filter: CountXRounds
        SubFilter: Scenario
        EnemyFamilies: [FrosthavenMods_InfuseThenConsume]
        CharacterIDs:
            ActorID: ElementalistID
        Value: 1
        Amount: 5"""),
]

SOOTHSINGER_PERKS = [
    (2, "Steady Voice", 'Remove two <sprite name="-1">',
     {"CardsToRemove": "[AMC_Basic_M1, AMC_Basic_M1]"}),
    (1, "Composure", 'Remove one <sprite name="-2">',
     {"CardsToRemove": "[AMC_Basic_M2]"}),
    (1, "Stunning Note", 'Replace one <sprite name="-1"> with one <sprite name="0"> <sprite name=Stun>',
     {"CardsToRemove": "[AMC_Basic_M1]", "CardsToAdd": "[AMC_Stun_P0]"}),
    (1, "Binding Chord", 'Replace one <sprite name="0"> with one <sprite name="+1"> <sprite name=Immobilize>',
     {"CardsToRemove": "[AMC_Basic_P0]", "CardsToAdd": "[AMC_Immobilize_P1]"}),
    (1, "Disarming Tune", 'Replace one <sprite name="0"> with one <sprite name="+1"> <sprite name=Disarm>',
     {"CardsToRemove": "[AMC_Basic_P0]", "CardsToAdd": "[AMC_Disarm_P1]"}),
    (1, "Cutting Verse", 'Replace one <sprite name="0"> with one <sprite name="+2"> <sprite name=Wound>',
     {"CardsToRemove": "[AMC_Basic_P0]", "CardsToAdd": "[AMC_Wound_P2]"}),
    (1, "Venomous Verse", 'Replace one <sprite name="0"> with one <sprite name="+2"> <sprite name=Poison>',
     {"CardsToRemove": "[AMC_Basic_P0]", "CardsToAdd": "[AMC_Poison_P2]"}),
    (1, "Cursed Verse", 'Replace one <sprite name="0"> with one <sprite name="+2"> <sprite name=Curse>',
     {"CardsToRemove": "[AMC_Basic_P0]", "CardsToAdd": "[AMC_Curse_P2]"}),
    (1, "Maddening Verse", 'Replace one <sprite name="0"> with one <sprite name="+3"> <sprite name=Muddle>',
     {"CardsToRemove": "[AMC_Basic_P0]", "CardsToAdd": "[AMC_Muddle_P3]"}),
    (2, "Crescendo", 'Replace two <sprite name="+1"> with one <sprite name="+4">',
     {"CardsToRemove": "[AMC_Basic_P1, AMC_Basic_P1]", "CardsToAdd": "[AMC_Basic_P4]"}),
    (1, "Encore", 'Add three <sprite name="+1"> <sprite name=Rolling>',
     {"CardsToAdd": "[AMC_P1_R, AMC_P1_R, AMC_P1_R]"}),
    (1, "Dirge", 'Add three <sprite name="0"> <sprite name=Curse> <sprite name=Rolling>',
     {"CardsToAdd": "[AMC_Curse_R, AMC_Curse_R, AMC_Curse_R]"}),
    # answered by the plugin when the class is ported (scenario end: 8 XP to every character if her battle goal was met)
    (1, "Storyteller", "At the end of each scenario, each character in that scenario gains 8 experience if you successfully completed your battle goal",
     {"Flags": "[Passive]"}),
]

SOOTHSINGER_MASTERIES = [
    ("Set List", "On your first turn of the scenario and the turn after each of your rests, perform one Song action that you have not yet performed this scenario",
     """\
# NEW TAG: a new song on the first turn and on the turn after every rest (GHMasteries.cs, from the ability and rest records)
CheckAtEndOfScenario: True
BattleGoalCondition:
    - Targets:
        Filter: CountXRounds
        SubFilter: Scenario
        EnemyFamilies: [FrosthavenMods_NewSongAfterRests]
        CharacterIDs:
            ActorID: SoothsingerID
        Value: 1"""),
    ("Full Deck", "Have all 10 monster <sprite name=Curse> cards and all 10 <sprite name=Bless> cards in modifier decks at the same time",
     """\
# NEW TAG: a moment with every curse in the monsters' decks and every bless in the party's (GHMasteries.cs, checked at
# each turn's end from the decks)
CheckAtEndOfScenario: True
BattleGoalCondition:
    - Targets:
        Filter: CountXRounds
        SubFilter: Scenario
        EnemyFamilies: [FrosthavenMods_AllCursesAndBlessesOut]
        CharacterIDs:
            ActorID: SoothsingerID
        Value: 1"""),
]

# ---------------------------------------------------------------- group 5 (sheets read 2026-10-27, ahead of the port)

TINKERER_PERKS = [
    (1, "Steady Hands", 'Remove two <sprite name="-1">',
     {"CardsToRemove": "[AMC_Basic_M1, AMC_Basic_M1]"}),
    (2, "Fine Tuning", 'Replace one <sprite name="-1"> with one <sprite name="+1">',
     {"CardsToRemove": "[AMC_Basic_M1]", "CardsToAdd": "[AMC_Basic_P1]"}),
    (1, "Composure", 'Replace one <sprite name="-2"> with one <sprite name="0">',
     {"CardsToRemove": "[AMC_Basic_M2]", "CardsToAdd": "[AMC_Basic_P0]"}),
    (1, "Wide Reach", 'Replace one <sprite name="0"> with one <sprite name="0"> +1 <sprite name=Target>',
     {"CardsToRemove": "[AMC_Basic_P0]", "CardsToAdd": "[AMC_AddTarget]"}),
    (2, "Jagged Gears", 'Replace one <sprite name="0"> with one <sprite name="+1"> <sprite name=Wound>',
     {"CardsToRemove": "[AMC_Basic_P0]", "CardsToAdd": "[AMC_Wound_P1]"}),
    (2, "Net Launcher", 'Replace one <sprite name="0"> with one <sprite name="+1"> <sprite name=Immobilize>',
     {"CardsToRemove": "[AMC_Basic_P0]", "CardsToAdd": "[AMC_Immobilize_P1]"}),
    (1, "Confusion", 'Replace two <sprite name="0"> with three <sprite name="0"> <sprite name=Muddle> <sprite name=Rolling>',
     {"CardsToRemove": "[AMC_Basic_P0, AMC_Basic_P0]", "CardsToAdd": "[AMC_Muddle_R, AMC_Muddle_R, AMC_Muddle_R]"}),
    (1, "Overcharge", 'Add one <sprite name="+3">',
     {"CardsToAdd": "[AMC_Basic_P3]"}),
    (1, "Restorative Tonic", 'Add two <sprite name="+1"> <sprite name=Heal> 2, self',
     {"CardsToAdd": "[AMC_Heal2_Self_P1, AMC_Heal2_Self_P1]"}),
    (1, "Hardened", 'Ignore negative scenario effects and add two <sprite name="0"> <sprite name=CreateFire> <sprite name=Rolling>',
     {"CardsToAdd": "[AMC_Fire_R, AMC_Fire_R]", "Flags": "[Passive, IgnoreNegativeScenarioEffects]"}),
    # the game's long-rest perk hook (as the Banner Spear's); Frosthaven's heal filter takes allies (self excluded: verify)
    (1, "Rejuvenating Vapor", "Whenever you long rest, you may perform <sprite name=Heal> 2, <sprite name=Range> 3",
     {"Flags": "[Passive, ExecuteAbilitiesAfterLongRest]", "Abilities": """\
    BHeal:
        Heal: 2
        Range: 3
        Target: 1
        Targeting: Range
        Filter: Ally
        CanSkip: True
        CanUndo: False
        AnimOverload: Heal"""}),
]

TINKERER_MASTERIES = [
    ("Field Medic", "Heal an ally or apply a negative condition to an enemy each turn",
     """\
# NEW TAG: every turn taken, an ally healed by the character or a negative condition applied to an enemy (GHMasteries.cs)
CheckAtEndOfScenario: True
BattleGoalCondition:
    - Targets:
        Filter: CountXRounds
        SubFilter: Scenario
        EnemyFamilies: [FrosthavenMods_HealOrDebuffEveryTurn]
        CharacterIDs:
            ActorID: TinkererID
        Value: 1"""),
    ("Spendthrift", "Perform two actions with lost icons before your first rest and then only rest after having played at least two actions with lost icons since your previous rest",
     """\
# NEW TAG: before every rest, at least Amount card halves with a lost icon performed since the previous rest (GHMasteries.cs)
CheckAtEndOfScenario: True
BattleGoalCondition:
    - Targets:
        Filter: CountXRounds
        SubFilter: Scenario
        EnemyFamilies: [FrosthavenMods_LossActionsBeforeRests]
        CharacterIDs:
            ActorID: TinkererID
        Value: 1
        Amount: 2"""),
]

DOOMSTALKER_PERKS = [
    (1, "Composure", 'Remove one <sprite name="-2">',
     {"CardsToRemove": "[AMC_Basic_M2]"}),
    (4, "Steady Aim", 'Replace one <sprite name="-1"> with one <sprite name="+1">',
     {"CardsToRemove": "[AMC_Basic_M1]", "CardsToAdd": "[AMC_Basic_P1]"}),
    (2, "Wide Reach", 'Replace one <sprite name="0"> with one <sprite name="0"> +1 <sprite name=Target> <sprite name=Rolling>',
     {"CardsToRemove": "[AMC_Basic_P0]", "CardsToAdd": "[AMC_AddTarget_R]"}),
    (1, "Stunning Shot", 'Replace one <sprite name="0"> with one <sprite name="0"> <sprite name=Stun>',
     {"CardsToRemove": "[AMC_Basic_P0]", "CardsToAdd": "[AMC_Stun_P0]"}),
    (1, "Barbed Arrow", 'Replace one <sprite name="0"> with one <sprite name="+1"> <sprite name=Wound>',
     {"CardsToRemove": "[AMC_Basic_P0]", "CardsToAdd": "[AMC_Wound_P1]"}),
    (1, "Venom Arrow", 'Replace one <sprite name="0"> with one <sprite name="+1"> <sprite name=Poison>',
     {"CardsToRemove": "[AMC_Basic_P0]", "CardsToAdd": "[AMC_Poison_P1]"}),
    (1, "Dizzying Shot", 'Replace one <sprite name="0"> with one <sprite name="+2"> <sprite name=Muddle>',
     {"CardsToRemove": "[AMC_Basic_P0]", "CardsToAdd": "[AMC_Muddle_P2]"}),
    (2, "Rapid Fire", 'Add two <sprite name="+1"> <sprite name=Rolling>',
     {"CardsToAdd": "[AMC_P1_R, AMC_P1_R]"}),
    (1, "Hardened", 'Ignore negative scenario effects and add one <sprite name="+1"> <sprite name=Immobilize>',
     {"CardsToAdd": "[AMC_Immobilize_P1]", "Flags": "[Passive, IgnoreNegativeScenarioEffects]"}),
    # answered by the plugin when the class is ported (an ally shares the active Dooms' benefits on a long-rest round)
    (1, "Marked for the Hunt", "At the beginning of each round in which you long rest, you may choose one ally to gain the benefits of any of your active Dooms as if the Dooms were in that ally's active area for the round",
     {"Flags": "[Passive]"}),
]

DOOMSTALKER_MASTERIES = [
    ("Fresh Quarry", "Never perform a Doom action that you have already performed in the scenario",
     """\
# NEW TAG: no doom card half performed twice (GHMasteries.cs, from the ability records; at least one doom performed)
CheckAtEndOfScenario: True
BattleGoalCondition:
    - Targets:
        Filter: CountXRounds
        SubFilter: Scenario
        EnemyFamilies: [FrosthavenMods_NoRepeatedDoom]
        CharacterIDs:
            ActorID: DoomstalkerID
        Value: 1"""),
    ("Cull the Herd", "Kill three Doomed enemies during one of your turns",
     """\
# NEW TAG: Value kills of doomed enemies (the kill record's Doom flag) in one of the character's turns (GHMasteries.cs)
BattleGoalCondition:
    - Targets:
        Filter: CountXRounds
        SubFilter: Scenario
        EnemyFamilies: [FrosthavenMods_DoomedKillsInTurn]
        CharacterIDs:
            ActorID: DoomstalkerID
        Value: 3"""),
]

BEASTTYRANT_PERKS = [
    (1, "Steady Hands", 'Remove two <sprite name="-1">',
     {"CardsToRemove": "[AMC_Basic_M1, AMC_Basic_M1]"}),
    (3, "Trained Reflexes", 'Replace one <sprite name="-1"> with one <sprite name="+1">',
     {"CardsToRemove": "[AMC_Basic_M1]", "CardsToAdd": "[AMC_Basic_P1]"}),
    (2, "Savage Strike", 'Replace one <sprite name="0"> with one <sprite name="+2">',
     {"CardsToRemove": "[AMC_Basic_P0]", "CardsToAdd": "[AMC_Basic_P2]"}),
    (2, "Rending Claws", 'Replace one <sprite name="0"> with one <sprite name="+1"> <sprite name=Wound>',
     {"CardsToRemove": "[AMC_Basic_P0]", "CardsToAdd": "[AMC_Wound_P1]"}),
    (1, "Pinning Bite", 'Add two <sprite name="+1"> <sprite name=Immobilize>',
     {"CardsToAdd": "[AMC_Immobilize_P1, AMC_Immobilize_P1]"}),
    (3, "Lick Wounds", 'Add two <sprite name="0"> <sprite name=Heal> 1, self <sprite name=Rolling>',
     {"CardsToAdd": "[AMC_Heal1_R, AMC_Heal1_R]"}),
    (1, "Hardened", 'Ignore negative scenario effects and add two <sprite name="0"> <sprite name=CreateEarth> <sprite name=Rolling>',
     {"CardsToAdd": "[AMC_Earth_R, AMC_Earth_R]", "Flags": "[Passive, IgnoreNegativeScenarioEffects]"}),
    # answered by the plugin when the class is ported (a long-rest round: the bear's turn at 99 becomes Move 3, Loot 1, Heal 3 self if it looted)
    (1, "Bear Treat", "During each round in which you long rest, at initiative 99, you may skip the bear's normal turn to Command: <sprite name=Move> 3; <sprite name=Loot> 1; if the loot ability was performed: <sprite name=Heal> 3, self",
     {"Flags": "[Passive]"}),
]

BEASTTYRANT_MASTERIES = [
    ("Ferocious Companion", "Have your bear summon deal <sprite name=Damage> 10 or more in three consecutive rounds",
     """\
# NEW TAG: Value rounds in a row with Amount damage to enemies by the character's summons (the bear) (GHMasteries.cs)
BattleGoalCondition:
    - Targets:
        Filter: CountXRounds
        SubFilter: Scenario
        EnemyFamilies: [FrosthavenMods_SummonDamageStreak]
        CharacterIDs:
            ActorID: BeastTyrantID
        Value: 3
        Amount: 10"""),
    ("Pack Tactics", "You or your summons must apply a negative condition to at least 10 different enemies",
     """\
# NEW TAG: Value distinct enemies given a negative condition by the character or its summons (GHMasteries.cs)
CheckAtEndOfScenario: True
BattleGoalCondition:
    - Targets:
        Filter: CountXRounds
        SubFilter: Scenario
        EnemyFamilies: [FrosthavenMods_EnemiesDebuffed]
        CharacterIDs:
            ActorID: BeastTyrantID
        Value: 10"""),
]

# ---------------------------------------------------------------- group 6 (sheet read 2026-10-27, ahead of the port)

SUMMONER_PERKS = [
    (1, "Steady Hands", 'Remove two <sprite name="-1">',
     {"CardsToRemove": "[AMC_Basic_M1, AMC_Basic_M1]"}),
    (3, "Focus", 'Replace one <sprite name="-1"> with one <sprite name="+1">',
     {"CardsToRemove": "[AMC_Basic_M1]", "CardsToAdd": "[AMC_Basic_P1]"}),
    (1, "Composure", 'Replace one <sprite name="-2"> with one <sprite name="0">',
     {"CardsToRemove": "[AMC_Basic_M2]", "CardsToAdd": "[AMC_Basic_P0]"}),
    (1, "Fire and Stone", 'Replace two <sprite name="0"> with one <sprite name="0"> <sprite name=CreateFire> <sprite name=Rolling> and one <sprite name="0"> <sprite name=CreateEarth> <sprite name=Rolling>',
     {"CardsToRemove": "[AMC_Basic_P0, AMC_Basic_P0]", "CardsToAdd": "[AMC_Fire_R, AMC_Earth_R]"}),
    (1, "Wind and Shadow", 'Replace two <sprite name="0"> with one <sprite name="0"> <sprite name=CreateAir> <sprite name=Rolling> and one <sprite name="0"> <sprite name=CreateDark> <sprite name=Rolling>',
     {"CardsToRemove": "[AMC_Basic_P0, AMC_Basic_P0]", "CardsToAdd": "[AMC_Air_R, AMC_Dark_R]"}),
    (1, "Empowered", 'Replace two <sprite name="+1"> with two <sprite name="+2">',
     {"CardsToRemove": "[AMC_Basic_P1, AMC_Basic_P1]", "CardsToAdd": "[AMC_Basic_P2, AMC_Basic_P2]"}),
    (1, "Rending Summons", 'Add two <sprite name="0"> <sprite name=Wound> <sprite name=Rolling>',
     {"CardsToAdd": "[AMC_Wound_R, AMC_Wound_R]"}),
    (1, "Venomous Summons", 'Add two <sprite name="0"> <sprite name=Poison> <sprite name=Rolling>',
     {"CardsToAdd": "[AMC_Poison_R, AMC_Poison_R]"}),
    (2, "Siphon", 'Add three <sprite name="0"> <sprite name=Heal> 1, self <sprite name=Rolling>',
     {"CardsToAdd": "[AMC_Heal1_R, AMC_Heal1_R, AMC_Heal1_R]"}),
    (1, "Hardened", 'Ignore negative scenario effects and add two <sprite name="+1">',
     {"CardsToAdd": "[AMC_Basic_P1, AMC_Basic_P1]", "Flags": "[Passive, IgnoreNegativeScenarioEffects]"}),
    # answered by the plugin when the class is ported (once a scenario, at initiative ordering, every summon turns invisible)
    (1, "Phase Out", "Once each scenario, during ordering of initiative, after all ability cards have been revealed, all your summons gain <sprite name=Invisible>",
     {"Flags": "[Passive]"}),
]

SUMMONER_MASTERIES = [
    ("Golem Keeper", "Summon the Lava Golem on your first turn and keep it alive for the entire scenario",
     """\
# NEW TAG: the Lava Golem summoned on the first turn and never killed (GHMasteries.cs, from the ability and summon death records)
CheckAtEndOfScenario: True
BattleGoalCondition:
    - Targets:
        Filter: CountXRounds
        SubFilter: Scenario
        EnemyFamilies: [FrosthavenMods_LavaGolemAllScenario]
        CharacterIDs:
            ActorID: SummonerID
        Value: 1
        Amount: 20236"""),
    ("Menagerie", "Perform the summon action of five different ability cards",
     """\
# NEW TAG: Value different cards whose summon ability was performed (GHMasteries.cs)
CheckAtEndOfScenario: True
BattleGoalCondition:
    - Targets:
        Filter: CountXRounds
        SubFilter: Scenario
        EnemyFamilies: [FrosthavenMods_SummonsFromDifferentCards]
        CharacterIDs:
            ActorID: SummonerID
        Value: 5"""),
]

BERSERKER_PERKS = [
    (1, "Light Step", 'Remove two <sprite name="-1">',
     {"CardsToRemove": "[AMC_Basic_M1, AMC_Basic_M1]"}),
    (2, "Steady Hand", 'Replace one <sprite name="-1"> with one <sprite name="+1">',
     {"CardsToRemove": "[AMC_Basic_M1]", "CardsToAdd": "[AMC_Basic_P1]"}),
    (2, "Open Wounds", 'Replace one <sprite name="0"> with two <sprite name="0"> <sprite name=Wound> <sprite name=Rolling>',
     {"CardsToRemove": "[AMC_Basic_P0]", "CardsToAdd": "[AMC_Wound_R, AMC_Wound_R]"}),
    (2, "Reckless Swing", 'Replace one <sprite name="+1"> with one <sprite name="+2"> <sprite name=Rolling>',
     {"CardsToRemove": "[AMC_Basic_P1]", "CardsToAdd": "[AMC_P2_R]"}),
    (2, "Burning Rage", 'Add one <sprite name="+2"> <sprite name=CreateFire>',
     {"CardsToAdd": "[AMC_Fire_P2]"}),
    (2, "Concussion", 'Add one <sprite name="0"> <sprite name=Stun> <sprite name=Rolling>',
     {"CardsToAdd": "[AMC_Stun_R]"}),
    (1, "Disarming Blow", 'Add one <sprite name="+1"> <sprite name=Disarm> <sprite name=Rolling>',
     {"CardsToAdd": "[AMC_Disarm_P1_R]"}),
    (1, "Second Wind", 'Add two <sprite name="0"> <sprite name=Heal> 1, self <sprite name=Rolling>',
     {"CardsToAdd": "[AMC_Heal1_R, AMC_Heal1_R]"}),
    (1, "Thick Skin", 'Ignore item <sprite name="-1"> effects',
     {"Flags": "[Passive, IgnoreMinusOneItemEffects]"}),
    # Frosthaven's own after-long-rest perks (Perk_BannerSpear_08) run their abilities the same way
    (1, "Rapid Recovery", "Whenever you heal from a long rest, add +1 <sprite name=Heal>",
     {"Flags": "[Passive, ExecuteAbilitiesAfterLongRest]",
      "Abilities": """\
    AHeal:
        Heal: 1
        Filter: Self
        AutoTrigger: True
        SkipAnim: True
        CanUndo: False"""}),
]

BERSERKER_MASTERIES = [
    ("Bloodlust", "Lose at least one hit point during each of your turns, without ever performing the bottom action of Blood Pact",
     """\
# NEW TAGS: lost hit points on every turn; failed by Blood Pact's bottom (card 20324) (GHMasteries.cs)
CheckAtEndOfScenario: True
BattleGoalCondition:
    - Targets:
        Filter: CountXRounds
        SubFilter: Scenario
        EnemyFamilies: [FrosthavenMods_LoseHpEveryTurn]
        CharacterIDs:
            ActorID: BerserkerID
        Value: 1
BattleGoalFailCondition:
    - Targets:
        Filter: CountXRounds
        SubFilter: Scenario
        EnemyFamilies: [FrosthavenMods_UsedCardBottom]
        CharacterIDs:
            ActorID: BerserkerID
        Amount: 20324
        Value: 1"""),
    ("Living on the Edge", "Have exactly one hit point at the end of each of your turns",
     """\
# NEW TAG: one hit point at the end of every turn (GHMasteries.cs)
CheckAtEndOfScenario: True
BattleGoalCondition:
    - Targets:
        Filter: CountXRounds
        SubFilter: Scenario
        EnemyFamilies: [FrosthavenMods_OneHpEveryTurn]
        CharacterIDs:
            ActorID: BerserkerID
        Value: 1"""),
]

CROSSOVER = {
    "Brute": {"perks": BRUTE_PERKS, "masteries": BRUTE_MASTERIES,
              "strings": {"PERK_Brute_10_Bonus": "Rested and Ready: +1 <sprite name=Move> to your next move ability"}},
    "Scoundrel": {"perks": SCOUNDREL_PERKS, "masteries": SCOUNDREL_MASTERIES},
    "Sunkeeper": {"perks": SUNKEEPER_PERKS, "masteries": SUNKEEPER_MASTERIES},
    "Sawbones": {"perks": SAWBONES_PERKS, "masteries": SAWBONES_MASTERIES},
    "Quartermaster": {"perks": QUARTERMASTER_PERKS, "masteries": QUARTERMASTER_MASTERIES},
    "Cragheart": {"perks": CRAGHEART_PERKS, "masteries": CRAGHEART_MASTERIES},
    "Plagueherald": {"perks": PLAGUEHERALD_PERKS, "masteries": PLAGUEHERALD_MASTERIES},
    "Nightshroud": {"perks": NIGHTSHROUD_PERKS, "masteries": NIGHTSHROUD_MASTERIES},
    "Berserker": {"perks": BERSERKER_PERKS, "masteries": BERSERKER_MASTERIES},
    "Hatchet": {"perks": HATCHET_PERKS, "masteries": HATCHET_MASTERIES},
    "RedGuard": {"perks": REDGUARD_PERKS, "masteries": REDGUARD_MASTERIES},
    "Voidwarden": {"perks": VOIDWARDEN_PERKS, "masteries": VOIDWARDEN_MASTERIES},
    "Demolitionist": {"perks": DEMOLITIONIST_PERKS, "masteries": DEMOLITIONIST_MASTERIES},
    "Spellweaver": {"perks": SPELLWEAVER_PERKS, "masteries": SPELLWEAVER_MASTERIES},
    "Mindthief": {"perks": MINDTHIEF_PERKS, "masteries": MINDTHIEF_MASTERIES},
    "Elementalist": {"perks": ELEMENTALIST_PERKS, "masteries": ELEMENTALIST_MASTERIES},
    "Soothsinger": {"perks": SOOTHSINGER_PERKS, "masteries": SOOTHSINGER_MASTERIES},
    "Tinkerer": {"perks": TINKERER_PERKS, "masteries": TINKERER_MASTERIES},
    "Doomstalker": {"perks": DOOMSTALKER_PERKS, "masteries": DOOMSTALKER_MASTERIES},
    "BeastTyrant": {"perks": BEASTTYRANT_PERKS, "masteries": BEASTTYRANT_MASTERIES},
    "Summoner": {"perks": SUMMONER_PERKS, "masteries": SUMMONER_MASTERIES},
}


def mastery_ids(cls):
    """The MasteryIDs list for the class's character file, or None when it has no crossover sheet."""
    sheet = CROSSOVER.get(cls)
    return [f"{cls}_MASTERY_{i + 1}" for i in range(len(sheet["masteries"]))] if sheet else None


def files(cls):
    """[(ruleset path, text)] for the class's crossover perks and masteries; empty without a sheet."""
    sheet = CROSSOVER.get(cls)
    if not sheet:
        return []
    out = []
    for i, (available, _name, _desc, fields) in enumerate(sheet["perks"], start=1):
        lines = ["Parser: Perk", f"ID: {cls}{i:02d}", f"Name: PERK_{cls}_{i:02d}_Name", f"Description: PERK_{cls}_{i:02d}_Desc",
                 f"CharacterID: {cls}ID", f"Available: {available}"]
        for key in ("Cost", "CardsToRemove", "CardsToAdd", "Flags"):
            if key in fields:
                lines.append(f"{key}: {fields[key]}")
        if "Abilities" in fields:
            lines.append("Abilities:")
            lines.append(fields["Abilities"])
        out.append((f"Global/Perks/{cls}/Perk_{cls}_{i:02d}.yml", "\n".join(lines) + "\n"))
    # the attack-modifier cards those perks add that Frosthaven has no file for
    added = set()
    for _available, _name, _desc, fields in sheet["perks"]:
        added.update(re.findall(r"\w+", fields.get("CardsToAdd", "")))
    for card in sorted(added & set(NEW_CARDS)):
        out.append((f"Global/AttackModifiers/{card}.yml", f"Parser: AttackModifier\nName: {card}\n{NEW_CARDS[card]}"))
    for i, (_name, _desc, body) in enumerate(sheet["masteries"], start=1):
        head = ["Parser: Mastery", f"ID: {cls}_MASTERY_{i}", f"LocalisedName: {cls}_MASTERY_{i}_Name",
                f"LocalisedDescription: {cls}_MASTERY_{i}_Desc", "PerkPoints: 1"]
        out.append((f"Campaign/Masteries/{cls}_Mastery_{i}.yml", "\n".join(head) + "\n" + body + "\n"))
    return out


def strings(cls):
    """{text key: English} for the class's crossover perks and masteries."""
    sheet = CROSSOVER.get(cls)
    if not sheet:
        return {}
    out = dict(sheet.get("strings", {}))
    for i, (_available, name, desc, _fields) in enumerate(sheet["perks"], start=1):
        out[f"PERK_{cls}_{i:02d}_Name"] = name
        out[f"PERK_{cls}_{i:02d}_Desc"] = desc
    for i, (name, desc, _body) in enumerate(sheet["masteries"], start=1):
        out[f"{cls}_MASTERY_{i}_Name"] = name
        out[f"{cls}_MASTERY_{i}_Desc"] = desc
    return out
