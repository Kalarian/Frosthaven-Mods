# Silent Knife (2nd edition) perks: the perk sheet "Human Silent Knife" (tools/gh2e/classes.json; Secretariat agrees),
# 18 checks in all. gh_crossover's table format: (checkboxes, name, description in sprite markup, {perk fields}).
# A linked perk (two boxes joined on the sheet) is one perk with Available 1, Cost 2, as gh_crossover writes them.
# Decisions in NOTES.md.
from gh_crossover import _once_per_quest

PERKS = [
    (1, "Composure", 'Remove one <sprite name="-2">',
     {"CardsToRemove": "[AMC_Basic_M2]"}),
    (1, "Light Step", 'Remove two <sprite name="-1">',
     {"CardsToRemove": "[AMC_Basic_M1, AMC_Basic_M1]"}),
    (3, "Steady Hand", 'Replace one <sprite name="-1"> with one <sprite name="+1">',
     {"CardsToRemove": "[AMC_Basic_M1]", "CardsToAdd": "[AMC_Basic_P1]"}),
    # "Gain one money token if this attack targeted an adjacent enemy": Frosthaven has no way to hand a hero a money
    # token (loot is picked up from the map); a plain +1 until the plugin can (NEEDS PLUGIN, NOTES.md)
    (3, "Pickpocket", 'Replace one <sprite name="0"> with one <sprite name="+1"> "Gain one money token if this attack '
     'targeted an adjacent <sprite name=Enemy>"',
     {"CardsToRemove": "[AMC_Basic_P0]", "CardsToAdd": "[AMC_Basic_P1]"}),
    (2, "Vital Strike", 'Replace one <sprite name="0"> with one <sprite name="+2">',
     {"CardsToRemove": "[AMC_Basic_P0]", "CardsToAdd": "[AMC_Basic_P2]"}),
    (2, "Vanish", 'Replace one <sprite name="+1"> with one <sprite name="+1"> "<sprite name=Invisible>, self" '
     '<sprite name=Rolling>',
     {"CardsToRemove": "[AMC_Basic_P1]", "CardsToAdd": "[AMC_GH2E_SilentKnife_Invisible_P1_R]"}),
    (2, "Disarming Cut", 'Add one <sprite name="+1"> <sprite name=Disarm>',
     {"CardsToAdd": "[AMC_Disarm_P1]"}),
    # Frosthaven's after-rest perk hooks (both rests, as the 1e Plagueherald's Grave Defense); the money token placed in
    # the enemy's hex has no Frosthaven form, so the attack is free and may be skipped (NEEDS PLUGIN for the cost)
    (1, "Parting Gift", "At the end of each of your rests, you may place one of your money tokens in the hex occupied by "
     "an <sprite name=Enemy> within <sprite name=Range> 3 to perform: <sprite name=Attack> 1, <sprite name=Target> that "
     "<sprite name=Enemy>, <sprite name=Range> 3",
     {"Flags": "[Passive, ExecuteAbilitiesAfterShortRest, ExecuteAbilitiesAfterLongRest]", "Abilities": """\
    BAttack:
        Attack: 1
        Range: 3
        Target: 1
        Targeting: Range
        Filter: Enemy
        CanSkip: True
        CanUndo: False"""}),
    # needs the plugin (no Frosthaven hook on city events): a passive with its text only
    (1, "Fence", "Whenever you complete a city event, draw an attack modifier card as though you were performing an "
     "<sprite name=Attack> 4. Gain an amount of gold equal to the <sprite name=Damage> the attack would have dealt",
     {"Flags": "[Passive]"}),
    # two linked boxes: the 1st edition Scoundrel's Cloak of Invisibility (Frosthaven's once-per-scenario toggle)
    (1, "Cloak of Invisibility", "Once each scenario, during your turn, you may perform: <sprite name=Invisible>, self",
     {"Cost": 2, "Flags": "[Passive, ApplyActiveBonusesAtScenarioStart]",
      "Abilities": _once_per_quest("PERK_SilentKnife_10_Desc", "Invisible", "            Invisible: True")}),
]

# The two 2e masteries ("only attack flanked or isolated enemies, at least 10 attacks"; "end 3 scenarios with 12+
# money tokens") fit no existing mastery tag: none written (NOTES.md)
MASTERIES = []

NEW_CARDS = {
    # +1, Invisible self, rolling (Frosthaven's AMC_Invisible_R with +1, as AMC_P1_R is a rolling +1)
    "AMC_GH2E_SilentKnife_Invisible_P1_R": 'MathModifier: "+1"\nRolling: True\nAbilityOverrides:\n    NegCon:\n'
                                           '        ParentName: AMC_GH2E_SilentKnife_Invisible_P1_R\n'
                                           '        Conditions: [Invisible]\n',
}

STRINGS = {}
