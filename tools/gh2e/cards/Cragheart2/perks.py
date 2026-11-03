# The 2nd edition Cragheart's perks (tools/gh2e/classes.json "perks", Savvas Cragheart sheet; Secretariat agrees), in
# gh_crossover's table format: (checkboxes, name, description in sprite markup, {perk fields}). 18 checks in all.
from gh_crossover import _once_per_quest, _room_revealed

PERKS = [
    (1, "Sure Footing", 'Replace one <sprite name="-2"> with one <sprite name="0">',
     {"CardsToRemove": "[AMC_Basic_M2]", "CardsToAdd": "[AMC_Basic_P0]"}),
    (3, "Steadfast", 'Replace one <sprite name="-1"> with one <sprite name="+1">',
     {"CardsToRemove": "[AMC_Basic_M1]", "CardsToAdd": "[AMC_Basic_P1]"}),
    # a new modifier: +1, rolling, a 1-hex obstacle in an empty hex adjacent to the target (aimed as Frosthaven's own
    # AMC_Damage1Trap_P0 / AMC_CreateLimitedHazardous_P0 aim around the target)
    (3, "Rock Spray", 'Replace one <sprite name="0"> with one <sprite name="+1"> "Create one 1-hex obstacle tile in an empty hex adjacent to the target" <sprite name=Rolling>',
     # a modifier card's Create ability was "Unsupported attackAbility modifier ability type" (the game's error screen, 2e
     # pass 1, 2026-11-01 03:09, Avalanche); the plugin runs it after the attack since 2026-11-02 (GHModifierAbilities.cs)
     {"CardsToRemove": "[AMC_Basic_P0]", "CardsToAdd": "[AMC_GH2E_Cragheart2_Obstacle_P1_R]"}),
    (2, "Rumbling Shove", 'Replace one <sprite name="0"> with two <sprite name="+1"> <sprite name=Push> 1',
     {"CardsToRemove": "[AMC_Basic_P0]", "CardsToAdd": "[AMC_Push1_P1, AMC_Push1_P1]"}),
    (3, "Grasping Earth", 'Add one <sprite name="+2"> <sprite name=Immobilize>',
     {"CardsToAdd": "[AMC_Immobilize_P2]"}),
    (1, "Dust Cloud", 'Ignore negative scenario effects and add one <sprite name="0"> <sprite name=Muddle> <sprite name=Rolling>',
     {"CardsToAdd": "[AMC_Muddle_R]", "Flags": "[Passive, IgnoreNegativeScenarioEffects]"}),
    (1, "Thick Skin", 'Ignore item <sprite name="-1"> effects',
     {"Flags": "[Passive, IgnoreMinusOneItemEffects]"}),
    # Frosthaven's once-per-scenario toggle (as the 1st edition crossover perks): destroy one obstacle within range 5,
    # then a teleport whose only valid hex is the freed one (IsValidTeleportTile on the destroy's selected tile, the
    # Infuser Formless Bladestorm form: Vars list + MiscVars test)
    (1, "Boulder Leap", 'Once each scenario, during your turn, you may destroy one 1-hex obstacle within <sprite name=Range> 5 to <sprite name=Teleport> to the hex it occupied',
     {"Flags": "[Passive, ApplyActiveBonusesAtScenarioStart]",
      "Abilities": _once_per_quest("PERK_Cragheart2_08_Desc", "Positive_DamagePrevent_Shadow_Teleport", """\
            DestroyObstacle: True
            Range: 5
            Target: 1
            Targeting: Range
            AnimOverload: PowerUp
            SubAbilities:
                PTeleport:
                    Teleport2: 5
                    Filter: Self
                    TreatAsNonSubAbility: True
                    Vars:
                        FreedTiles: ParentAbility.TilesSelected
                    MiscVars:
                        IsValidTeleportTile: FreedTiles.Contains(tile)
                    AnimOverload: Teleport Into
                    AnimOverload2: Teleport Outof""")}),
    # Frosthaven's after-rest perk hooks (both rests, as gh_crossover's Remodeling and Grave Defense), a skippable
    # destroy within range 3 whose sub-ability infuses Earth (Infuser Onyx Shards' infuse sub-ability). Verify on the bench.
    (1, "Earthen Rest", 'At the end of each of your rests, you may destroy one 1-hex obstacle within <sprite name=Range> 3 to <sprite name=CreateEarth>',
     {"Flags": "[Passive, ExecuteAbilitiesAfterShortRest, ExecuteAbilitiesAfterLongRest]", "Abilities": """\
    BDestroy:
        DestroyObstacle: True
        Range: 3
        Target: 1
        Targeting: Range
        CanSkip: True
        CanUndo: False
        AnimOverload: PowerUp
        SubAbilities:
            BEarth:
                Infuse: [Earth]
                TreatAsNonSubAbility: True
                AutoTrigger: True
                SkipAnim: True"""}),
    # the 1st edition crossover's Earthquakes (two linked checks): the revealed room's enemies are moved 1 as a
    # controlled move (a controlled move always ends in an empty hex)
    (1, "Earthquakes", "Whenever a new room is revealed, control all enemies in the newly revealed room: <sprite name=Move> 1, this movement must end in an empty hex",
     {"Cost": 2, "Flags": "[Passive, ExecuteAbilitiesAfterRoomRevealed, ApplyActiveBonusesAtScenarioStart]",
      "Abilities": _room_revealed("""\
        ControlActor:
            ControlActorType: Player
            Abilities:
                AQuake:
                    Move: 1
                    StrengthIsBase: True
        Filter: Enemy""")}),
]

# the 2e masteries ("create, destroy or move an obstacle every round"; "8+ heal in a round, 8+ damage with a ranged and
# with a melee attack") have no existing battle-goal pattern: left out (see NOTES.md)
MASTERIES = []

NEW_CARDS = {
    "AMC_GH2E_Cragheart2_Obstacle_P1_R": 'MathModifier: "+1"\nRolling: True\nAbilities:\n    AObstacle:\n'
                                         '        Create: RockSingle\n        Target: 1\n        TileFilter: EmptyHex\n'
                                         '        Targeting: Custom\n        AnimOverload: TerrainFormation\n'
                                         '        TargetingVars:\n'
                                         '            AddTilesInRange: CustomAbility.GetAdjacentTiles(TargetActor.Tile).Where(obj.IsWalkable)\n',
}

STRINGS = {
    # the new modifier's name (its file's Name is looked up as text)
    "AMC_GH2E_Cragheart2_Obstacle_P1_R": "+1, create one 1-hex obstacle tile in an empty hex adjacent to the target",
}
