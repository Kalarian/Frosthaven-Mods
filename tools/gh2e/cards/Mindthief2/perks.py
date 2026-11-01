# Mindthief (2nd edition) perks, from the perk sheet (tools/gh2e/classes.json "perks", 18 checks in all) in
# gh_crossover's table format: (checkboxes, name, description in sprite markup, {perk fields}).

PERKS = [
    # the modifier's control: Frosthaven's AMC_P0_ControlSummonMove2 (a control ability on a modifier), aimed at the
    # attack's target the way AMC_PushOrPull2_R aims its push (Vars TargetThisActorAutomatically: TargetActor)
    (2, "Mind Tug", 'Replace two <sprite name="-1"> with one <sprite name="0"> "After the attack ability, control the target: <sprite name=Move> 1"',
     {"CardsToRemove": "[AMC_Basic_M1, AMC_Basic_M1]", "CardsToAdd": "[AMC_GH2E_Mindthief2_ControlMove1]"}),
    # +1, and +1 more per negative condition on the target: Frosthaven's AMC_AddAttack1ForEachAllyAdjToTarget_P1 shape
    # (StatIsBasedOnX AddTo) with the count Frosthaven's own cards read (Tokens.CountNegativeConditions())
    (3, "Prey on the Weak", 'Replace one <sprite name="0"> with one <sprite name="+1"> "Add +1 <sprite name=Attack> for each negative condition the target has"',
     {"CardsToRemove": "[AMC_Basic_P0]", "CardsToAdd": "[AMC_GH2E_Mindthief2_P1_PerNegativeCondition]"}),
    (2, "Sharpened Claws", 'Replace one <sprite name="0"> with one <sprite name="+2">',
     {"CardsToRemove": "[AMC_Basic_P0]", "CardsToAdd": "[AMC_Basic_P2]"}),
    (1, "Hobbling Bite", 'Add two <sprite name="+1"> <sprite name=Immobilize>',
     {"CardsToAdd": "[AMC_Immobilize_P1, AMC_Immobilize_P1]"}),
    (3, "Cold Snap", 'Add one <sprite name="+2"> <sprite name=CreateIce>',
     {"CardsToAdd": "[AMC_Ice_P2]"}),
    (2, "Vanish", 'Add one <sprite name="0"> <sprite name=Invisible>, self <sprite name=Rolling>',
     {"CardsToAdd": "[AMC_Invisible_R]"}),
    (1, "Unfazed", 'Ignore scenario effects and add one <sprite name="+1"> <sprite name=Rolling>',
     {"CardsToAdd": "[AMC_P1_R]", "Flags": "[Passive, IgnoreNegativeScenarioEffects]"}),
    # Frosthaven's own data for "a controlled figure draws from the controller's deck": the hidden OverrideAbility on
    # ControlActor that Gloomhaven's Master Influence (Voidwarden) adds, here for the whole scenario. Always on (the
    # perk says "may"): the controlled attack always uses the Mindthief's deck.
    (1, "Puppet Strings", "Whenever you control the attack of an enemy, you may have the enemy use your attack modifier deck",
     {"Flags": "[Passive, ApplyActiveBonusesAtScenarioStart]", "Abilities": """\
    PuppetStrings:
        AutoTrigger: True
        AddActiveBonus:
            Attack: 0
        Filter: Self
        ActiveBonus:
            Behaviour: OverrideAbility
            Duration: Persistent
            ValidAbilityType: ControlActor
            ActiveBonusAbilityOverrides:
                PuppetStringsOverride:
                    ControlActor:
                        UseControllingActorModifierDeck: True
                        UseControllingActorModifierDeckFilter:
                            ActorType: [Enemy, Ally, Neutral, Enemy2]
            Hidden: True
        AnimOverload: None"""}),
    # NEEDS PLUGIN (as the 1st edition's Lying Low): monster focus treating her as last in initiative order
    (1, "Lying Low", "You are considered to be last in initiative order when determining monster focus",
     {"Flags": "[Passive]"}),
    # Frosthaven's after-rest hooks with an Ice-or-Dark consume (the element picker beside the Consume), as the 1st
    # edition Voidwarden's Grave Defense; the control is the cards' own (Attack 1 commanded, melee)
    (2, "Restless Mind", "At the end of each of your rests, you may <sprite name=ConsumeIce> or <sprite name=ConsumeDark> to control one enemy within <sprite name=Range> 5: <sprite name=Attack> 1",
     {"Flags": "[Passive, ExecuteAbilitiesAfterShortRest, ExecuteAbilitiesAfterLongRest]", "Abilities": """\
    RestControl:
        ShowElementPicker: True
        Consume:
            Elements: [Ice, Dark]
            Abilities:
                CControl:
                    ControlActor:
                        Abilities:
                            EnemyWillDoThis:
                                Attack: 1
                                Range: 1
                                Target: 1
                                StrengthIsBase: True
                                RangeIsBase: True
                                TargetIsBase: True
                        ControlDurationType: ControlForOneAction
                        ControlActorType: Player
                    Range: 5
                    Target: 1
                    Filter: Enemy
                    AnimOverload: MindControl
        CanSkip: True
        CanUndo: False"""}),
]

# Only the mastery an existing tag answers: the 1st edition Mindthief's "four different augments, three times each"
# (GHMasteries.cs AugmentsTriggered). "Kill 5 or more enemies with control abilities" has no tag yet (NOTES.md).
MASTERIES = [
    ("Thought Thief", "In a single scenario, trigger the on-attack effect from four different Augments at least 3 times each",
     """\
# NEW TAG: Value different augments whose on-attack abilities fired Amount times (GHMasteries.cs; the augment
# processing in CAbilityAttack is hooked for it)
CheckAtEndOfScenario: True
BattleGoalCondition:
    - Targets:
        Filter: CountXRounds
        SubFilter: Scenario
        EnemyFamilies: [FrosthavenMods_AugmentsTriggered]
        CharacterIDs:
            ActorID: Mindthief2ID
        Value: 4
        Amount: 3"""),
]

NEW_CARDS = {
    "AMC_GH2E_Mindthief2_ControlMove1": (
        'MathModifier: "+0"\n'
        'Abilities:\n'
        '    ControlAbility:\n'
        '        ControlActor:\n'
        '            Abilities:\n'
        '                EnemyWillDoThis:\n'
        '                    Move: 1\n'
        '                    StrengthIsBase: True\n'
        '            ControlDurationType: ControlForOneAction\n'
        '            ControlActorType: Player\n'
        '        Filter: Enemy\n'
        '        AnimOverload: MindControl\n'
        '        Vars:\n'
        '            TargetThisActorAutomatically: TargetActor\n'),
    "AMC_GH2E_Mindthief2_P1_PerNegativeCondition": (
        'MathModifier: "+1"\n'
        'AbilityOverrides:\n'
        '    ABuffAttack:\n'
        '        Flags: [AttackModifierAbilityOverride]\n'
        '        StatIsBasedOnX:\n'
        '            Strength:\n'
        '                BasedOn: Vars\n'
        '                AddTo: True\n'
        '                Vars:\n'
        '                    Value: TargetActor.Tokens.CountNegativeConditions()\n'),
}

STRINGS = {}
