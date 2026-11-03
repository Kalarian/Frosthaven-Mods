# Wildfury (2nd edition) perks: the perk sheet "Vermling Wildfury" (tools/gh2e/classes.json), 18 checks in all.
# gh_crossover's table format: (checkboxes, name, description in sprite markup, {perk fields}).
# A linked perk (two boxes joined on the sheet) is one perk with Available 1, Cost 2, as gh_crossover writes them.
# Decisions in NOTES.md.

# "Command" toggled once each scenario: gh_crossover._once_per_quest's text, with the bear as the target of a control
# (Frosthaven's AddActiveBonus ControlActor on an own summon, Infuser Boon of the Tempest) instead of Filter Self. The
# Bear is a companion: the filter must say Companion (an OwnSummon filter never matches a CompanionSummon, CAbilityFilter)
def _bear(indent):
    return "\n".join(" " * indent + ln for ln in (
        "Filter:", "    TargetType: [Companion]", "    HeroSummonClasses: [WildfuryBearID]", "Target: 1",
        "Targeting: All", "IgnoreLOS: True", "SkipAnim: True"))

PERKS = [
    # a rolling Heal 2 aimed at the Bear (Frosthaven's AMC_Heal1_TBoneshaper_R custom targeting: the summoner there,
    # the Bear here; when the Bear is gone or draws the card itself, it heals the attacker)
    (3, "Lick Wounds", 'Replace one <sprite name="-1"> with one "<sprite name=Heal> 2, <sprite name=Target> Bear" '
     '<sprite name=Rolling>',
     {"CardsToRemove": "[AMC_Basic_M1]", "CardsToAdd": "[AMC_GH2E_Wildfury_Heal2Bear_R]"}),
    (3, "Rending Claws", 'Replace one <sprite name="0"> with one <sprite name="+1"> <sprite name=Wound>',
     {"CardsToRemove": "[AMC_Basic_P0]", "CardsToAdd": "[AMC_Wound_P1]"}),
    # +2, and +1 more while the Wildfury and the Bear are within range 4 of each other: a plain "+2" card whose +1 the
    # plugin adds at the game's modifier rewrite step (GHWildfury.cs PackBond, as GHModifierRewrites' Follow-up Care:
    # +2 drawn -> +3), reading the drawer or its summoner in C# (the old expression override read TargetingActor, null
    # when the Bear draws the card: the game's error screen, 2e sweep 2026-11-01)
    (2, "Pack Bond", 'Replace one <sprite name="0"> with one <sprite name="+2"> "Add +1 <sprite name=Attack> if the '
     'Wildfury and the Bear are within <sprite name=Range> 4 of each other"',
     {"CardsToRemove": "[AMC_Basic_P0]", "CardsToAdd": "[AMC_GH2E_Wildfury_P2_Bond]"}),
    (1, "Savage Strike", 'Replace one <sprite name="+2"> with one <sprite name="+4">',
     {"CardsToRemove": "[AMC_Basic_P2]", "CardsToAdd": "[AMC_Basic_P4]"}),
    (2, "Primal Call", 'Add two <sprite name="+1"> <sprite name=CreateAir>/<sprite name=CreateEarth>',
     {"CardsToAdd": "[AMC_GH2E_Wildfury_AirEarth_P1, AMC_GH2E_Wildfury_AirEarth_P1]"}),
    (2, "Bowl Over", 'Add one <sprite name="+2"> <sprite name=Push> 2',
     {"CardsToAdd": "[AMC_GH2E_Wildfury_Push2_P2]"}),
    # "+1, Safeguard, target 1 ally": AMC_GH2E_Wildfury_P1_Safeguard (NEW_CARDS; the plugin runs its Safeguard)
    (1, "Warded Path", 'Ignore scenario effects and add one <sprite name="+1"> "Safeguard, <sprite name=Target> 1 '
     '<sprite name=Allies>"',
     {"CardsToAdd": "[AMC_GH2E_Wildfury_P1_Safeguard]", "Flags": "[Passive, IgnoreNegativeScenarioEffects]"}),
    # the plugin (GHWildfury.cs ThickHide) rewrites a 2x an enemy draws for an attack on the Bear to +0 at the game's
    # modifier rewrite step (Frosthaven's Crude Helmet does the same for its wearer); the perk itself is the switch
    (1, "Thick Hide", 'Whenever the Bear is attacked, treat any <sprite name="2x"> attack modifier card the enemy draws '
     'as <sprite name="0"> instead',
     {"Flags": "[Passive]"}),
    # once each scenario, during your turn: Command the Bear Move 4. The rider: the move is marked GHCallOfTheWild; when
    # it ends and the Bear exited a hex adjacent to her on the way, the plugin (GHWildfury.cs CallOfTheWild) stacks the
    # move's sub-ability for the Wildfury: an optional Teleport to an empty hex adjacent to the Bear (Spirit Swap top's
    # teleport). The sub-ability's requirement keeps the Bear itself from ever performing it.
    (1, "Call of the Wild", 'Once each scenario, during your turn, Command: <sprite name=Move> 4. If the Bear exits a hex '
     'adjacent to you during this movement, you may <sprite name=Teleport> to an empty hex adjacent to the Bear at the '
     'end of the movement',
     {"Flags": "[Passive, ApplyActiveBonusesAtScenarioStart]", "Abilities": f"""\
    TActive:
        AutoTrigger: True
        AddActiveBonus:
            ControlActor:
                ControlDurationType: ControlForOneAction
                Abilities:
                    ABearMove:
                        Move: 4
                        StrengthIsBase: True
                        MiscVars:
                            GHCallOfTheWild: True
                        SubAbilities:
                            ACallTeleport:
                                Teleport2: 99
                                Filter: Self
                                CanSkip: True
                                TreatAsNonSubAbility: True
                                StartAbilityRequirements:
                                    Type: ThisAbility
                                    Vars:
                                        ValidateWildfury: TargetingActor.Class.ID == "WildfuryID"
                                Vars:
                                    Bear: AllHeroSummons.Find(obj.Class.ID == "WildfuryBearID")
                                MiscVars:
                                    IsValidTeleportTile: Bear.GetDistanceTo(tile) == 1
                                    GHCallOfTheWildTeleport: True
                                AnimOverload: Teleport Into
                                AnimOverload2: Teleport Outof
{_bear(12)}
        ActiveBonus:
            Duration: Persistent
            Behaviour: DuringTurnAbility
            XPTracker: [0]
            IsToggleBonus: True
            Hidden: True
            ListLayoutOverride: [$PERK_Wildfury_09_Desc$, Positive_Move_Ally_Grant]
            MiscVars:
                OverrideIcon: "'Positive_Move_Ally_Grant'"
        AnimOverload: PowerUp"""}),
    # "At the start or end of each of your long rests": two hidden bonuses on the long rest, one before it (Frosthaven's
    # PreRestBonus flag, Boneshaper perk 10) and one after; once per rest (the plugin sets the Wildfury's temp var
    # GHLifeBondUsed when one is used and clears it when a long rest starts; each bonus requires it unset). Either
    # element: the element picker beside one Consume of [Air, Earth] (Restless Mind's shape). X: the marked step
    # GHLifeBond asks the player for X (1 up to her hit points - 1, the plugin's yes/no loop), reduces her current hit
    # points by X (not damage: no shield, no damage triggers) and sets the temp var GHLifeBondX, which the commanded Heal
    # reads (StatIsBasedOnX).
    (1, "Life Bond", 'At the start or end of each of your long rests, you may <sprite name=ConsumeAir>/<sprite '
     'name=ConsumeEarth> and reduce your current hit point value by X (X must be at least 1) to Command: <sprite '
     'name=Heal> X',
     {"Cost": 2, "Flags": "[Passive, ApplyActiveBonusesAtScenarioStart]", "Abilities": "\n".join(f"""\
    TLifeBond{when}:
        Filter: Self
        AutoTrigger: True
        SkipAnim: True
        AddActiveBonus:
            ShowElementPicker: True
            CanSkip: True
            CanUndo: False
            Consume:
                Elements: [Air, Earth]
                Abilities:
                    TLifeBondX{when}:
                        Null: True
                        AutoTrigger: True
                        CanUndo: False
                        MiscVars:
                            GHLifeBond: True
                    TLifeBondHeal{when}:
                        ControlActor:
                            ControlDurationType: ControlForOneAction
                            Abilities:
                                ABearHeal{when}:
                                    Heal: 1
                                    Filter: Self
                                    StatIsBasedOnX:
                                        Strength:
                                            BasedOn: Vars
                                            Vars:
                                                Value: TargetingActor.Summoner.GHLifeBondX
{_bear(24)}
        ActiveBonus:
            Duration: Persistent
            Behaviour: DuringActionAbilityOnLongRest
{flags}            Hidden: True
            Requirements:
                Vars:
                    ValidateUnused: Actor.GHLifeBondUsed != True""" for when, flags in (("Start", "            Flags: [PreRestBonus]\n"), ("End", "")))}),
]

