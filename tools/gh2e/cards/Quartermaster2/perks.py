# Quartermaster (2nd edition) perks: the perk sheet "Valrath Quartermaster" (tools/gh2e/classes.json), 18 checks in all.
# gh_crossover's table format: (checkboxes, name, description in sprite markup, {perk fields}).
# A linked perk (two boxes joined on the sheet) is one perk with Available 1, Cost 2, as gh_crossover writes them.
# "Gain the <item>" modifiers hand the named class item over (a self NullTargeting marked GH2E_QMGive / GH2E_QMGiveOr,
# done by the plugin GHQuartermaster.cs, which asks which of the two). Supplies are the class resource "Supplies"
# (resources.yml).

PERKS = [
    (2, "Muddling Strike", 'Replace two <sprite name="-1"> with one <sprite name="0"> <sprite name=Muddle>',
     {"CardsToRemove": "[AMC_Basic_M1, AMC_Basic_M1]", "CardsToAdd": "[AMC_GH2E_Quartermaster2_Muddle_P0]"}),
    (3, "Restock", 'Replace one <sprite name="0"> with one <sprite name="0"> "Gain 10 supplies"',
     {"CardsToRemove": "[AMC_Basic_P0]", "CardsToAdd": "[AMC_GH2E_Quartermaster2_Supplies10_P0]"}),
    (1, "Piercing Spears", 'Replace two <sprite name="0"> with two <sprite name=Pierce> 3 <sprite name=Rolling>',
     {"CardsToRemove": "[AMC_Basic_P0, AMC_Basic_P0]", "CardsToAdd": "[AMC_Pierce3_R, AMC_Pierce3_R]"}),
    # "gain the Barbed Strip or Iron Plate item": the plugin gives the chosen one (GHQuartermaster.cs)
    (2, "Spare Armor", 'Replace one <sprite name="+1"> with two <sprite name="0"> "Gain the Barbed Strip or Iron Plate item"',
     {"CardsToRemove": "[AMC_Basic_P1]",
      "CardsToAdd": "[AMC_GH2E_Quartermaster2_GainArmor_P0, AMC_GH2E_Quartermaster2_GainArmor_P0]"}),
    (1, "Honed Edges", 'Replace two <sprite name="+1"> with two <sprite name="+2">',
     {"CardsToRemove": "[AMC_Basic_P1, AMC_Basic_P1]", "CardsToAdd": "[AMC_Basic_P2, AMC_Basic_P2]"}),
    # the rolling "gain the Scroll of Relocation or Sharpened Dirk item": the plugin gives the chosen one
    (2, "Quick Draw", 'Add one "Gain the Scroll of Relocation or Sharpened Dirk item" <sprite name=Rolling>',
     {"CardsToAdd": "[AMC_GH2E_Quartermaster2_GainScroll_R]"}),
    (2, "Stunning Blow", 'Add one <sprite name=Stun> <sprite name=Rolling>',
     {"CardsToAdd": "[AMC_Stun_R]"}),
    (1, "Well Equipped", 'Ignore item <sprite name="-1"> effects and remove one <sprite name="0">',
     {"CardsToRemove": "[AMC_Basic_P0]", "Flags": "[Passive, IgnoreMinusOneItemEffects]"}),
    # Frosthaven's perk flags have an extra one-hand slot (AddOneAdditionalOneHandSlot) but no extra small item: the plugin
    # (GHQuartermaster.cs) adds one small-item slot to a character with this perk (perk ID Quartermaster209).
    (1, "Deep Pockets", 'You may bring one additional <sprite name=SmallItem> item into each scenario',
     {"Flags": "[Passive]"}),
    # The printed perk (2e "Well-Connected": faction items 5 gold cheaper, faction reputation +2) has no function outside
    # Gloomhaven 2e (GH2e FAQ, crossover section). The user's chosen Frosthaven equivalent: while a character with this perk
    # is in the party, the party buys items 5 gold cheaper; the reputation half is dropped. Done by the plugin
    # (GHQuartermaster.cs, perk ID Quartermaster210).
    (1, "Guild Connections", "While this character is in your party, your party may purchase items as though their costs "
     "were five gold less",
     {"Flags": "[Passive]"}),
    # Frosthaven's after-long-rest perks (Perk_BannerSpear_08, the Berserker's Rapid Recovery) run their abilities this way
    (1, "Resupply", "Whenever you long rest, gain 10 supplies",
     {"Cost": 2, "Flags": "[Passive, ExecuteAbilitiesAfterLongRest]",
      "Abilities": """\
    ASupplies:
        Null: True
        Filter: Self
        AutoTrigger: True
        SkipAnim: True
        CanUndo: False
        ResourcesToAddOnAbilityEnd:
            - ["Supplies", 10]"""}),
]

