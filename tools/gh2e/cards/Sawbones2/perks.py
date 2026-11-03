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
    # "you may gain Disarm to add Stun": the plugin asks when the card is used in an attack (GHSawbones2e.Sedation;
    # NEW_CARDS, NOTES.md)
    (2, "Sedation", 'Replace two <sprite name="0"> with one <sprite name="+1"> "You may gain <sprite name=Disarm> to add <sprite name=Stun>"',
     {"CardsToRemove": "[AMC_Basic_P0, AMC_Basic_P0]", "CardsToAdd": "[AMC_GH2E_Sawbones2_DisarmStun_P1]"}),
    # the conditional +1 Attack: the plugin turns the card's +1 into +2 while an ally holds an active Prescription
    # (GHModifierRewrites.cs, 2026-11-02)
    (2, "Follow-up Care", 'Add two <sprite name="+1"> "Add +1 <sprite name=Attack> if an ally has an active Prescription"',
     {"CardsToAdd": "[AMC_GH2E_Sawbones2_FollowUp_P1, AMC_GH2E_Sawbones2_FollowUp_P1]"}),
    (2, "Field Dressing", 'Add one "<sprite name=Heal> 3, <sprite name=Range> 1" <sprite name=Rolling>',
     {"CardsToAdd": "[AMC_GH2E_Sawbones2_Heal3Range1_R]"}),
    # answered by the plugin (GHPerks.cs, perk ID Sawbones207): +1 on every heal of a Medical Pack (33230-33235)
    (1, "Potent Remedies", 'Add +1 <sprite name=Heal> to all heal abilities on "Medical Pack" ability cards',
     {"Cost": 2, "Flags": "[Passive]"}),
    # the FAQ (Devout): "works just as normal for the Temple, once it is built": in each outpost phase one Temple of the
    # Great Oak blessing for you or an ally costs no gold (Frosthaven's blessing cost; no refunds). Plugin GHSawbones2e
    # (perk ID Sawbones208)
    (1, "Friend of the Oak", "Each City Phase after completing a scenario, your or one ally's donation to the Sanctuary of the Great Oak is free (Frosthaven: one Temple blessing each outpost phase costs no gold)",
     {"Cost": 2, "Flags": "[Passive]"}),
    # after each of your rests (long and short), if an ally holds an active Prescription: you gain one Medical Pack (the
    # Sawbones gives it to herself, so the pack comes from her own supply), then she may move the token on one ally's
    # active Prescription back one slot (the plugin, GHSawbones2e, MiscVars GH2E_PrescriptionBackOne: MoveActiveBonusTracker
    # only moves the holder's own class cards)
    (1, "Restorative Rounds", 'At the end of each of your rests, if an ally has an active Prescription, gain one "Medical Pack" ability card and move the token on one ally\'s active Prescription backward one slot',
     {"Cost": 3, "Flags": "[Passive, ExecuteAbilitiesAfterLongRest, ExecuteAbilitiesAfterShortRest]", "Abilities": f"""\
    AGivePack:
        GiveSupplyCard: [33230]
        Filter:
            TargetType: Self
            ActorType: Player
        CanUndo: False
        AnimOverload: GivePackAdjacent
        MiscVars:
            GH2E_PrescriptionBackOne: True
        StartAbilityRequirements:
            Type: ThisAbility
            Vars:
                ValidateAllyPrescription: {_RX_ANY}"""}),
]