# Mastery 1: the plugin's ElementEveryTurn tag. Mastery 2: the plugin's CommandKillsAtValue tag (GHWildfury.cs counts the
# kills made by the Bear's commanded attacks whose value before modifiers was Amount or more).
MASTERIES = [
    # mastery 1 of the sheet: the plugin's ElementEveryTurn tag (GHMasteries.cs, as the 1st edition Sunkeeper's mastery 2):
    # one of Elements infused or consumed on every turn the Wildfury takes.
    ("Elemental Bond", "In a single scenario, infuse or consume <sprite name=Air> or <sprite name=Earth> during each of your turns",
     """\
# the plugin's tag (GHMasteries.cs ElementEveryTurn): Air or Earth infused or consumed on every turn of the Wildfury
CheckAtEndOfScenario: True
BattleGoalCondition:
    - Targets:
        Filter: CountXRounds
        SubFilter: Scenario
        EnemyFamilies: [FrosthavenMods_ElementEveryTurn]
        Elements: [Air, Earth]
        CharacterIDs:
            ActorID: WildfuryID
        Value: 1"""),
    ("Unleashed", "In a single scenario, kill 4 or more enemies with <sprite name=Command> attacks of 7 or greater (after all bonuses, before attack modifiers)",
     """\
# the plugin's tag (GHMasteries.cs CommandKillsAtValue, counted by GHWildfury.cs): Value enemies killed by the Bear's
# commanded attacks whose value before attack modifiers was Amount or more
CheckAtEndOfScenario: True
BattleGoalCondition:
    - Targets:
        Filter: CountXRounds
        SubFilter: Scenario
        EnemyFamilies: [FrosthavenMods_CommandKillsAtValue]
        CharacterIDs:
            ActorID: WildfuryID
        Value: 4
        Amount: 7"""),
]

