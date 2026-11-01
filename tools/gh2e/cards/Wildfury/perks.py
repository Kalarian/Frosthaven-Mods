# Wildfury (2nd edition) perks: the perk sheet "Vermling Wildfury" (tools/gh2e/classes.json), 18 checks in all.
# gh_crossover's table format: (checkboxes, name, description in sprite markup, {perk fields}).
# A linked perk (two boxes joined on the sheet) is one perk with Available 1, Cost 2, as gh_crossover writes them.
# Decisions in NOTES.md.

# "Command" toggled once each scenario: gh_crossover._once_per_quest's text, with the bear as the target of a control
# (Frosthaven's AddActiveBonus ControlActor on an own summon, Infuser Boon of the Tempest) instead of Filter Self
def _bear(indent):
    return "\n".join(" " * indent + ln for ln in (
        "Filter:", "    TargetType: [OwnSummon]", "    HeroSummonClasses: [WildfuryBearID]", "Target: 1",
        "Targeting: All", "IgnoreLOS: True", "SkipAnim: True"))

PERKS = [
    # a rolling Heal 2 aimed at the Bear (Frosthaven's AMC_Heal1_TBoneshaper_R custom targeting: the summoner there,
    # the Bear here; when the Bear is gone or draws the card itself, it heals the attacker)
    (3, "Lick Wounds", 'Replace one <sprite name="-1"> with one "<sprite name=Heal> 2, <sprite name=Target> Bear" '
     '<sprite name=Rolling>',
     {"CardsToRemove": "[AMC_Basic_M1]", "CardsToAdd": "[AMC_GH2E_Wildfury_Heal2Bear_R]"}),
    (3, "Rending Claws", 'Replace one <sprite name="0"> with one <sprite name="+1"> <sprite name=Wound>',
     {"CardsToRemove": "[AMC_Basic_P0]", "CardsToAdd": "[AMC_Wound_P1]"}),
    # +2, and +1 more while the Wildfury and the Bear are within range 4 of each other (Frosthaven's
    # AMC_P1_Or_P2_IfPlayedTideThisRound: a boolean Strength var on the card's own override)
    (2, "Pack Bond", 'Replace one <sprite name="0"> with one <sprite name="+2"> "Add +1 <sprite name=Attack> if the '
     'Wildfury and the Bear are within <sprite name=Range> 4 of each other"',
     {"CardsToRemove": "[AMC_Basic_P0]", "CardsToAdd": "[AMC_GH2E_Wildfury_P2_Bond]"}),
    (1, "Savage Strike", 'Replace one <sprite name="+2"> with one <sprite name="+4">',
     {"CardsToRemove": "[AMC_Basic_P2]", "CardsToAdd": "[AMC_Basic_P4]"}),
    (2, "Primal Call", 'Add two <sprite name="+1"> <sprite name=CreateAir>/<sprite name=CreateEarth>',
     {"CardsToAdd": "[AMC_GH2E_Wildfury_AirEarth_P1, AMC_GH2E_Wildfury_AirEarth_P1]"}),
    (2, "Bowl Over", 'Add one <sprite name="+2"> <sprite name=Push> 2',
     {"CardsToAdd": "[AMC_GH2E_Wildfury_Push2_P2]"}),
    # APPROXIMATION: the card's Safeguard to an ally is left out (no attack-modifier shape places a bonus on another
    # figure); a plain +1. NEEDS PLUGIN (NOTES.md).
    (1, "Warded Path", 'Ignore scenario effects and add one <sprite name="+1"> "Safeguard, <sprite name=Target> 1 '
     '<sprite name=Allies>"',
     {"CardsToAdd": "[AMC_Basic_P1]", "Flags": "[Passive, IgnoreNegativeScenarioEffects]"}),
    # NEEDS PLUGIN: no data shape changes the modifier an enemy draws against one figure (text only)
    (1, "Thick Hide", 'Whenever the Bear is attacked, treat any <sprite name="2x"> attack modifier card the enemy draws '
     'as <sprite name="0"> instead',
     {"Flags": "[Passive]"}),
    # once each scenario, during your turn: Command the Bear Move 4. The "you may teleport next to the Bear after it
    # left your side" rider is left out (NEEDS PLUGIN, NOTES.md).
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
    # APPROXIMATION: after each long rest (the Brute's Rested and Ready flag, ExecuteAbilitiesAfterLongRest), you may
    # consume Air or Earth (one: Earth only while Air is not infused, as the class's split consumes) to suffer 2 damage
    # and Command: the Bear heals 2. X is fixed at 2 (the sheet lets you choose X >= 1). Untested shape.
    (1, "Life Bond", 'At the start or end of each of your long rests, you may <sprite name=ConsumeAir>/<sprite '
     'name=ConsumeEarth> and reduce your current hit point value by X (X must be at least 1) to Command: <sprite '
     'name=Heal> X',
     {"Cost": 2, "Flags": "[Passive, ExecuteAbilitiesAfterLongRest]", "Abilities": f"""\
    TRestEarth:
        StartAbilityRequirements:
            Type: ThisAbility
            Vars:
                ValidateNoAir: CustomAbility.IsElementInfused(ElementInfusionBoardManager.EElement.Air) == False
        Consume:
            Elements: [Earth]
            Abilities:
                TRestEarthCost:
                    Damage: 2
                    Filter: Self
                    AutoTrigger: True
                TRestEarthHeal:
                    ControlActor:
                        ControlDurationType: ControlForOneAction
                        Abilities:
                            ABearHealE:
                                Heal: 2
                                Filter: Self
{_bear(20)}
    TRestAir:
        Consume:
            Elements: [Air]
            Abilities:
                TRestAirCost:
                    Damage: 2
                    Filter: Self
                    AutoTrigger: True
                TRestAirHeal:
                    ControlActor:
                        ControlDurationType: ControlForOneAction
                        Abilities:
                            ABearHealA:
                                Heal: 2
                                Filter: Self
{_bear(20)}"""}),
]