MASTERIES = [
    ("Constant Care", "In a single scenario, give an ally a medical pack or a Prescription each round except when performing a long rest",
     """\
# Answered by the Frosthaven Mods plugin (GHMasteries.cs, FrosthavenMods_PackOrPrescriptionEveryRound): in every round
# the Sawbones took a turn, except the rounds she long rested, she gave an ally a Medical Pack (GiveSupplyCard) or put
# a Prescription (an ability tagged GH2E_Prescription) on an ally.
CheckAtEndOfScenario: True
BattleGoalCondition:
    - Targets:
        Filter: CountXRounds
        SubFilter: Scenario
        EnemyFamilies: [FrosthavenMods_PackOrPrescriptionEveryRound]
        CharacterIDs:
            ActorID: Sawbones2ID
        Value: 1"""),
    ("Triage", "In a single scenario, never attack an undamaged enemy while there are any damaged monsters alive, and perform at least 10 attacks",
     """\
# Answered by the Frosthaven Mods plugin (GHMasteries.cs, FrosthavenMods_NoUndamagedWhileDamaged): Value attacks (one
# per target of an attack ability) and none of them on an enemy at full hit points while a damaged monster is alive
# (each attack is checked as it happens).
CheckAtEndOfScenario: True
BattleGoalCondition:
    - Targets:
        Filter: CountXRounds
        SubFilter: Scenario
        EnemyFamilies: [FrosthavenMods_NoUndamagedWhileDamaged]
        CharacterIDs:
            ActorID: Sawbones2ID
        Value: 10"""),
]

NEW_CARDS = {
    # A modifier card runs only the ability kinds the game's modifier code knows (a GiveSupplyCard or a Disarm on it is
    # the error screen when drawn). Both ride in a Choose, whose chosen option runs as an ordinary ability (Frosthaven's
    # AMC_PushOrPull2_R is a top-level Choose; AMC_ImproveTrapDamage2OrHeal2_P0's options are Null abilities); one
    # option each, which the player may skip.
    # Emergency Supplies: the GiveSupplyCard runs after the attack through the plugin (GHModifierAbilities.cs,
    # 2026-11-02), not in a Choose.
    "AMC_GH2E_Sawbones2_Pack_M2": """\
MathModifier: "-2"
Abilities:
    AGivePack:
        GiveSupplyCard: [33230]
        CanUndo: False
        HelpBoxTooltipLocKey: "GH2E_Sawbones2_PACK_HELP"
        Range: 1
        Targeting: Range
        Filter:
            TargetType: [Self, Ally]
            ActorType: Player
        AnimOverload: GivePackAdjacent
""",
    # "You may gain Disarm to add Stun": a +1 whose one ability is only a marker (a self NullTargeting with Disarm). A
    # Choose on a modifier stopped the game ("Invalid targeting type", 2026-11-01), so the plugin (GHSawbones2e.Sedation)
    # takes the marker out before the engine runs the card's abilities and asks the drawer: yes = the target gains Stun
    # and the Sawbones gains Disarm; no = nothing (only a card used in the attack gets there).
    "AMC_GH2E_Sawbones2_DisarmStun_P1": """\
MathModifier: "+1"
Abilities:
    ASedation:
        NullTargeting: True
        Conditions: [Disarm]
        Filter: Self
        CanUndo: False
        AutoTrigger: True
        HelpBoxTooltipLocKey: "GH2E_Sawbones2_SEDATION_HELP"
        MiscVars:
            GH2E_Sedation: True
""",
    # Follow-up Care: a +1 the plugin reads as +2 while an ally has an active Prescription (GHModifierRewrites.cs)
    "AMC_GH2E_Sawbones2_FollowUp_P1": 'MathModifier: "+1"' + chr(10),
    "AMC_GH2E_Sawbones2_Heal3Range1_R": ('MathModifier: "+0"\nRolling: True\nControlTargetingActor: True\nAbilities:\n'
                                         '    AHeal:\n        Heal: 3\n        Filter: [Self, Ally]\n        Targeting: Range\n'
                                         '        Range: 1\n        AnimOverload: Heal\n'),
}

STRINGS = {
    "GH2E_Sawbones2_PACK_HELP": "Give one adjacent ally or self one Medical Pack",
    "GH2E_Sawbones2_SEDATION_HELP": "You may gain <sprite name=Disarm> to add <sprite name=Stun>",
}
