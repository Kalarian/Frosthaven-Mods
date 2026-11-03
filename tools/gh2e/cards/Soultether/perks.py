# Soultether (2nd edition) perks: the perk sheet "Aesther Soultether" (tools/gh2e/classes.json), 18 checks in all.
# gh_crossover's table format: (checkboxes, name, description in sprite markup, {perk fields}).
# A linked perk (two boxes joined on the sheet) is one perk with Available 1, Cost 2, as gh_crossover writes them.
# Decisions in NOTES.md.


def _once_per_scenario_summon(desc_key, icon, inner):
    """gh_crossover._once_per_quest (Frosthaven's once-per-scenario toggle: a persistent DuringTurnAbility bonus with a
    single use), with the bonus's ability aimed at one of your summons instead of yourself (that helper always adds
    Filter: [Self])."""
    return f"""\
    TActive:
        AutoTrigger: True
        AddActiveBonus:
{inner}
        ActiveBonus:
            Duration: Persistent
            Behaviour: DuringTurnAbility
            XPTracker: [0]
            IsToggleBonus: True
            Hidden: True
            ListLayoutOverride: [${desc_key}$, {icon}]
            MiscVars:
                OverrideIcon: "'{icon}'"
        AnimOverload: PowerUp"""


PERKS = [
    (1, "Composure", 'Replace one <sprite name="-2"> with one <sprite name="0">',
     {"CardsToRemove": "[AMC_Basic_M2]", "CardsToAdd": "[AMC_Basic_P0]"}),
    # Frosthaven's own summon-command modifier (AMC_P0_ControlSummonMove2: TryToPerformAsSummoner, a control on one of
    # your summons) with the grant of Teleport 2 (Teleport2, as HIVE Recall grants it)
    (3, "Planar Shift", 'Replace one <sprite name="-1"> with one <sprite name="0"> "After the attack ability, grant one '
     'of your summons: <sprite name=Teleport> 2"',
     {"CardsToRemove": "[AMC_Basic_M1]", "CardsToAdd": "[AMC_GH2E_Soultether_P0_GrantTeleport2]"}),
    # Frosthaven's AMC_Strengthen_Ally_P1 shape (a condition on another figure from a modifier), Invisible on one of
    # your summons, performed by you when a summon draws it (TryToPerformAsSummoner). The Invisible is a NullTargeting
    # carrying the condition (an Invisible ability on a modifier card is the error screen when drawn)
    (3, "Veiled Servant", 'Replace one <sprite name="0"> with one <sprite name="+1"> "<sprite name=Invisible>, '
     '<sprite name=Target> 1 of your summons"',
     {"CardsToRemove": "[AMC_Basic_P0]", "CardsToAdd": "[AMC_GH2E_Soultether_P1_InvisibleSummon]"}),
    (1, "Rending Summons", 'Replace one <sprite name="0"> with two <sprite name="0"> <sprite name=Wound> <sprite name=Rolling>',
     {"CardsToRemove": "[AMC_Basic_P0]", "CardsToAdd": "[AMC_Wound_R, AMC_Wound_R]"}),
    (1, "Venomous Summons", 'Replace one <sprite name="0"> with two <sprite name="0"> <sprite name=Poison> <sprite name=Rolling>',
     {"CardsToRemove": "[AMC_Basic_P0]", "CardsToAdd": "[AMC_Poison_R, AMC_Poison_R]"}),
    (1, "Hexing Strikes", 'Replace two <sprite name="+1"> with two <sprite name="+1"> <sprite name=Curse>',
     {"CardsToRemove": "[AMC_Basic_P1, AMC_Basic_P1]", "CardsToAdd": "[AMC_Curse_P1, AMC_Curse_P1]"}),
    # one of two elements: Frosthaven's element-picker modifiers (AMC_FireAir_P1, AMC_Air_Dark_P0) at +2
    (1, "Blazing Gale", 'Add one <sprite name="+2"> <sprite name=CreateFire>/<sprite name=CreateAir>',
     {"CardsToAdd": "[AMC_GH2E_Soultether_FireAir_P2]"}),
    (1, "Smoldering Shadow", 'Add one <sprite name="+2"> <sprite name=CreateFire>/<sprite name=CreateDark>',
     {"CardsToAdd": "[AMC_GH2E_Soultether_FireDark_P2]"}),
    (1, "Night Wind", 'Add one <sprite name="+2"> <sprite name=CreateAir>/<sprite name=CreateDark>',
     {"CardsToAdd": "[AMC_GH2E_Soultether_AirDark_P2]"}),
    (1, "Unfazed", 'Ignore scenario effects and add two <sprite name="+1">',
     {"CardsToAdd": "[AMC_Basic_P1, AMC_Basic_P1]", "Flags": "[Passive, IgnoreNegativeScenarioEffects]"}),
    # Frosthaven's long-rest round-start hook (HIVE perk 8: PreCalcInitiativeAbility with Requirements IsLongRest, which
    # runs once the cards are chosen, before the initiative order): Shield 1 for the round on every one of your summons
    (1, "Restful Guard", 'At the beginning of each round in which you long rest, all your summons gain <sprite name=Shield> 1 for the round',
     {"Flags": "[Passive, ApplyActiveBonusesAtScenarioStart]", "Abilities": """\
    OnChooseToLongRest:
        Filter: Self
        AutoTrigger: True
        SkipAnim: True
        AddActiveBonus:
            Shield: 1
            Range: 99
            Target: -1
            Targeting: All
            IgnoreLOS: True
            Filter: OwnSummon
            AutoTrigger: True
            CanUndo: False
            ActiveBonus:
                Duration: Round
            AnimOverload: RadialBuff
        ActiveBonus:
            Duration: Persistent
            Behaviour: PreCalcInitiativeAbility
            Hidden: True
            Requirements:
                IsLongRest: True"""}),
    # the once-per-scenario toggle with Frosthaven's HIVE Recall bottom grant (one of your summons: Teleport2 limited to
    # the hexes adjacent to you, IsValidTeleportTile)
    (1, "Summoner's Call", 'Once each scenario, during your turn, <sprite name=Teleport> one of your summons to an empty hex adjacent to you',
     {"Flags": "[Passive, ApplyActiveBonusesAtScenarioStart]",
      "Abilities": _once_per_scenario_summon("PERK_Soultether_12_Desc", "Positive_Summon", """\
            Range: 99
            Target: 1
            Filter: OwnSummon
            Vars:
                ParentCaster: TargetingActor
            ControlActor:
                Abilities:
                    ASummonTeleport:
                        Teleport2: 99
                        StrengthIsBase: True
                        Filter: Self
                        MiscVars:
                            IsValidTeleportTile: ParentCaster.GetDistanceTo(tile) == 1
                        AnimOverload: Teleport Into
                        AnimOverload2: Teleport Outof
            AnimOverload: Grant""")}),
    # two linked boxes. A once-a-scenario choice offered each round "after all ability cards have been revealed":
    # Frosthaven's GenericAfterRevealMonsterCards (Infuser perk 11: offered once the monster cards are revealed,
    # OncePerRound, finished only when taken; the single slot ends it): Invisible on all your summons.
    (1, "Phase Out", 'Once each scenario, during ordering of initiative, after all ability cards have been revealed, all '
     'your summons gain <sprite name=Invisible>',
     {"Cost": 2, "Flags": "[Passive, ApplyActiveBonusesAtScenarioStart]", "Abilities": """\
    TActive:
        Filter: Self
        AutoTrigger: True
        SkipAnim: True
        AddActiveBonus:
            Invisible: True
            Range: 99
            Target: -1
            Targeting: All
            IgnoreLOS: True
            Filter: OwnSummon
            CanSkip: True
            CanUndo: False
            AnimOverload: RadialBuff
        ActiveBonus:
            Duration: Persistent
            Behaviour: GenericAfterRevealMonsterCards
            Restriction: OncePerRound
            Hidden: True
            XPTracker: [0]"""}),
]

