# Tinkerer (2nd edition) perks, from the perk sheet in tools/gh2e/classes.json (18 checks; Secretariat agrees).
# gh_crossover's table format: (checkboxes, name, description in sprite markup, {perk fields}).
PERKS = [
    (1, "Composure", 'Replace one <sprite name="-2"> with one <sprite name="0">',
     {"CardsToRemove": "[AMC_Basic_M2]", "CardsToAdd": "[AMC_Basic_P0]"}),
    (3, "First Aid Kit", 'Replace one <sprite name="-1"> with one <sprite name="0"> <sprite name=Heal> 1, one <sprite name=Allies>',
     {"CardsToRemove": "[AMC_Basic_M1]", "CardsToAdd": "[AMC_Heal1_1Ally]"}),
    (2, "Jagged Gears", 'Replace one <sprite name="0"> with one <sprite name="+1"> <sprite name=Wound>',
     {"CardsToRemove": "[AMC_Basic_P0]", "CardsToAdd": "[AMC_Wound_P1]"}),
    (2, "Toxic Coating", 'Replace one <sprite name="0"> with one <sprite name="+1"> <sprite name=Poison>',
     {"CardsToRemove": "[AMC_Basic_P0]", "CardsToAdd": "[AMC_Poison_P1]"}),
    (2, "Net Launcher", 'Replace one <sprite name="0"> with one <sprite name="+1"> <sprite name=Immobilize>',
     {"CardsToRemove": "[AMC_Basic_P0]", "CardsToAdd": "[AMC_Immobilize_P1]"}),
    (3, "Stimulant Dart", 'Add one <sprite name="+2"> <sprite name=Strengthen>, one <sprite name=Allies>',
     {"CardsToAdd": "[AMC_GH2E_Tinkerer2_Strengthen_Ally_P2]"}),
    (1, "Hardened", 'Ignore negative scenario effects and add one <sprite name="0"> <sprite name=Heal> 1, one <sprite name=Allies>',
     {"CardsToAdd": "[AMC_Heal1_1Ally]", "Flags": "[Passive, IgnoreNegativeScenarioEffects]"}),
    # needs the plugin (no Frosthaven perk flag lends an item to an ally): a passive with its text only
    (1, "Shared Supplies", "Once each scenario, one adjacent <sprite name=Allies> may use one of your potion <sprite name=SmallItem> items during their turn without it becoming lost",
     {"Flags": "[Passive]"}),
    # needs the plugin (no Frosthaven trigger fires on a lost action): a passive with its text only
    (1, "Spare Parts", "Whenever you perform an action with a lost icon, you may infuse any one element",
     {"Flags": "[Passive]"}),
    # two linked boxes on the sheet: one perk that costs two checks (Frosthaven's Cost: 2); the 1st edition Tinkerer's
    # Rejuvenating Vapor (the game's long-rest perk hook)
    (1, "Rejuvenating Vapor", "Whenever you long rest, you may perform <sprite name=Heal> 2, <sprite name=Range> 3",
     {"Cost": 2, "Flags": "[Passive, ExecuteAbilitiesAfterLongRest]", "Abilities": """\
    BHeal:
        Heal: 2
        Range: 3
        Target: 1
        Targeting: Range
        Filter: [Self, Ally]
        CanSkip: True
        CanUndo: False
        AnimOverload: Heal"""}),
]

MASTERIES = [
    # the 1st edition Tinkerer's Field Medic tag (an ally healed or an enemy given a negative condition on every turn);
    # 2e also counts a positive condition or a shield given to an ally, which the tag doesn't (NOTES.md)
    ("Field Medic", "For an entire scenario, during each of your turns, give an enemy a negative condition, give an ally a positive condition, heal an ally, or grant an ally <sprite name=Shield>",
     """\
# NEW TAG: every turn taken, an ally healed by the character or a negative condition applied to an enemy (GHMasteries.cs)
CheckAtEndOfScenario: True
BattleGoalCondition:
    - Targets:
        Filter: CountXRounds
        SubFilter: Scenario
        EnemyFamilies: [FrosthavenMods_HealOrDebuffEveryTurn]
        CharacterIDs:
            ActorID: Tinkerer2ID
        Value: 1"""),
    # the Spellweaver's Deep Reserves tag: Value different lost card halves, each performed Amount times
    ("Inventor's Stamina", "Perform 11 different actions with a lost icon",
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
            ActorID: Tinkerer2ID
        Value: 11
        Amount: 1"""),
]

NEW_CARDS = {
    # +2, Strengthen one ally (Frosthaven's AMC_Strengthen_Ally_P1 with +2)
    "AMC_GH2E_Tinkerer2_Strengthen_Ally_P2": 'MathModifier: "+2"\nControlTargetingActor: True\nAbilities:\n    AStrengthen:\n'
                                             '        Strengthen: True\n        Filter: Ally\n        Targeting: Range\n'
                                             '        Range: 99\n        CanUndo: False\n',
}

STRINGS = {}
