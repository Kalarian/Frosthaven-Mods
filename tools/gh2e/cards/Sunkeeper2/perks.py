# Sunkeeper (2nd edition) perks: the perk sheet "Valrath Sunkeeper" (tools/gh2e/classes.json, docs/gh2e/class-notes.md),
# 18 checks in all. gh_crossover's table format: (checkboxes, name, description in sprite markup, {perk fields}).
# Linked perks (two boxes joined on the sheet) are one perk with Available 1, Cost 2, as gh_crossover writes them.

PERKS = [
    (2, "Radiance", 'Replace one <sprite name="-1"> with two <sprite name="0"> <sprite name=CreateLight>',
     {"CardsToRemove": "[AMC_Basic_M1]", "CardsToAdd": "[AMC_GH2E_Sunkeeper2_Light_P0, AMC_GH2E_Sunkeeper2_Light_P0]"}),
    (3, "Bulwark", 'Replace one <sprite name="0"> with one <sprite name="+1"> <sprite name=Shield> 1 <sprite name=Rolling>',
     {"CardsToRemove": "[AMC_Basic_P0]", "CardsToAdd": "[AMC_Shield1_P1_R]"}),
    (2, "Smite", 'Replace one <sprite name="0"> with one <sprite name="+2">',
     {"CardsToRemove": "[AMC_Basic_P0]", "CardsToAdd": "[AMC_Basic_P2]"}),
    (2, "Mending Light", 'Add two <sprite name="+1"> "<sprite name=Heal> 1, <sprite name=Range> 3"',
     {"CardsToAdd": "[AMC_GH2E_Sunkeeper2_Heal1Range3_P1, AMC_GH2E_Sunkeeper2_Heal1Range3_P1]"}),
    (1, "Second Wind", 'Add one <sprite name="-1"> "You or an adjacent ally may <sprite name=RecoverIcon> one level 1 card from the discard pile"',
     {"CardsToAdd": "[AMC_GH2E_Sunkeeper2_Recover1_M1]"}),
    (1, "Dazzle", 'Add one <sprite name="0"> <sprite name=Stun> <sprite name=Rolling>',
     {"CardsToAdd": "[AMC_Stun_R]"}),
    (1, "Unfazed", 'Ignore scenario effects and add one <sprite name="+1">',
     {"CardsToAdd": "[AMC_Basic_P1]", "Flags": "[Passive, IgnoreNegativeScenarioEffects]"}),
    (1, "Well Armored", 'Ignore item <sprite name="-1"> effects and remove one <sprite name="-1">',
     {"CardsToRemove": "[AMC_Basic_M1]", "Flags": "[Passive, IgnoreMinusOneItemEffects]"}),
    # Frosthaven's room-revealed perk (Perk_Snowdancer_09): it fires whenever a room is revealed, by anyone; the sheet's
    # "whenever you open one or more doors during your turn" can't tell who opened it (noted in NOTES.md)
    (1, "Vigilant Entry", "Whenever you open one or more doors during your turn, gain <sprite name=Shield> 1, <sprite name=Retaliate> 1 for the round",
     {"Cost": 2, "Flags": "[Passive, ExecuteAbilitiesAfterRoomRevealed, ApplyActiveBonusesAtScenarioStart]", "Abilities": """\
    AShield:
        Shield: 1
        Filter: Self
        AutoTrigger: True
        SkipAnim: True
        ActiveBonus:
            Duration: Round
    ARetaliate:
        Retaliate: 1
        Filter: Self
        AutoTrigger: True
        SkipAnim: True
        ActiveBonus:
            Duration: Round"""}),
    # the Elementalist's long-rest infusion (gh_crossover ELEMENTALIST_PERKS), Light only
    (1, "Restful Dawn", "Whenever you long rest, you may <sprite name=CreateLight>",
     {"Flags": "[Passive, ExecuteAbilitiesAfterLongRest]", "Abilities": """\
    BInfuse:
        Infuse: [Light]
        Filter: Self
        AutoTrigger: True
        SkipAnim: True
        CanUndo: False"""}),
    # the 1st edition's Shielding Light, answered by the plugin there (GHPerks.cs, perk ID Sunkeeper10); this one is
    # Sunkeeper211: text only until the plugin knows that ID (NOTES.md, shared changes)
    (1, "Shielding Light", "Whenever one of your heals would cause an ally's current hit point value to increase beyond their maximum hit point value, that ally gains <sprite name=Ward>",
     {"Cost": 2, "Flags": "[Passive]"}),
]

