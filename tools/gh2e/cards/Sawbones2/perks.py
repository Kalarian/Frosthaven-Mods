# Sawbones (2nd edition) perks: the perk sheet "Human Sawbones" (tools/gh2e/classes.json, docs/gh2e/class-notes.md),
# 18 checks in all. gh_crossover's table format: (checkboxes, name, description in sprite markup, {perk fields}).
# Linked perks (boxes joined on the sheet) are one perk with Available 1 and Cost = the boxes, as gh_crossover writes them.
# Medical Pack card IDs here are the converted (Frosthaven) IDs: 13230 + 20000 (perk files are written as they are).

# an ally holds an active Prescription (the class's Prescription abilities carry MiscVars GH2E_Prescription)
_RX_ANY = ('ScenarioManager.Scenario.PlayerActors.Any(obj.ActiveBonuses.Any('
           'CustomAbility.GetMiscVarBool(obj.Ability, "GH2E_Prescription")))')

PERKS = [
    (1, "Steady Hands", 'Remove two <sprite name="-1">',
     {"CardsToRemove": "[AMC_Basic_M1, AMC_Basic_M1]"}),
    (1, "Emergency Supplies", 'Replace one <sprite name="-2"> with one <sprite name="-2"> "Give one adjacent ally or self one Medical Pack"',
     {"CardsToRemove": "[AMC_Basic_M2]", "CardsToAdd": "[AMC_GH2E_Sawbones2_Pack_M2]"}),
    (3, "Precise Incisions", 'Replace one <sprite name="-1"> with one <sprite name="+1">',
     {"CardsToRemove": "[AMC_Basic_M1]", "CardsToAdd": "[AMC_Basic_P1]"}),
    # "you may gain Disarm to add Stun": the choice can't be offered on a modifier; the card always stuns and always
    # disarms you (NOTES.md)
    (2, "Sedation", 'Replace two <sprite name="0"> with one <sprite name="+1"> "You may gain <sprite name=Disarm> to add <sprite name=Stun>"',
     {"CardsToRemove": "[AMC_Basic_P0, AMC_Basic_P0]", "CardsToAdd": "[AMC_GH2E_Sawbones2_DisarmStun_P1]"}),
    # the conditional +1 Attack needs the plugin (NOTES.md): plain +1 cards until then
    (2, "Follow-up Care", 'Add two <sprite name="+1"> "Add +1 <sprite name=Attack> if an ally has an active Prescription"',
     {"CardsToAdd": "[AMC_Basic_P1, AMC_Basic_P1]"}),
    (2, "Field Dressing", 'Add one "<sprite name=Heal> 3, <sprite name=Range> 1" <sprite name=Rolling>',
     {"CardsToAdd": "[AMC_GH2E_Sawbones2_Heal3Range1_R]"}),
    # text only until the plugin knows "a heal on a Medical Pack card" (NOTES.md)
    (1, "Potent Remedies", 'Add +1 <sprite name=Heal> to all heal abilities on "Medical Pack" ability cards',
     {"Cost": 2, "Flags": "[Passive]"}),
    # Frosthaven has no Sanctuary of the Great Oak donation in its outpost phase: text only (NOTES.md)
    (1, "Friend of the Oak", "Each City Phase after completing a scenario, your or one ally's donation to the Sanctuary of the Great Oak is free",
     {"Cost": 2, "Flags": "[Passive]"}),
    # after each of your rests (long and short), if an ally holds an active Prescription: you gain one Medical Pack (the
    # Sawbones gives it to herself, so the pack comes from her own supply). Moving a Prescription's token back needs
    # the plugin (MoveActiveBonusTracker only moves the holder's own class cards; NOTES.md)
    (1, "Restorative Rounds", 'At the end of each of your rests, if an ally has an active Prescription, gain one "Medical Pack" ability card and move the token on one ally\'s active Prescription backward one slot',
     {"Cost": 3, "Flags": "[Passive, ExecuteAbilitiesAfterLongRest, ExecuteAbilitiesAfterShortRest]", "Abilities": f"""\
    AGivePack:
        GiveSupplyCard: [33230]
        Filter:
            TargetType: Self
            ActorType: Player
        CanUndo: False
        AnimOverload: GivePackAdjacent
        StartAbilityRequirements:
            Type: ThisAbility
            Vars:
                ValidateAllyPrescription: {_RX_ANY}"""}),
]

MASTERIES = [
    ("Constant Care", "In a single scenario, give an ally a medical pack or a Prescription each round except when performing a long rest",
     """\
# The 1st edition crossover's Bedside Manner tag: an ally helped on every turn the Sawbones takes (a heal, shield,
# supply card, buff or recovery with an ally among its targets), answered by the Frosthaven Mods plugin (GHMasteries.cs).
# Broader than the sheet (any ally help counts, not only packs and Prescriptions), and long-rest rounds are not exempt
# unless the plugin skips them (NOTES.md).
CheckAtEndOfScenario: True
BattleGoalCondition:
    - Targets:
        Filter: CountXRounds
        SubFilter: Scenario
        EnemyFamilies: [FrosthavenMods_AllySupportEveryTurn]
        CharacterIDs:
            ActorID: Sawbones2ID
        Value: 1"""),
]

NEW_CARDS = {
    "AMC_GH2E_Sawbones2_Pack_M2": ('MathModifier: "-2"\nControlTargetingActor: True\nAbilities:\n    AGivePack:\n'
                                   '        GiveSupplyCard: [33230]\n        Range: 1\n        Targeting: Range\n'
                                   '        Filter:\n            TargetType: [Self, Ally]\n            ActorType: Player\n'
                                   '        AnimOverload: GivePackAdjacent\n'),
    "AMC_GH2E_Sawbones2_DisarmStun_P1": ('MathModifier: "+1"\nAbilityOverrides:\n    NegCon:\n'
                                         '        ParentName: AMC_GH2E_Sawbones2_DisarmStun_P1\n        Conditions: [Stun]\n'
                                         'Abilities:\n    ADisarmSelf:\n        Disarm: True\n        Filter: Self\n'
                                         '        AnimOverload: SelfDebuff\n'),
    "AMC_GH2E_Sawbones2_Heal3Range1_R": ('MathModifier: "+0"\nRolling: True\nControlTargetingActor: True\nAbilities:\n'
                                         '    AHeal:\n        Heal: 3\n        Filter: [Self, Ally]\n        Targeting: Range\n'
                                         '        Range: 1\n        AnimOverload: Heal\n'),
}

STRINGS = {}