# The sheet's masteries, answered by the plugin from its own records of supply spends and class-item gives
# (GHQuartermaster.cs, the markers in EnemyFamilies).
MASTERIES = [
    ("Quartermaster's Ledger",
     "In a single scenario, spend 50 supplies to Recover 50 gold of lost items three times",
     """\
# Three supply spends in one scenario that each spent 50 supplies and recovered lost items worth 50 gold or more
# together, answered by the Frosthaven Mods plugin (the marker in EnemyFamilies, GHQuartermaster.cs).
# Value = spends, Amount = supplies spent and gold of lost items recovered.
CheckAtEndOfScenario: True
BattleGoalCondition:
    - Targets:
        Filter: CountXRounds
        SubFilter: Scenario
        EnemyFamilies: [FrosthavenMods_QMSupplySpends]
        CharacterIDs:
            ActorID: Quartermaster2ID
        Value: 3
        Amount: 50"""),
    ("Never Short",
     "For an entire scenario, give yourself or an ally an item from the Quartermaster item supply each round, except "
     "when performing a long rest",
     """\
# A class item given (to yourself or an ally) in every round of the scenario in which the Quartermaster took a turn and
# did not long rest, answered by the Frosthaven Mods plugin (the marker in EnemyFamilies, GHQuartermaster.cs).
CheckAtEndOfScenario: True
BattleGoalCondition:
    - Targets:
        Filter: CountXRounds
        SubFilter: Scenario
        EnemyFamilies: [FrosthavenMods_QMItemEveryRound]
        CharacterIDs:
            ActorID: Quartermaster2ID
        Value: 1"""),
]

NEW_CARDS = {
    # +0 Muddle (Frosthaven has the +0 Disarm one, AMC_Disarm_P0; its Muddle cards are rolling or +1 and up)
    "AMC_GH2E_Quartermaster2_Muddle_P0": ('MathModifier: "+0"\nAbilityOverrides:\n    NegCon:\n'
                                          '        ParentName: AMC_GH2E_Quartermaster2_Muddle_P0\n        Conditions: [Muddle]\n'),
    # +0, gain 10 supplies (the class resource): a self NullTargeting (Frosthaven's AMC_Resonance_P1; a Null ability on
    # a modifier card is the error screen when drawn) whose end adds the supplies, as the cards' own gains
    "AMC_GH2E_Quartermaster2_Supplies10_P0": ('MathModifier: "+0"\nAbilities:\n    ASupplies:\n        NullTargeting: True\n'
                                              '        Filter: Self\n        AutoTrigger: True\n        SkipAnim: True\n'
                                              '        ResourcesToAddOnAbilityEnd:\n            - ["Supplies", 10]\n'),
    # +0, gain the Barbed Strip or Iron Plate (the plugin asks which): a self NullTargeting as AMC_Resonance_P1
    "AMC_GH2E_Quartermaster2_GainArmor_P0": ('MathModifier: "+0"\nAbilities:\n    AGain:\n        NullTargeting: True\n'
                                             '        Filter: Self\n        AutoTrigger: True\n        SkipAnim: True\n'
                                             '        MiscVars:\n            GH2E_QMGive: 8\n            GH2E_QMGiveOr: 7\n'),
    # rolling, gain the Scroll of Relocation or Sharpened Dirk
    "AMC_GH2E_Quartermaster2_GainScroll_R": ('MathModifier: "+0"\nRolling: True\nAbilities:\n    AGain:\n        NullTargeting: True\n'
                                             '        Filter: Self\n        AutoTrigger: True\n        SkipAnim: True\n'
                                             '        MiscVars:\n            GH2E_QMGive: 5\n            GH2E_QMGiveOr: 1\n'),
}

STRINGS = {   # the new modifiers' names (the game looks a modifier's Name up as text)
    "AMC_GH2E_Quartermaster2_Muddle_P0": '<sprite name="0"> <sprite name=Muddle>',
    "AMC_GH2E_Quartermaster2_Supplies10_P0": '<sprite name="0"> Gain 10 supplies',
    "AMC_GH2E_Quartermaster2_GainArmor_P0": '<sprite name="0"> Gain the Barbed Strip or Iron Plate item',
    "AMC_GH2E_Quartermaster2_GainScroll_R": '<sprite name=Rolling> Gain the Scroll of Relocation or Sharpened Dirk item',
}