# Both 2e masteries are answered by the plugin (GHSoultether.cs): the first from the ability records and the active
# area at the check, the second with a career count kept in the campaign save.
MASTERIES = [
    ("Bound Legion",
     "Perform three different summon actions before your first rest and keep those cards in your active area for the entire scenario",
     """\
# Answered by the Frosthaven Mods plugin (GHSoultether.cs, FrosthavenMods_SummonCardsKeptBeforeRest): Value different
# ability cards whose summon ability the Soultether performed before the round of its first rest, and every one of
# them still in its active area when the scenario ends (a summon card leaves the active area only when its summon is
# gone, so one that left can't have stayed all scenario).
CheckAtEndOfScenario: True
BattleGoalCondition:
    - Targets:
        Filter: CountXRounds
        SubFilter: Scenario
        EnemyFamilies: [FrosthavenMods_SummonCardsKeptBeforeRest]
        CharacterIDs:
            ActorID: SoultetherID
        Value: 3"""),
    ("Planar Conclave",
     "Perform six different summon abilities from your ability cards over the course of the first rounds of six scenarios",
     """\
# Answered by the Frosthaven Mods plugin (GHSoultether.cs, FrosthavenMods_FirstRoundSummonsAcrossScenarios): the summon
# abilities (card + ability) the Soultether performed in round 1 of each scenario are added to a career list kept in
# the campaign save (synced campaign state, written from the scenario result every peer receives); the mastery is met
# when the list holds Value different ones (two summon cards can be played in one first round; the scenarios need not
# be in a row).
CheckAtEndOfScenario: True
BattleGoalCondition:
    - Targets:
        Filter: CountXRounds
        SubFilter: Scenario
        EnemyFamilies: [FrosthavenMods_FirstRoundSummonsAcrossScenarios]
        CharacterIDs:
            ActorID: SoultetherID
        Value: 6"""),
]

