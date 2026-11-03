# Plagueherald (2nd edition) perks, from the perk sheet (tools/gh2e/classes.json "Plagueherald" perks; docs/gh2e/class-notes.md),
# in gh_crossover's table format. 18 checkboxes, as on the sheet.
# Plague = the character resource "Plague" (resources.yml): a give places one token from the class supply of 12 (plugin
# GHPlague, see NOTES.md).

_PLAGUE_OR_POISON_TARGETING = """\
        Filter: Enemy
        Target: 1
        Targeting: Custom
        TargetingVars:
            AddValidActorsInRange: GameState.GetAllEnemiesInRange(TargetingActor, TargetActor.ArrayIndex, 2)
        MiscVars:
            TreatAsNonInlineForTargeting: True
"""

NEW_CARDS = {
    # "Give the target or one enemy within Range 2 of the target Plague" (the targeting of Frosthaven's
    # AMC_Damage1Range1_P0, range 2: the attack's target is at range 0 of itself)
    "AMC_GH2E_Plagueherald2_Plague_P0": 'MathModifier: "+0"\nAbilities:\n    PlagueAbility:\n        NullTargeting: True\n'
                                        + _PLAGUE_OR_POISON_TARGETING
                                        + '        ResourcesToGiveToTargets:\n            - ["Plague", 1]\n',
    # "Give the target or one enemy within Range 2 of the target Poison", rolling. A Poison ability on a modifier card is
    # the error screen when drawn (the game's modifier code runs no condition abilities): a NullTargeting carrying the
    # condition, as Frosthaven's cards give an enemy a condition (Infuser Diamondization: NullTargeting, IsNegative,
    # Conditions), with the Plague card's targeting.
    "AMC_GH2E_Plagueherald2_Poison_R": """\
MathModifier: "+0"
Rolling: True
Abilities:
    PoisonAbility:
        NullTargeting: True
        Conditions: [Poison]
        Filter: Enemy
        Target: 1
        Targeting: Custom
        TargetingVars:
            AddValidActorsInRange: GameState.GetAllEnemiesInRange(TargetingActor, TargetActor.ArrayIndex, 2)
        MiscVars:
            TreatAsNonInlineForTargeting: True
            IsNegative: True
""",
}

