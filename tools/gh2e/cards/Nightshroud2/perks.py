# Nightshroud (2nd edition) perks: the perk sheet "Aesther Nightshroud" (tools/gh2e/classes.json, docs/gh2e/class-notes.md),
# 18 checks in all. gh_crossover's table format: (checkboxes, name, description in sprite markup, {perk fields}).
# Linked perks (boxes joined on the sheet) are one perk with Available 1 and Cost = the joined boxes, as gh_crossover
# writes them. A Tear is a Deathwalker shadow on the map (CreateShadow), as on the cards (NOTES.md).

PERKS = [
    (1, "Smooth Edge", 'Remove one <sprite name="-2">',
     {"CardsToRemove": "[AMC_Basic_M2]"}),
    (2, "Light Step", 'Remove two <sprite name="-1">',
     {"CardsToRemove": "[AMC_Basic_M1, AMC_Basic_M1]"}),
    # "grant one adjacent ally or self: Teleport 2": APPROXIMATION: the Teleport 2 is yours only (a modifier can't grant
    # an ability to another figure cleanly; NOTES.md)
    (1, "Shadow Hop", 'Replace one <sprite name="-1"> with two <sprite name="0"> "After the attack ability, grant one adjacent ally or self: <sprite name=Teleport> 2" <sprite name=Rolling>',
     {"CardsToRemove": "[AMC_Basic_M1]",
      "CardsToAdd": "[AMC_GH2E_Nightshroud2_Teleport2_R, AMC_GH2E_Nightshroud2_Teleport2_R]"}),
    (1, "Calculated Risk", 'Replace one <sprite name="NullModifier"> with one <sprite name="-2"> <sprite name=Shuffle>',
     {"CardsToRemove": "[AMC_Basic_T0]", "CardsToAdd": "[AMC_Basic_M2_Shuffle]"}),
    # unverified: the kill test reads the target after the attack (NOTES.md); the curse is the game's own
    # AddCurseModifiersToEnemyMonsterDecks, called from a self NullTargeting's end (NEW_CARDS)
    (2, "Lingering Doom", 'Replace two <sprite name="0"> with two <sprite name="+1"> "If this attack kills the target, shuffle one <sprite name=Curse> card into the monster attack modifier deck"',
     {"CardsToRemove": "[AMC_Basic_P0, AMC_Basic_P0]",
      "CardsToAdd": "[AMC_GH2E_Nightshroud2_KillCurse_P1, AMC_GH2E_Nightshroud2_KillCurse_P1]"}),
    (2, "Gathering Dark", 'Replace one <sprite name="0"> with one <sprite name="+2"> <sprite name=CreateDark>',
     {"CardsToRemove": "[AMC_Basic_P0]", "CardsToAdd": "[AMC_Dark_P2]"}),
    (2, "Torn Veil", 'Replace one <sprite name="+1"> with one <sprite name="0"> "Place one Tear in a hex adjacent to the target"',
     {"CardsToRemove": "[AMC_Basic_P1]", "CardsToAdd": "[AMC_GH2E_Nightshroud2_Tear_P0]"}),
    (1, "Unfazed", 'Ignore scenario effects and add two <sprite name="+1">',
     {"CardsToAdd": "[AMC_Basic_P1, AMC_Basic_P1]", "Flags": "[Passive, IgnoreNegativeScenarioEffects]"}),
    # the Elementalist's long-rest infusion (gh_crossover ELEMENTALIST_PERKS), Dark only; "may" is automatic
    (1, "Restful Night", "Whenever you long rest, you may <sprite name=CreateDark>",
     {"Flags": "[Passive, ExecuteAbilitiesAfterLongRest]", "Abilities": """\
    BInfuse:
        Infuse: [Dark]
        Filter: Self
        AutoTrigger: True
        SkipAnim: True
        CanUndo: False"""}),
    # gh_crossover's once-each-scenario toggle (_once_per_quest: Perk_BannerSpear_10's persistent DuringTurnAbility
    # toggle, one use), its ability a Tear in an adjacent hex (CreateShadow, range 1)
    (1, "Rift Opener", "Once each scenario, during your turn, place one Tear in an adjacent hex",
     {"Cost": 2, "Flags": "[Passive, ApplyActiveBonusesAtScenarioStart]", "Abilities": """\
    TActive:
        AutoTrigger: True
        AddActiveBonus:
            CreateShadow:
            HelpBoxTooltipLocKey: "GH2E_Nightshroud2_TEAR_PLACE_HELP"
            Range: 1
            Target: 1
            AnimOverload: Shadow Manipulation
            Filter: [Self]
        ActiveBonus:
            Duration: Persistent
            Behaviour: DuringTurnAbility
            XPTracker: [0]
            IsToggleBonus: True
            Hidden: True
            ListLayoutOverride: [$GH2E_Nightshroud2_PERK_10$, Positive_Teleport_Shadow]
            MiscVars:
                OverrideIcon: "'Positive_Teleport_Shadow'"
        AnimOverload: PowerUp"""}),
    # the 1st edition Plagueherald Baneful Hex's DamageOnCurseModApplied bonus (damage to an enemy whose drawn curse
    # applies), held for the whole scenario. A drawn curse always makes the attack deal no damage (null), so this is the
    # printed rule except for curses drawn on attacks that deal no damage anyway.
    (1, "Hexed Retribution", "If a <sprite name=Curse> causes an enemy to deal no <sprite name=Damage> during its attack, that enemy suffers <sprite name=Damage> 3",
     {"Cost": 3, "Flags": "[Passive, ApplyActiveBonusesAtScenarioStart]", "Abilities": """\
    TCurseDamage:
        Damage: 3
        Filter: Self
        AutoTrigger: True
        SkipAnim: True
        ActiveBonus:
            Duration: Persistent
            Behaviour: DamageOnCurseModApplied
            Filter: Enemy
            Hidden: True
        AnimOverload: None"""}),
]

