# Elementalist (2nd edition) perks, from the perk sheet in tools/gh2e/classes.json ("Savvas Elementalist", 18 boxes).
# gh_crossover's table format: (checkboxes, name, description in sprite markup, {perk fields}).
# Mixed-element modifiers infuse one of the two with the element picker (Frosthaven's own AMC_FireAir_P1 shape); a wild
# modifier infuses any element with the picker.

_C = "AMC_GH2E_Elementalist2_"

PERKS = [
    (1, "Flame and Gale", 'Replace one <sprite name="-1"> with one <sprite name="0"> <sprite name=CreateMixed-FA>',
     {"CardsToRemove": "[AMC_Basic_M1]", "CardsToAdd": f"[{_C}FireAir_P0]"}),
    (1, "Flame and Stone", 'Replace one <sprite name="-1"> with one <sprite name="0"> <sprite name=CreateMixed-FE>',
     {"CardsToRemove": "[AMC_Basic_M1]", "CardsToAdd": f"[{_C}FireEarth_P0]"}),
    (1, "Frost and Gale", 'Replace one <sprite name="-1"> with one <sprite name="0"> <sprite name=CreateMixed-IA>',
     {"CardsToRemove": "[AMC_Basic_M1]", "CardsToAdd": f"[{_C}IceAir_P0]"}),
    (1, "Frost and Stone", 'Replace one <sprite name="-1"> with one <sprite name="0"> <sprite name=CreateMixed-IE>',
     {"CardsToRemove": "[AMC_Basic_M1]", "CardsToAdd": f"[{_C}IceEarth_P0]"}),
    (2, "Fire and Ice", 'Replace one <sprite name="0"> with one <sprite name="+1"> <sprite name=CreateMixed-FI>',
     {"CardsToRemove": "[AMC_Basic_P0]", "CardsToAdd": f"[{_C}FireIce_P1]"}),
    (2, "Air and Earth", 'Replace one <sprite name="0"> with one <sprite name="+1"> <sprite name=CreateMixed-AE>',
     {"CardsToRemove": "[AMC_Basic_P0]", "CardsToAdd": f"[{_C}AirEarth_P1]"}),
    (2, "Light and Dark", 'Replace one <sprite name="0"> with one <sprite name="+1"> <sprite name=CreateMixed-LD>',
     {"CardsToRemove": "[AMC_Basic_P0]", "CardsToAdd": f"[{_C}LightDark_P1]"}),
    (2, "Wild Surge", 'Add two <sprite name=CreateWild> <sprite name=Rolling>',
     {"CardsToAdd": f"[{_C}Wild_R, {_C}Wild_R]"}),
    (1, "Unfazed", 'Ignore scenario effects and add one <sprite name=CreateWild> <sprite name=Rolling>',
     {"CardsToAdd": f"[{_C}Wild_R]", "Flags": "[Passive, IgnoreNegativeScenarioEffects]"}),
    # 2e: "At the start of each scenario, you may infuse any": the plugin (GHElementalist2e.cs, Primed) queues an optional
    # infuse-any with the element picker among Frosthaven's scenario-start abilities (StartScenarioAbilities, HIVE's
    # mode choice), strong at once
    (1, "Primed", 'At the start of each scenario, you may <sprite name=CreateWild>',
     {"Flags": "[Passive]"}),
    # Frosthaven's own consume-any-to-infuse-any shape (AMC_ConsumeInfuseWild), on the long-rest hook
    (1, "Restful Flux", 'Whenever you long rest, <sprite name=ConsumeWild>: <sprite name=CreateWild>',
     {"Flags": "[Passive, ExecuteAbilitiesAfterLongRest]", "Abilities": """\
    BRestConsume:
        Consume:
            Elements: Any
            Abilities:
                BRestInfuse:
                    Infuse: [Any]
                    ShowElementPicker: True
                    CanUndo: False"""}),
    # 2e: Fire, Ice, Air and Earth don't wane; Light and Dark do. The plugin (GHElementalist2e.cs, Elemental Mastery)
    # keeps those four strong through the end of the round (Frosthaven's ElementsDontWane would hold all six)
    (1, "Elemental Mastery", '<sprite name=CreateFire>, <sprite name=CreateIce>, <sprite name=CreateAir> and <sprite name=CreateEarth> do not wane at the end of each round',
     {"Cost": 3, "Flags": "[Passive]"}),
]

MASTERIES = [
    ("Elemental Overload", "In a single scenario, consume 4 or more elements in a single turn 5 times",
     """\
# Answered by the Frosthaven Mods plugin (GHElementalist2e.cs, FrosthavenMods_ConsumesInOneTurn): Value turns with Amount
# or more element consumes by the Elementalist, from the element records.
CheckAtEndOfScenario: True
BattleGoalCondition:
    - Targets:
        Filter: CountXRounds
        SubFilter: Scenario
        EnemyFamilies: [FrosthavenMods_ConsumesInOneTurn]
        CharacterIDs:
            ActorID: Elementalist2ID
        Value: 5
        Amount: 4"""),
    ("Elemental Harmony", "Once in 3 different scenarios, have all 6 elements strong or waning at the same time",
     """\
# Answered by the Frosthaven Mods plugin (GHElementalist2e.cs, FrosthavenMods_AllSixElementsInScenarios): the moment all
# six elements are strong or waning is noted on the Elementalist (saved with the scenario); the scenarios are counted
# per character in the campaign save, Value different scenarios.
CheckAtEndOfScenario: True
BattleGoalCondition:
    - Targets:
        Filter: CountXRounds
        SubFilter: Scenario
        EnemyFamilies: [FrosthavenMods_AllSixElementsInScenarios]
        CharacterIDs:
            ActorID: Elementalist2ID
        Value: 3"""),
]


def _mixed(math, a, b):
    return f'MathModifier: "{math}"\nAbilities:\n    InfuseAbility:\n        Infuse: [{a}, {b}]\n        ShowElementPicker: True\n        CanUndo: False\n'


NEW_CARDS = {
    f"{_C}FireAir_P0": _mixed("+0", "Fire", "Air"),
    f"{_C}FireEarth_P0": _mixed("+0", "Fire", "Earth"),
    f"{_C}IceAir_P0": _mixed("+0", "Ice", "Air"),
    f"{_C}IceEarth_P0": _mixed("+0", "Ice", "Earth"),
    f"{_C}FireIce_P1": _mixed("+1", "Fire", "Ice"),
    f"{_C}AirEarth_P1": _mixed("+1", "Air", "Earth"),
    f"{_C}LightDark_P1": _mixed("+1", "Light", "Dark"),
    f"{_C}Wild_R": 'MathModifier: "+0"\nRolling: True\nAbilities:\n    InfuseAbility:\n        Infuse: [Any]\n        ShowElementPicker: True\n        CanUndo: False\n',
}

STRINGS = {}