PERKS = [   # (checkboxes, name, description in sprite markup, {perk fields})
    (2, "Virulence", 'Replace one <sprite name="-1"> with one <sprite name="+1">',
     {"CardsToRemove": "[AMC_Basic_M1]", "CardsToAdd": "[AMC_Basic_P1]"}),
    (2, "Carrier", 'Replace one <sprite name="-1"> with one <sprite name="0"> "Give the target or one <sprite name=Enemy> within <sprite name=Range> 2 of the target Plague"',
     {"CardsToRemove": "[AMC_Basic_M1]", "CardsToAdd": "[AMC_GH2E_Plagueherald2_Plague_P0]"}),
    (2, "Hex", 'Replace one <sprite name="0"> with one <sprite name="+1"> <sprite name=Curse>',
     {"CardsToRemove": "[AMC_Basic_P0]", "CardsToAdd": "[AMC_Curse_P1]"}),
    (2, "Paralysis", 'Replace one <sprite name="0"> with one <sprite name="+1"> <sprite name=Immobilize>',
     {"CardsToRemove": "[AMC_Basic_P0]", "CardsToAdd": "[AMC_Immobilize_P1]"}),
    (1, "Lethal Strain", 'Replace two <sprite name="+1"> with two <sprite name="+2">',
     {"CardsToRemove": "[AMC_Basic_P1, AMC_Basic_P1]", "CardsToAdd": "[AMC_Basic_P2, AMC_Basic_P2]"}),
    (2, "Numbing Agent", 'Add one <sprite name="+1"> <sprite name=Stun>',
     {"CardsToAdd": "[AMC_Stun_P1]"}),
    (1, "Toxic Touch", 'Add three <sprite name="0"> "Give the target or one <sprite name=Enemy> within <sprite name=Range> 2 of the target <sprite name=Poison>" <sprite name=Rolling>',
     {"CardsToAdd": "[AMC_GH2E_Plagueherald2_Poison_R, AMC_GH2E_Plagueherald2_Poison_R, AMC_GH2E_Plagueherald2_Poison_R]"}),
    (1, "Soothing Swarm", 'Add two "<sprite name=Heal> 1, target 1 <sprite name=Allies>" <sprite name=Rolling>',
     {"CardsToAdd": "[AMC_Heal1_1Ally_R, AMC_Heal1_1Ally_R]"}),
    (1, "Unfazed", 'Ignore scenario effects and add one <sprite name="+1">',
     {"CardsToAdd": "[AMC_Basic_P1]", "Flags": "[Passive, IgnoreNegativeScenarioEffects]"}),
    # Flying for the whole scenario: the 1st edition Winged Congregation's persistent Fly bonus, placed at scenario start
    (1, "Winged", "You have <sprite name=Flying>",
     {"Flags": "[Passive, ApplyActiveBonusesAtScenarioStart]", "Abilities": """\
    TFly:
        Move:
            Amount: 0
            Fly: True
        Filter: Self
        AutoTrigger: True
        SkipAnim: True
        ActiveBonus:
            Duration: Persistent
            Behaviour: BuffMove
            Hidden: True"""}),
    # Answered by the plugin (GHPlagueherald2e, perk Plagueherald211): when the long rest's own heal is about to apply,
    # the Plagueherald may remove Poison from one ally (asked ally by ally) to add +1 to that heal.
    (1, "Purging Rest", "Whenever you heal from a long rest, you may remove <sprite name=Poison> from one <sprite name=Allies> to add +1 <sprite name=Heal>",
     {"Flags": "[Passive]"}),
    # Answered by the plugin (GHPlagueherald2e, perk Plagueherald212): once each scenario (the used mark is the hidden
    # resource GH2EXornUsed on the Plagueherald, saved with the scenario), when an enemy that has Plague would die, the
    # Plagueherald may first control it: it performs the abilities of its monster ability card for this round (its
    # attacks also give Plague), driven by the Plagueherald's player, then it dies (the engine's own on-death path:
    # the kill stays credited as it was).
    (1, "Xorn's Last Gift", "Once each scenario, when an <sprite name=Enemy> that has Plague would die, first control that <sprite name=Enemy> and have it perform the abilities on its ability card, adding Plague to all its attacks, then it dies",
     {"Cost": 2, "Flags": "[Passive]"}),
]

MASTERIES = [
    ("Plague Bearer's Harvest",
     "In a single turn, kill three or more enemies without drawing an attack modifier card",
     """\
# Answered by the Frosthaven Mods plugin (GHPlagueherald2e.cs, FrosthavenMods_KillsWithoutModifierInTurn): a turn of the
# Plagueherald in which he killed Value or more enemies and drew no attack modifier card (kill and modifier records).
CheckAtEndOfScenario: True
BattleGoalCondition:
    - Targets:
        Filter: CountXRounds
        SubFilter: Scenario
        EnemyFamilies: [FrosthavenMods_KillsWithoutModifierInTurn]
        CharacterIDs:
            ActorID: Plagueherald2ID
        Value: 3"""),
    ("Endless Contagion",
     "In a single scenario, either apply or remove Poison from an ally or enemy each round",
     """\
# Answered by the Frosthaven Mods plugin (GHPlagueherald2e.cs, FrosthavenMods_PoisonEveryRound): in every round the
# Plagueherald took a turn, he gave Poison to or removed Poison from a figure (counted as it happens, kept in hidden
# resources saved with the scenario).
CheckAtEndOfScenario: True
BattleGoalCondition:
    - Targets:
        Filter: CountXRounds
        SubFilter: Scenario
        EnemyFamilies: [FrosthavenMods_PoisonEveryRound]
        CharacterIDs:
            ActorID: Plagueherald2ID
        Value: 1"""),
]
STRINGS = {}