MASTERIES = [
    ("Child of Darkness", "In a single scenario, infuse or consume <sprite name=CreateDark> during each of your turns",
     """\
# The 1st edition crossover's Creature of the Night: Dark infused or consumed on every turn taken, answered by the
# Frosthaven Mods plugin (GHMasteries.cs) from the element records.
CheckAtEndOfScenario: True
BattleGoalCondition:
    - Targets:
        Filter: CountXRounds
        SubFilter: Scenario
        EnemyFamilies: [FrosthavenMods_ElementEveryTurn]
        Elements: Dark
        CharacterIDs:
            ActorID: Nightshroud2ID
        Value: 1"""),
    ("Relentless Shadow", "In a single scenario, perform an attack ability during your turn in 5 consecutive rounds",
     """\
# The plugin's attack streak (GHMasteries.cs, FrosthavenMods_AttackStreak): Value rounds in a row in each of which an
# attack ability of yours happened, whatever damage it dealt.
BattleGoalCondition:
    - Targets:
        Filter: CountXRounds
        SubFilter: Scenario
        EnemyFamilies: [FrosthavenMods_AttackStreak]
        CharacterIDs:
            ActorID: Nightshroud2ID
        Value: 5"""),
]

NEW_CARDS = {
    # "grant one adjacent ally or self: Teleport 2": a granted action (ControlActor, a kind the game's modifier code runs
    # after the attack) on you or an adjacent ally, in Frosthaven's Banner Spear Explosive Epicenter shape (Filter
    # [Self, Ally], Range 1, Target 1, ControlActorType Player)
    "AMC_GH2E_Nightshroud2_Teleport2_R": """\
MathModifier: "+0"
Rolling: True
Abilities:
    AGrantHop:
        ControlActor:
            Abilities:
                AHop:
                    Teleport2: 2
                    StrengthIsBase: True
                    Filter: Self
                    AnimOverload: Teleport Into
                    AnimOverload2: Teleport Outof
            ControlActorType: Player
        Target: 1
        Range: 1
        Targeting: Range
        Filter: [Self, Ally]
        AnimOverload: Grant
""",
    # A modifier card runs only the ability kinds the game's modifier code knows (an AddModifiersToMonsterDeck or a
    # CreateShadow on it is the error screen when drawn). The curse: a self NullTargeting (Frosthaven's AMC_Resonance_P1)
    # whose end (PerformVars OnAbilityEnded, as Frosthaven's AMC_ShowNextAbilityCard_P0) calls the game's own
    # CustomAbility.AddCurseModifiersToEnemyMonsterDecks (Pain Conduit's Redemption) when the attack's target is dead
    # ("||" stops at the first true part, so the curse comes only for a dead target).
    "AMC_GH2E_Nightshroud2_KillCurse_P1": """\
MathModifier: "+1"
Abilities:
    ACurseDeck:
        NullTargeting: True
        Filter: Self
        AutoTrigger: True
        SkipAnim: True
        PerformVars:
            OnAbilityEnded:
                AddCurse: (TargetActor == null) || (TargetActor.IsDead == False) || CustomAbility.AddCurseModifiersToEnemyMonsterDecks(1)
""",
    # The Tear: a CreateShadow on a modifier card isn't run by the game's modifier code; the plugin runs it after the
    # attack (GHModifierAbilities.cs, 2026-11-02), with the attack's target as TargetActor: one hex adjacent to it.
    "AMC_GH2E_Nightshroud2_Tear_P0": """\
MathModifier: "+0"
Abilities:
    ATear:
        CreateShadow:
        CanUndo: False
        HelpBoxTooltipLocKey: "GH2E_Nightshroud2_TEAR_HELP"
        Target: 1
        Targeting: Custom
        TargetingVars:
            AddTilesInRange: CustomAbility.GetAdjacentTiles(TargetActor.Tile)
        AnimOverload: Shadow Manipulation
""",
}

STRINGS = {
    "GH2E_Nightshroud2_PERK_10": "Once each scenario, during your turn, place one Tear in an adjacent hex",
    "GH2E_Nightshroud2_TEAR_HELP": "Place one Tear in a hex adjacent to the target",
}