# Neither mastery fits an existing pattern: "infuse or consume Air or Earth during each of your turns" needs a per-turn
# element tag, "kill 4+ enemies with Command attacks of 7 or more (after bonuses, before modifiers)" a kill tag on the
# Bear's commanded attacks with their printed value (NOTES.md, shared changes).
MASTERIES = []

NEW_CARDS = {
    "AMC_GH2E_Wildfury_Heal2Bear_R": ('MathModifier: "+0"\nRolling: True\nAbilities:\n    AHeal:\n        Heal: 2\n'
                                      '        Filter: [Self, Ally]\n        AutoTrigger: True\n        Targeting: Custom\n'
                                      '        TargetingVars:\n'
                                      '            AddValidActorInRange: AllHeroSummons.Find(obj.Class.ID == "WildfuryBearID") || TargetingActor\n'
                                      '            AddTileInRange: AddValidActorInRange.Tile\n'
                                      '            AddActorToTarget: AddValidActorInRange\n'
                                      '            AddSelectedTile: AddValidActorInRange.Tile\n'),
    "AMC_GH2E_Wildfury_P2_Bond": ('MathModifier: "+2"\nAbilityOverrides:\n    NegCon:\n'
                                  '        ParentName: AMC_GH2E_Wildfury_P2_Bond\n        Vars:\n'
                                  '            Strength: (AllHeroSummons.Any(obj.Class.ID == "WildfuryBearID" && '
                                  'SharedAbilityTargeting.GetDistanceBetweenActorsInHexes(obj, TargetingActor.Summoner || '
                                  'TargetingActor) <= 4))\n'),
    "AMC_GH2E_Wildfury_AirEarth_P1": ('MathModifier: "+1"\nAbilities:\n    InfuseAbility:\n        Infuse: [Air, Earth]\n'
                                      '        ShowElementPicker: True\n'),
    "AMC_GH2E_Wildfury_Push2_P2": 'MathModifier: "+2"\nAbilities:\n    APush:\n        Push: 2\n',
}

STRINGS = {}