MASTERIES = [
    ("Child of the Sun", "In a single scenario, infuse or consume <sprite name=CreateLight> during each of your turns",
     """\
# The 1st edition crossover's Child of the Sun: Light infused or consumed on every turn the Sunkeeper takes, answered by
# the Frosthaven Mods plugin (GHMasteries.cs) from the element records.
CheckAtEndOfScenario: True
BattleGoalCondition:
    - Targets:
        Filter: CountXRounds
        SubFilter: Scenario
        EnemyFamilies: [FrosthavenMods_ElementEveryTurn]
        Elements: Light
        CharacterIDs:
            ActorID: Sunkeeper2ID
        Value: 1"""),
    ("Hammer of Dawn", "In a single scenario, deal <sprite name=Damage> 8 or more with an attack 3 or more times",
     """\
# The plugin's attack-action tag (GHMasteries.cs, the Demolitionist's Heavy Hitter): Value attack actions each dealing
# Amount damage. It counts different card halves, where the sheet counts any three attacks (NOTES.md).
BattleGoalCondition:
    - Targets:
        Filter: CountXRounds
        SubFilter: Scenario
        EnemyFamilies: [FrosthavenMods_AttackActionsWithDamage]
        CharacterIDs:
            ActorID: Sunkeeper2ID
        Value: 3
        Amount: 8"""),
]

NEW_CARDS = {
    "AMC_GH2E_Sunkeeper2_Light_P0": 'MathModifier: "+0"\nAbilities:\n    InfuseAbility:\n        Infuse: [Light]\n',
    "AMC_GH2E_Sunkeeper2_Heal1Range3_P1": ('MathModifier: "+1"\nControlTargetingActor: True\nAbilities:\n    AHeal:\n'
                                           '        Heal: 1\n        Filter: [Self, Ally]\n        Targeting: Range\n'
                                           '        Range: 3\n        AnimOverload: Heal\n'),
    # "You or an adjacent ally may recover one level 1 card": a RecoverDiscardedCards on a modifier card is the error
    # screen when drawn (the game's modifier code doesn't run it), so it rides in a Choose, whose chosen option runs as
    # an ordinary ability (Frosthaven's AMC_PushOrPull2_R is a top-level Choose; AMC_ImproveTrapDamage2OrHeal2_P0's
    # options are Null abilities). One option, which the player may skip: the printed "may".
    "AMC_GH2E_Sunkeeper2_Recover1_M1": """\
MathModifier: "-1"
Abilities:
    AChooseRecover:
        Filter: Self
        CanUndo: False
        HelpBoxTooltipLocKey: "GH2E_Sunkeeper2_RECOVER_HELP"
        PreviewEffectText: "<sprite name=RecoverIcon> 1"
        Choose:
            Abilities:
                ARecover:
                    RecoverDiscardedCards: 1
                    CanUndo: False
                    PreviewEffectText: "<sprite name=RecoverIcon> 1"
                    Range: 1
                    Filter:
                        TargetType: [Self, Ally]
                        ActorType: Player
                    Vars:
                        CardLevel: 1
                    AnimOverload: PositiveBuffTargeted
""",
}

STRINGS = {
    "GH2E_Sunkeeper2_RECOVER_HELP": "You or an adjacent ally may recover one level 1 card from the discard pile",
}