NEW_CARDS = {
    "AMC_GH2E_Soultether_P0_GrantTeleport2": (
        'MathModifier: "+0"\n'
        'TryToPerformAsSummoner: True\n'
        'Abilities:\n'
        '    ControlAbility:\n'
        '        ControlActor:\n'
        '            Abilities:\n'
        '                BSummonTeleport:\n'
        '                    Teleport2: 2\n'
        '                    StrengthIsBase: True\n'
        '                    Filter: Self\n'
        '                    AnimOverload: Teleport Into\n'
        '                    AnimOverload2: Teleport Outof\n'
        '        Range: 99\n'
        '        Filter: OwnSummon\n'
        '        AnimOverload: Grant\n'),
    "AMC_GH2E_Soultether_P1_InvisibleSummon": (
        'MathModifier: "+1"\n'
        'TryToPerformAsSummoner: True\n'
        'Abilities:\n'
        '    AInvisible:\n'
        '        NullTargeting: True\n'
        '        Conditions: [Invisible]\n'
        '        Filter: OwnSummon\n'
        '        Targeting: Range\n'
        '        Range: 99\n'
        '        CanUndo: False\n'),
    "AMC_GH2E_Soultether_FireAir_P2": (
        'MathModifier: "+2"\nAbilities:\n    InfuseAbility:\n        Infuse: [Fire, Air]\n        ShowElementPicker: True\n'
        '        CanUndo: False\n'),
    "AMC_GH2E_Soultether_FireDark_P2": (
        'MathModifier: "+2"\nAbilities:\n    InfuseAbility:\n        Infuse: [Fire, Dark]\n        ShowElementPicker: True\n'
        '        CanUndo: False\n'),
    "AMC_GH2E_Soultether_AirDark_P2": (
        'MathModifier: "+2"\nAbilities:\n    InfuseAbility:\n        Infuse: [Air, Dark]\n        ShowElementPicker: True\n'
        '        CanUndo: False\n'),
}

STRINGS = {}