NEW_CARDS = {
    # Warded Path: "+1, Safeguard, target 1 ally": the pack's Safeguard shape (a Ward carrier tagged GH2ESafeguard,
    # GHSafeguard.cs), run after the attack by the plugin (GHModifierAbilities.cs; a Ward isn't a kind the game's
    # modifier code runs), by the Wildfury also when the Bear drew the card. No range printed: any distance (p21).
    "AMC_GH2E_Wildfury_P1_Safeguard": """\
MathModifier: "+1"
TryToPerformAsSummoner: True
Abilities:
    ASafeguard:
        Ward: True
        Filter: Ally
        Target: 1
        Targeting: Range
        Range: 99
        CanUndo: False
        AnimOverload: Heal
        MiscVars:
            GH2ESafeguard: True
""",
    "AMC_GH2E_Wildfury_Heal2Bear_R": ('MathModifier: "+0"\nRolling: True\nAbilities:\n    AHeal:\n        Heal: 2\n'
                                      '        Filter: [Self, Ally]\n        AutoTrigger: True\n        Targeting: Custom\n'
                                      '        TargetingVars:\n'
                                      '            AddValidActorInRange: AllHeroSummons.Find(obj.Class.ID == "WildfuryBearID") || TargetingActor\n'
                                      '            AddTileInRange: AddValidActorInRange.Tile\n'
                                      '            AddActorToTarget: AddValidActorInRange\n'
                                      '            AddSelectedTile: AddValidActorInRange.Tile\n'),
    # Pack Bond: a plain +2; the plugin makes it +3 while the Wildfury and the Bear are within range 4 (GHWildfury.cs)
    "AMC_GH2E_Wildfury_P2_Bond": 'MathModifier: "+2"\n',
    "AMC_GH2E_Wildfury_AirEarth_P1": ('MathModifier: "+1"\nAbilities:\n    InfuseAbility:\n        Infuse: [Air, Earth]\n'
                                      '        ShowElementPicker: True\n'),
    "AMC_GH2E_Wildfury_Push2_P2": 'MathModifier: "+2"\nAbilities:\n    APush:\n        Push: 2\n',
}

STRINGS = {}
