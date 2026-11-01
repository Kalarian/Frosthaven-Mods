# Quartermaster (2nd edition) perks: the perk sheet "Valrath Quartermaster" (tools/gh2e/classes.json), 18 checks in all.
# gh_crossover's table format: (checkboxes, name, description in sprite markup, {perk fields}).
# A linked perk (two boxes joined on the sheet) is one perk with Available 1, Cost 2, as gh_crossover writes them.
# "Gain the <item>" modifiers are Frosthaven's own +0 "recover one spent or lost item" card (AMC_RefreshItem), the class's
# stand-in for its item supply (NOTES.md). Supplies are the class resource "Supplies" (resources.yml).

PERKS = [
    (2, "Muddling Strike", 'Replace two <sprite name="-1"> with one <sprite name="0"> <sprite name=Muddle>',
     {"CardsToRemove": "[AMC_Basic_M1, AMC_Basic_M1]", "CardsToAdd": "[AMC_GH2E_Quartermaster2_Muddle_P0]"}),
    (3, "Restock", 'Replace one <sprite name="0"> with one <sprite name="0"> "Gain 10 supplies"',
     {"CardsToRemove": "[AMC_Basic_P0]", "CardsToAdd": "[AMC_GH2E_Quartermaster2_Supplies10_P0]"}),
    (1, "Piercing Spears", 'Replace two <sprite name="0"> with two <sprite name=Pierce> 3 <sprite name=Rolling>',
     {"CardsToRemove": "[AMC_Basic_P0, AMC_Basic_P0]", "CardsToAdd": "[AMC_Pierce3_R, AMC_Pierce3_R]"}),
    # APPROXIMATION: "gain the Barbed Strip or Iron Plate item" = Frosthaven's +0 "recover one spent or lost item"
    (2, "Spare Armor", 'Replace one <sprite name="+1"> with two <sprite name="0"> "Gain the Barbed Strip or Iron Plate item"',
     {"CardsToRemove": "[AMC_Basic_P1]", "CardsToAdd": "[AMC_RefreshItem, AMC_RefreshItem]"}),
    (1, "Honed Edges", 'Replace two <sprite name="+1"> with two <sprite name="+2">',
     {"CardsToRemove": "[AMC_Basic_P1, AMC_Basic_P1]", "CardsToAdd": "[AMC_Basic_P2, AMC_Basic_P2]"}),
    # APPROXIMATION: the rolling "gain the Scroll of Relocation or Sharpened Dirk item" = a rolling recover-one-item card
    (2, "Quick Draw", 'Add one "Gain the Scroll of Relocation or Sharpened Dirk item" <sprite name=Rolling>',
     {"CardsToAdd": "[AMC_GH2E_Quartermaster2_RefreshItem_R]"}),
    (2, "Stunning Blow", 'Add one <sprite name=Stun> <sprite name=Rolling>',
     {"CardsToAdd": "[AMC_Stun_R]"}),
    (1, "Well Equipped", 'Ignore item <sprite name="-1"> effects and remove one <sprite name="0">',
     {"CardsToRemove": "[AMC_Basic_P0]", "Flags": "[Passive, IgnoreMinusOneItemEffects]"}),
    # NEEDS PLUGIN: Frosthaven's perk flags have an extra one-hand slot (AddOneAdditionalOneHandSlot) but no extra small
    # item. The perk can be bought and does nothing until the plugin does it (NOTES.md).
    (1, "Deep Pockets", 'You may bring one additional <sprite name=SmallItem> item into each scenario',
     {"Flags": "[Passive]"}),
    # NEEDS PLUGIN: a shop discount and a reputation bonus are campaign rules no perk field reaches (NOTES.md).
    (1, "Guild Connections", "Your party may purchase items from each faction as though the items' costs were five gold "
     "less and your reputation with that faction was two greater",
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

# The sheet's masteries ("spend 50 supplies to refresh 50 gold of lost items 3 times in one scenario"; "give yourself or
# an ally a class item each round of a scenario except when long resting") need the supply spending and the item supply
# the plugin doesn't have yet: none written (NOTES.md).
MASTERIES = []

NEW_CARDS = {
    # +0 Muddle (Frosthaven has the +0 Disarm one, AMC_Disarm_P0; its Muddle cards are rolling or +1 and up)
    "AMC_GH2E_Quartermaster2_Muddle_P0": ('MathModifier: "+0"\nAbilityOverrides:\n    NegCon:\n'
                                          '        ParentName: AMC_GH2E_Quartermaster2_Muddle_P0\n        Conditions: [Muddle]\n'),
    # +0, gain 10 supplies (the class resource): a Null ability whose end adds the supplies, as the cards' own gains
    "AMC_GH2E_Quartermaster2_Supplies10_P0": ('MathModifier: "+0"\nAbilities:\n    ASupplies:\n        Null: True\n'
                                              '        Filter: Self\n        AutoTrigger: True\n        SkipAnim: True\n'
                                              '        ResourcesToAddOnAbilityEnd:\n            - ["Supplies", 10]\n'),
    # rolling: recover one spent or lost item (Frosthaven's AMC_RefreshItem, rolling)
    "AMC_GH2E_Quartermaster2_RefreshItem_R": ('MathModifier: "+0"\nRolling: True\nAbilities:\n    RefreshItemsAbility:\n'
                                              '        RefreshItems: 1\n        SlotStateToRefresh: [Spent, Consumed]\n'),
}

STRINGS = {   # the new modifiers' names (the game looks a modifier's Name up as text)
    "AMC_GH2E_Quartermaster2_Muddle_P0": '<sprite name="0"> <sprite name=Muddle>',
    "AMC_GH2E_Quartermaster2_Supplies10_P0": '<sprite name="0"> Gain 10 supplies',
    "AMC_GH2E_Quartermaster2_RefreshItem_R": '<sprite name=Rolling> Recover one spent or lost item',
}
