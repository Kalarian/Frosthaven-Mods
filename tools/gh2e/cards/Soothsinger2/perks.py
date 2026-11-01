# Soothsinger (2nd edition) perks: the perk sheet "Quatryl Soothsinger" (tools/gh2e/classes.json, docs/gh2e/class-notes.md),
# 18 checks in all. gh_crossover's table format: (checkboxes, name, description in sprite markup, {perk fields}).
# Linked perks (two boxes joined on the sheet) are one perk with Available 1, Cost 2, as gh_crossover writes them.

PERKS = [
    (1, "Benediction", 'Replace one <sprite name="-2"> with one <sprite name="-2"> "<sprite name=Bless>, <sprite name=Target> 2"',
     {"CardsToRemove": "[AMC_Basic_M2]", "CardsToAdd": "[AMC_GH2E_Soothsinger2_Bless2_M2]"}),
    # skipping the song tokens has no Frosthaven ability (the songs' slots are TSongTimer's EndRoundAbility tracker):
    # a plain +0 here, the text as printed (NEEDS PLUGIN, NOTES.md)
    (3, "Fermata", 'Replace one <sprite name="-1"> with one <sprite name="0"> "Skip moving your character tokens on active Songs at the end of this round"',
     {"CardsToRemove": "[AMC_Basic_M1]", "CardsToAdd": "[AMC_Basic_P0]"}),
    (2, "Stunning Note", 'Replace one <sprite name="-1"> with one <sprite name="0"> <sprite name=Stun>',
     {"CardsToRemove": "[AMC_Basic_M1]", "CardsToAdd": "[AMC_Stun_P0]"}),
    # Frosthaven's own "+1, Strengthen one ally" card
    (2, "Rousing Chord", 'Replace two <sprite name="0"> with one <sprite name="+1"> "<sprite name=Strengthen>, <sprite name=Target> 1 ally"',
     {"CardsToRemove": "[AMC_Basic_P0, AMC_Basic_P0]", "CardsToAdd": "[AMC_Strengthen_Ally_P1]"}),
    (2, "Crescendo", 'Replace one <sprite name="0"> and one <sprite name="+1"> with two <sprite name="+2">',
     {"CardsToRemove": "[AMC_Basic_P0, AMC_Basic_P1]", "CardsToAdd": "[AMC_Basic_P2, AMC_Basic_P2]"}),
    # "Move 2 or Attack 2": one grant of both (the ally may skip either); a choice inside a modifier draw was judged too
    # risky unbenched (NOTES.md)
    (3, "Call and Response", 'Add one "Grant one ally: <sprite name=Move> 2 or <sprite name=Attack> 2" <sprite name=Rolling>',
     {"CardsToAdd": "[AMC_GH2E_Soothsinger2_GrantAlly_R]"}),
    (1, "Dirge", 'Add three <sprite name=Curse> <sprite name=Rolling>',
     {"CardsToAdd": "[AMC_Curse_R, AMC_Curse_R, AMC_Curse_R]"}),
    # the 1st edition's Storyteller, answered by the plugin there (GHPerks.cs); text only until the plugin knows this
    # class's perk ID (NOTES.md, shared changes)
    (1, "Storyteller", "At the end of each scenario, each character in that scenario gains 8 experience if you completed your battle goal",
     {"Flags": "[Passive]"}),
    # NEEDS PLUGIN: a Note on performing a Song (NOTES.md)
    (1, "Harmonics", "Whenever you perform a Song action, if you have any unused Notes, gain one Note of your choice in addition to any other Note gained that round",
     {"Flags": "[Passive]"}),
    # Frosthaven's long-rest trigger (ExecuteAbilitiesAfterLongRest, gh_crossover ELEMENTALIST_PERKS) with the Note choice;
    # the allies' -10 initiative is NEEDS PLUGIN (NOTES.md)
    (1, "Lullaby", "During ordering of initiative in rounds in which you declare a long rest, gain one Note of your choice, and each of your allies may decrease their initiative by 10",
     {"Cost": 2, "Flags": "[Passive, ExecuteAbilitiesAfterLongRest]", "Abilities": """\
    ANoteChoice:
        Filter: Self
        AutoTrigger: True
        CanSkip: False
        CanUndo: False
        PreviewEffectText: "High Note / Low Note"
        Choose:
            Abilities:
                ANoteHigh:
                    PreviewEffectText: "High Note"
                    NullTargeting: True
                    Filter: Self
                    AutoTrigger: True
                    SkipAnim: True
                    ResourcesToAddOnAbilityEnd:
                        - ["SoothsingerHighNote", 1]
                ANoteLow:
                    PreviewEffectText: "Low Note"
                    NullTargeting: True
                    Filter: Self
                    AutoTrigger: True
                    SkipAnim: True
                    ResourcesToAddOnAbilityEnd:
                        - ["SoothsingerLowNote", 1]"""}),
]

# Neither mastery fits an existing pattern: "perform 7 different Song actions" and "create 20+ Notes and use each in a
# Song" need plugin tags that count TSongNotes plays and the Note resources (NOTES.md, shared changes).
MASTERIES = []

NEW_CARDS = {
    # Frosthaven's ally-condition modifier shape (AMC_Strengthen_Ally_P1), Bless on two figures, you included. A Bless
    # ability on a modifier card is the error screen when drawn (the game's modifier code runs no condition abilities):
    # a NullTargeting carrying the condition, as Frosthaven's cards give conditions (NullTargeting + Conditions)
    "AMC_GH2E_Soothsinger2_Bless2_M2": ('MathModifier: "-2"\nControlTargetingActor: True\nAbilities:\n    ABless:\n'
                                        '        NullTargeting: True\n        Conditions: [Bless]\n'
                                        '        Filter: [Self, Ally]\n        Targeting: Range\n'
                                        '        Range: 99\n        Target: 2\n        CanUndo: False\n'),
    # Frosthaven's grant-in-a-modifier (AMC_P0_ControlSummonMove2), on an ally, Move 2 then Attack 2
    "AMC_GH2E_Soothsinger2_GrantAlly_R": ('MathModifier: "+0"\nRolling: True\nControlTargetingActor: True\nAbilities:\n'
                                          '    ControlAbility:\n        ControlActor:\n            Abilities:\n'
                                          '                AAllyMove:\n                    Move: 2\n                    StrengthIsBase: True\n'
                                          '                AAllyAttack:\n                    Attack: 2\n                    Range: 1\n'
                                          '                    StrengthIsBase: True\n                    RangeIsBase: True\n'
                                          '        Range: 99\n        Filter: Ally\n        AnimOverload: Grant\n'),
}

STRINGS = {}
