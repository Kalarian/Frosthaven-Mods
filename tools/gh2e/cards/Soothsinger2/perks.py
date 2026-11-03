# Soothsinger (2nd edition) perks: the perk sheet "Quatryl Soothsinger" (tools/gh2e/classes.json, docs/gh2e/class-notes.md),
# 18 checks in all. gh_crossover's table format: (checkboxes, name, description in sprite markup, {perk fields}).
# Linked perks (two boxes joined on the sheet) are one perk with Available 1, Cost 2, as gh_crossover writes them.

PERKS = [
    (1, "Benediction", 'Replace one <sprite name="-2"> with one <sprite name="-2"> "<sprite name=Bless>, <sprite name=Target> 2"',
     {"CardsToRemove": "[AMC_Basic_M2]", "CardsToAdd": "[AMC_GH2E_Soothsinger2_Bless2_M2]"}),
    # a +0 whose marker ability (MiscVars GH2E_SkipSongTokens) the plugin reads when the card is drawn: every active
    # song's token stays where it is at the end of this round (GHSoothsinger; all or nothing, GH2e FAQ)
    (3, "Fermata", 'Replace one <sprite name="-1"> with one <sprite name="0"> "Skip moving your character tokens on active Songs at the end of this round"',
     {"CardsToRemove": "[AMC_Basic_M1]", "CardsToAdd": "[AMC_GH2E_Soothsinger2_Fermata_P0]"}),
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
    # the plugin (GHSoothsinger): at the scenario's end, if this character's battle goal is complete, 8 XP to every
    # character in the scenario
    (1, "Storyteller", "At the end of each scenario, each character in that scenario gains 8 experience if you completed your battle goal",
     {"Flags": "[Passive]"}),
    # the plugin (GHSoothsinger): when a Song removes Notes beyond its cost, a Note of your choice (no round limit)
    (1, "Harmonics", "Whenever you perform a Song action, if you have any unused Notes, gain one Note of your choice in addition to any other Note gained that round",
     {"Flags": "[Passive]"}),
    # the plugin (GHSoothsinger): when initiative is ordered in a round this character long rests, a Note of your choice
    # and each ally character may lower their initiative by 10 (the engine's own initiative bonus, this round)
    (1, "Lullaby", "During ordering of initiative in rounds in which you declare a long rest, gain one Note of your choice, and each of your allies may decrease their initiative by 10",
     {"Cost": 2, "Flags": "[Passive]"}),
]

MASTERIES = [
    ("Medley", "In a single scenario, perform 7 different Song actions",
     """# The plugin's distinct-song count (GHSoothsinger.cs, FrosthavenMods_SSDistinctSongs): Value different Song actions
# (cards whose song top was performed, its TSongNotes) in the scenario.
BattleGoalCondition:
    - Targets:
        Filter: CountXRounds
        SubFilter: Scenario
        EnemyFamilies: [FrosthavenMods_SSDistinctSongs]
        CharacterIDs:
            ActorID: Soothsinger2ID
        Value: 7"""),
    ("Perfect Pitch", "In a single scenario, create 20 or more Notes and use each Note you create in a Song",
     """# The plugin's Note ledger (GHSoothsinger.cs, FrosthavenMods_SSNotesAllUsed): Value Notes created (resource gains that
# placed a Note) and none wasted: a Note a Song removes counts as used only when it paid that Song's printed cost (the
# approved default), and no Note may be left at the scenario's end.
CheckAtEndOfScenario: True
BattleGoalCondition:
    - Targets:
        Filter: CountXRounds
        SubFilter: Scenario
        EnemyFamilies: [FrosthavenMods_SSNotesAllUsed]
        CharacterIDs:
            ActorID: Soothsinger2ID
        Value: 20"""),
]

NEW_CARDS = {
    # Fermata: a +0 with a self NullTargeting marker (Frosthaven's AMC_Resonance_P1 / the Quartermaster's Supplies10
    # shape); the plugin (GHSoothsinger) sees the marker when the card applies and holds the song tokens this round
    "AMC_GH2E_Soothsinger2_Fermata_P0": """\
MathModifier: "+0"
Abilities:
    AFermata:
        NullTargeting: True
        Filter: Self
        AutoTrigger: True
        SkipAnim: True
        MiscVars:
            GH2E_SkipSongTokens: True
""",
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

STRINGS = {   # the new modifiers' names (the game looks a modifier's Name up as text)
    "AMC_GH2E_Soothsinger2_Fermata_P0": '<sprite name="0"> Skip moving your character tokens on active Songs at the end of this round',
}
